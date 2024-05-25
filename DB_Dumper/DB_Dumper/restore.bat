set db_server_name=%1
set db_server_user=%2
set db_server_pwd=%3

set db_name=<restore_db_name>

cd 00_db
sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -i 00_create-db.sql -o ..\log_create_db.txt
cd..

for %%f in (*.sql) do (
sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -i %%f -o ..\log_create_shemas_roles_users.txt
)

cd 01_tables
for %%f in (*.sql) do (
sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -i %%f -o ..\log_create_tables.txt
)
cd..

cd 02_sp
for %%f in (*.sql) do (
sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -i %%f -o ..\log_create_stored_procedure.txt
)
cd..

cd 03_views
for %%f in (*.sql) do (
sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -i %%f -o ..\log_create_views.txt
)
cd..

cd 04_udfs
for %%f in (*.sql) do (
sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -i %%f -o ..\log_create_udfs.txt
)
cd..

cd 05_triggers
for %%f in (*.sql) do (
sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -i %%f -o ..\log_create_triggers.txt
)
cd..


sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -Q "EXEC sp_msforeachtable ""ALTER TABLE ? NOCHECK CONSTRAINT all""" -o ..\log_di%db_server_user%ble_constraints.txt
cd 06_data

for %%f in (*.sql) do (
sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -i %%f -o ..\log_populate_data.txt
)

cd 00_formats
for %%f in (*.sql) do (
sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -i %%f -o ..\..\log_populate_formats.txt
)

cd..
cd..

sqlcmd -S %db_server_name% -U %db_server_user% -P %db_server_pwd% -d %db_name% -Q "EXEC sp_msforeachtable ""ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all""" -o log_enable_constraints.txt