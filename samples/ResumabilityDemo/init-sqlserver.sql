-- Create the cache database and table for SQL Server distributed cache
-- This script is run by the sqlserver-init container

USE master;
GO

-- Create database if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'McpCache')
BEGIN
    CREATE DATABASE McpCache;
    PRINT 'Database McpCache created.';
END
GO

USE McpCache;
GO

-- Create the cache table using the schema expected by Microsoft.Extensions.Caching.SqlServer
-- See: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-9.0#distributed-sql-server-cache
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SseEventCache]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SseEventCache] (
        [Id] NVARCHAR(449) NOT NULL PRIMARY KEY,
        [Value] VARBINARY(MAX) NOT NULL,
        [ExpiresAtTime] DATETIMEOFFSET NOT NULL,
        [SlidingExpirationInSeconds] BIGINT NULL,
        [AbsoluteExpiration] DATETIMEOFFSET NULL
    );
    
    -- Create index on expiration time for efficient cleanup
    CREATE NONCLUSTERED INDEX [IX_SseEventCache_ExpiresAtTime] 
        ON [dbo].[SseEventCache]([ExpiresAtTime]);
    
    PRINT 'Table SseEventCache created.';
END
ELSE
BEGIN
    PRINT 'Table SseEventCache already exists.';
END
GO
