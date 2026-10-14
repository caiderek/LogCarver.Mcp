-- Throwaway test database for trying LogCarver.Mcp. Drop it when you are done.
-- Run this in SSMS or sqlcmd. Do not run CHECKPOINT or back up the log afterwards,
-- or the delete records may disappear from the log.

IF DB_ID('McpTest') IS NOT NULL
BEGIN
    ALTER DATABASE McpTest SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE McpTest;
END
GO
CREATE DATABASE McpTest;
GO
ALTER DATABASE McpTest SET RECOVERY FULL;
GO
-- A full backup makes FULL recovery really take effect (written to NUL, no disk used).
BACKUP DATABASE McpTest TO DISK = 'NUL';
GO
USE McpTest;
GO

CREATE TABLE dbo.Orders (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Customer  NVARCHAR(50)  NOT NULL,
    Amount    DECIMAL(10,2) NOT NULL,
    Note      NVARCHAR(100) NULL,
    CreatedAt DATETIME2(0)  NOT NULL DEFAULT SYSDATETIME()
);
GO
INSERT dbo.Orders (Customer, Amount, Note) VALUES
 (N'Alice', 100.00, N'first'),
 (N'Bob',   250.50, NULL),
 (N'Carol', 80.00,  N'中文備註'),
 (N'Dave',  999.99, N'big'),
 (N'Eve',   10.00,  NULL),
 (N'Frank', 42.00,  N'keep'),
 (N'Grace', 55.50,  N'keep'),
 (N'Heidi', 70.00,  N'keep'),
 (N'Ivan',  15.00,  N'keep'),
 (N'Judy',  5.00,   N'keep');
GO

DELETE FROM dbo.Orders WHERE Id IN (2, 3, 4);    -- Bob, Carol, Dave
GO
UPDATE dbo.Orders SET Amount = 111.11, Note = N'changed' WHERE Id = 1;
GO
DELETE FROM dbo.Orders WHERE Id = 5;             -- Eve
GO

SELECT * FROM dbo.Orders;   -- 6 rows left: Id 1, 6, 7, 8, 9, 10
