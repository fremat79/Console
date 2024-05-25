// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Configuration;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.Smo;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

var config =  new ConfigurationBuilder().AddJsonFile("config.json").Build();
var srcServer =  config.GetSection("Configuration").GetSection("Server");
var skipTables = config.GetSection("Configuration").GetSection("SkipTables").GetChildren();


Helper h = new();

string dbName = srcServer["DbName"];

DateTime begin = DateTime.Now;
DateTime end = DateTime.Now;

Trace.Listeners.Clear();

List<string> skipDataTables = new List<string>( skipTables.Select( cs => cs.Value) );

if (Directory.Exists("script"))
{
    Directory.Delete(@"script", true);
}

Directory.CreateDirectory(@"script\00_db");
Directory.CreateDirectory(@"script\01_tables");
Directory.CreateDirectory(@"script\02_sp");
Directory.CreateDirectory(@"script\03_views");
Directory.CreateDirectory(@"script\04_udfs");
Directory.CreateDirectory(@"script\05_triggers");
Directory.CreateDirectory(@"script\06_data");
Directory.CreateDirectory(@"script\06_data\00_formats");

Server myServer = new Server(srcServer["name"]);


myServer.ConnectionContext.LoginSecure = string.IsNullOrEmpty(srcServer["UserName"]);

if (!myServer.ConnectionContext.LoginSecure)
{
    myServer.ConnectionContext.Login = srcServer["UserName"];
    myServer.ConnectionContext.Password = srcServer["Password"];
}
myServer.ConnectionContext.Connect();

Database atsDB = myServer.Databases[dbName];

var scriptOptions = new ScriptingOptions
{
    AppendToFile = true,
    ScriptData = false,
    ToFileOnly = true,
    ExtendedProperties = true,
    IncludeIfNotExists = true,
    Permissions = true,
    IncludeDatabaseRoleMemberships = true,
    
};

var scripter = new Scripter(myServer);
scripter.Options = scriptOptions;

#region SCRIPT DB CREATION

h.SetColor(ConsoleColor.DarkBlue);
h.StartOperation("CreateDB");
scriptOptions.FileName = @"script\00_db\00_create-db.sql";
scripter.Script(new Urn[] { atsDB.Urn });

#region POST PROCESS DB CREATION SCRIPT

Regex name = new(@"IF NOT EXISTS \(SELECT name FROM sys\.databases WHERE name = N'(?<dbName>\w*)'");
Regex filesName = new(@"FILENAME = \w''(?<dataPath>.*)''");

var createDBFilesSqlScript = File.ReadAllText(scriptOptions.FileName);
var dbNameMatch = name.Match(createDBFilesSqlScript);
var restoredDbName = string.Empty;

if (dbNameMatch.Success)
{
    var originalDBName = dbNameMatch.Groups["dbName"].Value;
    restoredDbName = $"{originalDBName}_rebuild";
    createDBFilesSqlScript =  createDBFilesSqlScript.Replace(originalDBName, restoredDbName);
    
}
createDBFilesSqlScript = createDBFilesSqlScript.Replace("'", "''");

var filesMatch = filesName.Matches(createDBFilesSqlScript);

if (filesMatch.Count > 0)
{         
  
    createDBFilesSqlScript = Regex.Replace(createDBFilesSqlScript, @"FILENAME = \w''(?<dataPath>.*)''", m => 
    {    
        var fileName = Path.GetFileName(m.Groups["dataPath"].Value);    
        return $"FILENAME = ''' + @DataPath + '{fileName}''";
    });

    var scriptChunks = createDBFilesSqlScript.Split($"GO{Environment.NewLine}");

    StringBuilder sb = new();

    Array.ForEach(scriptChunks, sc =>
    {
        if (!string.IsNullOrEmpty(sc))
        {
            var x = $"SET @script = '{sc}'";
            var y = "EXECUTE sp_executesql  @script";
            sb.AppendLine(x);
            sb.AppendLine(y);
        }
    });

    string headerScript = "DECLARE @DataPath NVARCHAR(MAX)\r\nDECLARE @LogPath NVARCHAR(MAX)\r\n\r\nSELECT\r\n  @DataPath = CONVERT(NVARCHAR(MAX), SERVERPROPERTY('instancedefaultdatapath')) ,\r\n  @LogPath = CONVERT(NVARCHAR(MAX), SERVERPROPERTY('instancedefaultlogpath'))\r\n\r\nDECLARE @script NVARCHAR(MAX)";

    using (var sqlScript = File.CreateText("script\\00_db\\00_create-db.sql"))
    {
        sqlScript.WriteLine(headerScript);
        sqlScript.WriteLine(sb.ToString()); 
    }
}

using (var sqlScript = File.CreateText("script\\restore.bat"))
{
    var assembly = Assembly.GetExecutingAssembly();
    var resourceName = assembly.GetManifestResourceNames()
      .First(str => str.EndsWith("restore.bat"));
    var restore_script = string.Empty;
    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
    using (StreamReader reader = new StreamReader(stream))
    {
        restore_script = reader.ReadToEnd();
    }
    sqlScript.WriteLine(restore_script.Replace("<restore_db_name>", restoredDbName));
}
#endregion

h.EndOperation();
h.RestoreColor();
#endregion

#region SCRIPT SCHEMA CREATION

scriptOptions.FileName = @"script\00_schemas.sql";
h.WriteLine($"Found {atsDB.Schemas.Cast<Schema>().Where(s => !s.IsSystemObject).Count()} schema").SetRandomColor();

foreach (Schema s in atsDB.Schemas)
{    
    if (!s.IsSystemObject)
    {
        h.StartOperation($"\tCreate schema {s.Name}");
        var urn = new Urn[] { s.Urn };
        scripter.Script(urn);
        h.EndOperation();
    }
}
#endregion

#region SCRIPT ROLES CREATION

scriptOptions.FileName = @"script\01_roles.sql";
h.WriteLine($"Found {atsDB.Roles.Cast<DatabaseRole>().Count()} roles").SetRandomColor();

foreach (DatabaseRole r in atsDB.Roles)
{
    h.StartOperation($"\tCreate role {r.Name}");
    scripter.Script(new Urn[] { r.Urn });
    h.EndOperation();
}
#endregion

#region SCRIPT USERS CREATION

scriptOptions.FileName = @"script\02_users.sql";
h.WriteLine($"Found {atsDB.Users.Cast<User>().Where(u => !u.IsSystemObject && u.LoginType == LoginType.SqlLogin).Count()} users").SetRandomColor();
foreach (User u in atsDB.Users)
{    
    if (!u.IsSystemObject && u.LoginType == LoginType.SqlLogin)
    {
        h.StartOperation($"\tCreate user {u.Name}");
        scripter.Script(new Urn[] { u.Urn });        
        h.EndOperation();
    }
}
#endregion

scriptOptions.WithDependencies = true;
scriptOptions.DriAll = true;
scriptOptions.DriForeignKeys = false;
scriptOptions.DriChecks = true;
scriptOptions.Indexes = true;

scriptOptions.Default = true;

List<ForeignKeyCollection> fkcolList = new();
List<TriggerCollection> tcolList = new();
List<DefaultConstraint> dfConstraintList = new();

#region TABLES

#region SCRIPT TABLE CREATION

h.WriteLine($"Found {atsDB.Tables.Cast<Table>().Where(s => !s.IsSystemObject).Count()} tables").SetRandomColor();
int i = 0;
foreach (Table t in atsDB.Tables)
{
    if (!t.IsSystemObject)
    {
        scriptOptions.FileName = @$"script\01_tables\{i:0000}_{t.Schema}.{t.Name}.sql";
        h.StartOperation($"\tCreate table {t.Schema}.{t.Name}");

        var urn = new Urn[] { t.Urn };
        //var dep = scripter.DiscoverDependencies(urn, true);
        scripter.Script(new Urn[] { t.Urn });

        fkcolList.Add(t.ForeignKeys);
        tcolList.Add(t.Triggers);

        foreach (Column c in t.Columns)
        {
            if (c.DefaultConstraint != null)
            {
                dfConstraintList.Add(c.DefaultConstraint);
            }
        }
        h.EndOperation();        
    }
    i++;
}
#endregion

#region SCRIPT FSKS CREATION

h.WriteLine($"Found {fkcolList.Count} ForeignKeyCollection").SetRandomColor();
i++;
using (var fkStream = File.CreateText(@$"script\01_tables\{i:0000}_table_fks.sql"))
{
    h.StartOperation("\t Create tables ForeignKey");
    foreach (ForeignKeyCollection fkcol in fkcolList) // Generate Relations
    {
        foreach (ForeignKey fk in fkcol)
        {
            Console.WriteLine($"{Now()} Create fk {fk.Name}");
            StringCollection lines = fk.Script();
            foreach (string line in lines)
            {
                fkStream.WriteLine(line);
            }
        }
    }
    fkcolList.Clear();
    h.EndOperation();
}
#endregion

#region SCRIPT DFCS CREATION
h.WriteLine($"Found {fkcolList.Count} df").SetRandomColor();
i++;
using (var dfConstraintStream = File.CreateText(@$"script\01_tables\{i:0000}_table_df.sql"))
{
    h.StartOperation("\t Create DefaultConstraint");
    foreach (DefaultConstraint dfC in dfConstraintList)
    {
        StringCollection lines = dfC.Script();
        foreach( string line in lines)
        { 
            dfConstraintStream.WriteLine(line);
        }
    }
    dfConstraintList.Clear();
    h.EndOperation();
}
#endregion

#region SCRIPT TABLE TRIGGER CREATION
h.WriteLine($"Found {tcolList.Count} table triggers").SetRandomColor();
i++;
using (var dfConstraintStream = File.CreateText(@$"script\01_tables\{i:0000}_table_t.sql"))
{
    h.StartOperation("\t Create Tables triggers");
    foreach (TriggerCollection tList in tcolList)
    {
        foreach (Trigger t in tList)
        {
            StringCollection lines = t.Script();
            foreach (string line in lines)
            {
                dfConstraintStream.WriteLine(line);
            }
        }        
    }
    tcolList.Clear();
    h.EndOperation();
}
#endregion

#endregion

#region SCRIPT STORED PROCEDURE
h.WriteLine($"Found {atsDB.StoredProcedures.Cast<StoredProcedure>().Where(s => !s.IsSystemObject).Count()} StoredProcedures").SetRandomColor();
i = 0 ;
foreach (StoredProcedure sp in atsDB.StoredProcedures)
{
    if (!sp.IsSystemObject)
    {
        h.StartOperation($"\tCreate sp {sp.Name}");
        scriptOptions.FileName = @$"script\02_sp\{i:0000}_{sp.Name}.sql";
        scripter.Script(new Urn[] { sp.Urn });
        h.EndOperation();
    }
    i++;
}
#endregion

#region SCRIPT VIEW

h.WriteLine($"Found {atsDB.Views.Cast<View>().Where(s => !s.IsSystemObject).Count()} Views").SetRandomColor();
i = 0;
foreach (View v in atsDB.Views)
{
    if (!v.IsSystemObject)
    {
        h.StartOperation($"Create {v.Schema}.{v.Name}");
        scriptOptions.FileName = @$"script\03_views\{i:0000}_{v.Schema}.{v.Name}.sql";        
        scripter.Script(new Urn[] { v.Urn });
        h.EndOperation();
    }
    i++;
}
#endregion

#region SCRIPT FUNCTION
h.WriteLine($"Found {atsDB.UserDefinedFunctions.Cast<UserDefinedFunction>().Where(s => !s.IsSystemObject).Count()} UDF").SetRandomColor();
i = 0;
foreach (UserDefinedFunction f in atsDB.UserDefinedFunctions)
{
    if (!f.IsSystemObject)
    {
        h.StartOperation($"Create {f.Schema}.{f.Name}");
        scriptOptions.FileName = @$"script\04_udfs\{i:0000}_{f.Schema}.{f.Name}.sql";        
        scripter.Script(new Urn[] { f.Urn });
        h.EndOperation();
    }
    i++;
}

#endregion

#region SCRIPT TRIGGER
h.WriteLine($"Found {atsDB.Triggers.Cast<Trigger>().Where(s => !s.IsSystemObject).Count()} trigger").SetRandomColor();
i = 0 ;
foreach (Trigger t in atsDB.Triggers)
{
    if (!t.IsSystemObject)
    {
        h.StartOperation($"\tCreate {t.Name}");
        scriptOptions.FileName = @$"script\05_triggers\{i:0000}_{t.Name}.sql";        
        scripter.Script(new Urn[] { t.Urn });
        h.EndOperation();
    }
    i++;
}
#endregion

scriptOptions.ScriptData = true;
scriptOptions.WithDependencies = false;
scriptOptions.ScriptDataCompression = true;
scriptOptions.ScriptSchema = false;

#region SCRIPT DATA

h.WriteLine($"{Now()} Start generate data...");
i = 0;
foreach (Table t in atsDB.Tables)
{
    if (ScriptData(t) && !t.IsSystemObject)
    {        
        h.StartOperation($"\tData for {t.Name} ready");
        scriptOptions.FileName = @$"script\06_data\{i:0000}_{t.Schema}.{t.Name}.sql";
        var urn = new Urn[] { t.Urn };

        if (t.Name != "Format")
        {
            scripter.EnumScript(new Urn[] { t.Urn }).ToList();
        }
        else
        {
            var rows = t.RowCount;
            var currentFormat = 0;
            var scripts = t.EnumScript(new ScriptingOptions() { ScriptSchema = false, ScriptData = true });

            foreach (var script in scripts)
            {
                using (var format = File.CreateText(@$"script\06_data\00_formats\{currentFormat:0000}_format.sql"))
                {
                    format.WriteLine(script);
                }
                currentFormat++;
            }                           
        }
        h.EndOperation();
        
        i++;
    }
}
#endregion

end = DateTime.Now;
TimeSpan duration = end - begin;
Console.WriteLine($"{Now()} Dump take {duration.ToString()}");

Console.WriteLine($"{Now()} Start compression");

if (File.Exists("database_dump.zip"))
    File.Delete("database_dump.zip");

ZipFile.CreateFromDirectory("script", "database_dump.zip", CompressionLevel.SmallestSize, true);

Console.WriteLine($"{Now()} Compressed dump ready");

if (Directory.Exists("script"))
{
    Directory.Delete(@"script", true);
}

bool ScriptData(Table t)
{
    var skipScriptData = skipDataTables.Contains($"{t.Schema}.{t.Name}", StringComparer.InvariantCultureIgnoreCase);
    return !skipScriptData;
}

string Now()
{
    return DateTime.Now.ToString("hh:mm:ss");
}


public class Helper
{
    ConsoleColor _originalColor;
    DateTime _startTime;
    DateTime _endTime;
    string _operation;
    public Helper()
    {
        _originalColor = Console.ForegroundColor;
    }
    public Helper WriteLine(string value)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;        
        Console.WriteLine($"{DateTime.Now:hh:mm:ss} ${value}");
        Console.ForegroundColor = _originalColor;     
        return this;
    }
    public void SetColor(ConsoleColor color)
    {
        Console.ForegroundColor = color;
    }
    public void RestoreColor()
    {
        Console.ForegroundColor = _originalColor;
    }
    public void StartOperation(string name)
    {
        _startTime = DateTime.Now;
        _operation = name;
    }
    public void SetRandomColor()
    {
        ConsoleColor c;
        do {
            c = (ConsoleColor)Random.Shared.Next(1, 15);
        } while (c == ConsoleColor.DarkGreen);

        Console.ForegroundColor = c;  
    }


    public void EndOperation()
    {
        _endTime = DateTime.Now;
        var s = $"{_startTime.ToLongTimeString()} {_operation} takes {Math.Round((_endTime - _startTime).TotalSeconds,2)} sec.";
        Console.WriteLine(s);
    }    
}