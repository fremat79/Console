set db_name=<restore_db_name>
set db_user=<replace_with_target_server_username_here>
set db_pwd=<replace_with_target_server_password_here>


cd 00_db
sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -i 00_create-db.sql -o ..\log_create_db.txt
cd..

for %%f in (*.sql) do (
sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -i %%f -o ..\log_create_shemas_roles_users.txt
)

cd 01_tables
for %%f in (*.sql) do (
sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -i %%f -o ..\log_create_tables.txt
)
cd..

cd 02_sp
for %%f in (*.sql) do (
sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -i %%f -o ..\log_create_stored_procedure.txt
)
cd..

cd 03_views
for %%f in (*.sql) do (
sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -i %%f -o ..\log_create_views.txt
)
cd..

cd 04_udfs
for %%f in (*.sql) do (
sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -i %%f -o ..\log_create_udfs.txt
)
cd..

cd 05_triggers
for %%f in (*.sql) do (
sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -i %%f -o ..\log_create_triggers.txt
)
cd..


sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -Q "EXEC sp_msforeachtable ""ALTER TABLE ? NOCHECK CONSTRAINT all""" -o ..\log_disable_constraints.txt
cd 06_data

for %%f in (*.sql) do (
sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -i %%f -o ..\log_populate_data.txt
)

cd 00_formats
for %%f in (*.sql) do (
sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -i %%f -o ..\..\log_populate_formats.txt
)

cd..
cd..

sqlcmd -S srvranorex3\MSSQLServer_2017 -U sa -P @!TestAutom20 -d %db_name% -Q "EXEC sp_msforeachtable ""ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all""" -o log_enable_constraints.txt

