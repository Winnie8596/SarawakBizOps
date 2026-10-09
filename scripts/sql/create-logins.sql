-- Least-privilege logins for the API (sqlcmd script; run as sa by the db-init service in docker-compose.yml, and by
-- DemoApiFactory in the tests). Variables: DatabaseName, AppLogin, AppPassword, AiLogin, AiPassword.
--
--   AppLogin  the API itself. Its own database only, no server rights: read and write the data in dbo, use its
--             sequence, and change the schema (db_ddladmin) because the demo migrates on startup. The stock ledger
--             (schema "ledger", BR-09) is append-only for it: SELECT and INSERT, with UPDATE and DELETE denied.
--   AiLogin   the AI features (ConnectionStrings:AiReadOnly). SELECT only, so SQL Server refuses a write even if the
--             in-app ReadOnlyGuard were bypassed.
--
-- Safe to run on every start: it creates what is missing, and re-applies the passwords so a changed .env value
-- takes effect without wiping the volume. Passwords must not contain a single quote.
:on error exit
SET NOCOUNT ON;

IF DB_ID(N'$(DatabaseName)') IS NULL
    EXEC(N'CREATE DATABASE [$(DatabaseName)]');

IF SUSER_ID(N'$(AppLogin)') IS NULL
    EXEC(N'CREATE LOGIN [$(AppLogin)] WITH PASSWORD = N''$(AppPassword)'', DEFAULT_DATABASE = [$(DatabaseName)]');
ELSE
    EXEC(N'ALTER LOGIN [$(AppLogin)] WITH PASSWORD = N''$(AppPassword)''');

IF SUSER_ID(N'$(AiLogin)') IS NULL
    EXEC(N'CREATE LOGIN [$(AiLogin)] WITH PASSWORD = N''$(AiPassword)'', DEFAULT_DATABASE = [$(DatabaseName)]');
ELSE
    EXEC(N'ALTER LOGIN [$(AiLogin)] WITH PASSWORD = N''$(AiPassword)''');
GO

-- After a restart SQL Server answers (the compose healthcheck passes) while it is still starting its databases, and
-- USE fails with error 904 ("cannot be autostarted during server shutdown or startup"). Retry it for up to two minutes.
DECLARE @waited int = 0;
WHILE 1 = 1
BEGIN
    BEGIN TRY
        EXEC(N'USE [$(DatabaseName)];');
        BREAK;
    END TRY
    BEGIN CATCH
        IF @waited >= 120 THROW;
        WAITFOR DELAY '00:00:01';
        SET @waited += 1;
    END CATCH
END
GO

USE [$(DatabaseName)];

IF USER_ID(N'$(AppLogin)') IS NULL
    CREATE USER [$(AppLogin)] FOR LOGIN [$(AppLogin)];
-- Schema-level grants cover every table and the report-number SEQUENCE (NEXT VALUE FOR needs UPDATE), including
-- objects the migrations create later. REFERENCES lets the migrations add foreign keys.
GRANT SELECT, INSERT, UPDATE, DELETE, REFERENCES ON SCHEMA::dbo TO [$(AppLogin)];
ALTER ROLE db_ddladmin ADD MEMBER [$(AppLogin)];

-- The ledger schema is created here, before the migrations move InventoryTransactions into it, so the permissions
-- below hold from the very first row. Schema-level, so they also cover the table once it arrives. A DENY wins over
-- any GRANT, so no later grant to the app login can reopen the ledger to edits.
IF SCHEMA_ID(N'ledger') IS NULL
    EXEC(N'CREATE SCHEMA [ledger] AUTHORIZATION [dbo]');
GRANT SELECT, INSERT, REFERENCES ON SCHEMA::ledger TO [$(AppLogin)];
DENY UPDATE, DELETE ON SCHEMA::ledger TO [$(AppLogin)];

IF USER_ID(N'$(AiLogin)') IS NULL
    CREATE USER [$(AiLogin)] FOR LOGIN [$(AiLogin)];
ALTER ROLE db_datareader ADD MEMBER [$(AiLogin)];
GO
