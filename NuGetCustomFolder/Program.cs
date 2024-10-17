using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGetCustomFolder
{
    internal class Program
    {
        static void Main(string[] args)
        {

            var d = Newtonsoft.Json.JsonConvert.SerializeObject(new { Name = "John Doe" });

            //https://stackoverflow.com/questions/55946010/how-to-specify-output-folder-for-the-referenced-nuget-packages

        }
    }
}
