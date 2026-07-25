-- S001: Initial Schema — All 15 Tables
-- Engine: SQLite (System.Data.SQLite)
-- Sprint: 1
-- Description: Creates the complete database schema for BomAddIn V1.0

-- ==============================
-- §4.1 Business Tables (8)
-- ==============================

-- 1. Materials (Owned)
CREATE TABLE IF NOT EXISTS Materials (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    OrgId       INTEGER NOT NULL,
    Code        TEXT NOT NULL,
    Name        TEXT NOT NULL,
    Spec        TEXT DEFAULT '',
    Unit        TEXT DEFAULT 'PCS',
    Category    TEXT DEFAULT '',
    IsActive    INTEGER NOT NULL DEFAULT 1,
    CreatedAt   TEXT NOT NULL DEFAULT (datetime('now')),
    UpdatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_Materials_OrgCode ON Materials(OrgId, Code);

-- 2. BomStructures (Owned) — Adjacency List Model
CREATE TABLE IF NOT EXISTS BomStructures (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    OrgId           INTEGER NOT NULL,
    ParentMaterialId INTEGER NOT NULL REFERENCES Materials(Id) ON DELETE CASCADE,
    ChildMaterialId  INTEGER NOT NULL REFERENCES Materials(Id) ON DELETE CASCADE,
    Quantity        REAL NOT NULL DEFAULT 1.0,
    Position        TEXT DEFAULT '',        -- Reference designator
    ScrapRate       REAL DEFAULT 0.0,      -- Scrap rate (0~1)
    BomViewType     TEXT DEFAULT 'EBOM',   -- EBOM/MBOM/CBOM
    Level           INTEGER NOT NULL DEFAULT 0,
    ValidFrom       TEXT NOT NULL,         -- ISO 8601 date
    ValidTo         TEXT,                  -- NULL = currently valid
    VersionState    TEXT NOT NULL DEFAULT 'Draft',  -- Draft/Released/Obsolete
    CreatedAt       TEXT NOT NULL DEFAULT (datetime('now')),
    UpdatedAt       TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS IX_BomStructures_Parent ON BomStructures(ParentMaterialId, ValidFrom);
CREATE INDEX IF NOT EXISTS IX_BomStructures_Child ON BomStructures(ChildMaterialId);

-- 3. Suppliers (Owned)
CREATE TABLE IF NOT EXISTS Suppliers (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    OrgId     INTEGER NOT NULL,
    Code      TEXT NOT NULL,
    Name      TEXT NOT NULL,
    Contact   TEXT DEFAULT '',
    Rating    INTEGER DEFAULT 0,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_Suppliers_OrgCode ON Suppliers(OrgId, Code);

-- 4. Prices (Synced-RO from ERP)
CREATE TABLE IF NOT EXISTS Prices (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    OrgId         INTEGER NOT NULL,
    MaterialId    INTEGER NOT NULL REFERENCES Materials(Id) ON DELETE CASCADE,
    SupplierId    INTEGER NOT NULL REFERENCES Suppliers(Id) ON DELETE CASCADE,
    UnitPrice     REAL NOT NULL DEFAULT 0,
    Currency      TEXT NOT NULL DEFAULT 'CNY',
    DataVersion   INTEGER NOT NULL DEFAULT 0,
    EffectiveDate TEXT NOT NULL,
    CreatedAt     TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS IX_Prices_MaterialVersion ON Prices(MaterialId, DataVersion);

-- 5. Inventories (Synced-RO from ERP)
CREATE TABLE IF NOT EXISTS Inventories (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    OrgId         INTEGER NOT NULL,
    MaterialId    INTEGER NOT NULL REFERENCES Materials(Id) ON DELETE CASCADE,
    WarehouseId   TEXT NOT NULL,
    Quantity      REAL NOT NULL DEFAULT 0,
    DataVersion   INTEGER NOT NULL DEFAULT 0,
    SnapshotDate  TEXT NOT NULL,
    CreatedAt     TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS IX_Inventories_MaterialWarehouse ON Inventories(MaterialId, WarehouseId, DataVersion);

-- 6. Orders (Synced-RO from ERP)
CREATE TABLE IF NOT EXISTS Orders (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    OrgId       INTEGER NOT NULL,
    MaterialId  INTEGER NOT NULL REFERENCES Materials(Id) ON DELETE CASCADE,
    OrderQty    REAL NOT NULL DEFAULT 0,
    DueDate     TEXT NOT NULL,
    DataVersion INTEGER NOT NULL DEFAULT 0,
    CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS IX_Orders_MaterialDue ON Orders(MaterialId, DueDate);

-- 7. Capacities (Synced-RO from ERP)
CREATE TABLE IF NOT EXISTS Capacities (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    OrgId         INTEGER NOT NULL,
    WorkCenterId  TEXT NOT NULL,
    CapacityHours REAL NOT NULL DEFAULT 0,
    DataVersion   INTEGER NOT NULL DEFAULT 0,
    CreatedAt     TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS IX_Capacities_WorkCenter ON Capacities(WorkCenterId, DataVersion);

-- 8. Estimates (Owned)
CREATE TABLE IF NOT EXISTS Estimates (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    OrgId       INTEGER NOT NULL,
    BomVersionId INTEGER NOT NULL,
    TotalCost   REAL NOT NULL DEFAULT 0,
    LaborHours  REAL NOT NULL DEFAULT 0,
    Notes       TEXT DEFAULT '',
    CreatedAt   TEXT NOT NULL DEFAULT (datetime('now')),
    UpdatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS IX_Estimates_BomVersion ON Estimates(BomVersionId);

-- ==============================
-- §4.2 System Tables (7)
-- ==============================

-- 9. Users
CREATE TABLE IF NOT EXISTS Users (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    Username           TEXT NOT NULL UNIQUE,
    PasswordHash       TEXT NOT NULL,
    Role               TEXT NOT NULL DEFAULT 'Viewer',  -- Admin/Analyst/Viewer
    OrgId              INTEGER NOT NULL DEFAULT 1,
    IsActive           INTEGER NOT NULL DEFAULT 1,
    FailedLoginAttempts INTEGER NOT NULL DEFAULT 0,
    LockoutUntil       TEXT,                            -- NULL = not locked
    CreatedAt          TEXT NOT NULL DEFAULT (datetime('now')),
    LastLoginAt        TEXT
);

-- 10. UserTokens (Session/JWT)
CREATE TABLE IF NOT EXISTS UserTokens (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId    INTEGER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    TokenHash TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    IsRevoked INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_UserTokens_User ON UserTokens(UserId);

-- 11. AuditLogs
CREATE TABLE IF NOT EXISTS AuditLogs (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId    INTEGER REFERENCES Users(Id) ON DELETE SET NULL,
    Action    TEXT NOT NULL,            -- CREATE/UPDATE/DELETE
    TableName TEXT NOT NULL,
    RecordId  INTEGER,
    OldValues TEXT,                     -- JSON
    NewValues TEXT,                     -- JSON
    Timestamp TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS IX_AuditLogs_Table ON AuditLogs(TableName, Timestamp);

-- 12. SyncLogs
CREATE TABLE IF NOT EXISTS SyncLogs (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    SyncType          TEXT NOT NULL DEFAULT 'Full',   -- Full/Incremental/Materials/Prices
    StartedAt         TEXT NOT NULL DEFAULT (datetime('now')),
    CompletedAt       TEXT,
    RecordsProcessed  INTEGER NOT NULL DEFAULT 0,
    Status            TEXT NOT NULL DEFAULT 'Pending', -- Pending/Running/Complete/Error
    ErrorMessage      TEXT
);

-- 13. AppConfig (Key-Value)
CREATE TABLE IF NOT EXISTS AppConfig (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Key         TEXT NOT NULL UNIQUE,
    Value       TEXT NOT NULL DEFAULT '',
    Description TEXT DEFAULT '',
    UpdatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 14. DataSnapshots
CREATE TABLE IF NOT EXISTS DataSnapshots (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    SnapshotType TEXT NOT NULL DEFAULT 'Daily',  -- Daily/Manual
    SnapshotData TEXT NOT NULL DEFAULT '',       -- JSON/Binary
    CreatedAt    TEXT NOT NULL DEFAULT (datetime('now')),
    Description  TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS IX_DataSnapshots_Type ON DataSnapshots(SnapshotType, CreatedAt);

-- 15. BomVersions
CREATE TABLE IF NOT EXISTS BomVersions (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    BomId         INTEGER NOT NULL REFERENCES Materials(Id) ON DELETE CASCADE,
    VersionNumber INTEGER NOT NULL DEFAULT 1,
    State         TEXT NOT NULL DEFAULT 'Draft',  -- Draft/Released/Obsolete
    ApprovedBy    INTEGER REFERENCES Users(Id) ON DELETE SET NULL,
    ApprovedAt    TEXT,
    CreatedAt     TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS IX_BomVersions_Bom ON BomVersions(BomId, VersionNumber);

-- ==============================
-- Seed: Default Admin User
-- ==============================
-- Password: "admin123" hashed with BCrypt work-factor 12
-- This is a placeholder — real hash generated at first run via AuthService
