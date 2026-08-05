-- S007: Fix BomVersions.BomId FK target: Materials(Id) -> BomStructures(Id)
-- Date: 2026-08-05
-- Change: S001 错误地将 BomVersions.BomId 声明为 REFERENCES Materials(Id)，
--         但业务代码（BomService.UpdateNode / SeedDataGenerator.GenerateBomVersions /
--         ApprovalService）与 specification.md §4.4.3 均以 BomStructures(Id) 为权威语义。
--         该错误导致 BomService.UpdateNode 创建版本记录时触发 constraint failed。
-- Status: SQLite 不支持修改 FK 引用目标，采用标准重建表方案。
--         Estimates.BomVersionId 由 S004 触发器维护（非真实 FK），不受重建影响。
--         BomStructures 删除经 ON DELETE CASCADE 连带删除版本历史（Step 5b/7 防止
--         Estimates.BomVersionId 悬空 — code-review impact 发现）。
--         同时修正 S001 偏离：Estimates.BomVersionId 声明为 NOT NULL，
--         与 spec §4.4.3（可空）不一致，Step 5b 重建为可空。

-- Step 1: 删除基于错误语义的触发器（S005: Materials 删除时对 BomVersions 的检查）。
--         FK 修正后 BomVersions 不再直接引用 Materials，该检查会误拦截物料删除。
DROP TRIGGER IF EXISTS trg_Materials_Restrict_BomVersions;

-- Step 1b: 临时删除 S004 的 Estimates 触发器 — 它们引用 BomVersions 表名，
--          DROP/RENAME 期间的悬空引用会触发 "no such table" 错误（SQLite 3.25+
--          RENAME 会重新校验引用表名的触发器体），重建表完成后在 Step 6 恢复。
DROP TRIGGER IF EXISTS trg_Estimates_BomVersionId;
DROP TRIGGER IF EXISTS trg_Estimates_BomVersionId_Update;

-- Step 2: 重建表 — FK 指向 BomStructures，BOM 边删除时级联清理版本历史
CREATE TABLE IF NOT EXISTS BomVersions_new (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    BomId         INTEGER NOT NULL REFERENCES BomStructures(Id) ON DELETE CASCADE,
    VersionNumber INTEGER NOT NULL DEFAULT 1,
    State         TEXT NOT NULL DEFAULT 'Draft',  -- Draft/Released/Obsolete
    ApprovedBy    INTEGER REFERENCES Users(Id) ON DELETE SET NULL,
    ApprovedAt    TEXT,
    CreatedAt     TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Step 3: 迁移有效数据 — 过滤 BomId 不存在于 BomStructures 的孤儿行
--         （现有数据均由业务代码以 BomStructures.Id 语义写入，孤儿行仅为防御性过滤）
INSERT INTO BomVersions_new (Id, BomId, VersionNumber, State, ApprovedBy, ApprovedAt, CreatedAt)
SELECT Id, BomId, VersionNumber, State, ApprovedBy, ApprovedAt, CreatedAt
FROM BomVersions
WHERE EXISTS (SELECT 1 FROM BomStructures WHERE Id = BomVersions.BomId);

-- Step 4: 替换旧表
DROP TABLE BomVersions;
ALTER TABLE BomVersions_new RENAME TO BomVersions;

-- Step 5: 重建索引（与 S001 定义一致）
CREATE INDEX IF NOT EXISTS IX_BomVersions_Bom ON BomVersions(BomId, VersionNumber);

-- Step 5b: 重建 Estimates — BomVersionId 改为可空（修正 S001 与 spec §4.4.3 的偏离，
--          使 Step 7 的 SET NULL 保护可执行）。必须在 Step 6 恢复 S004 触发器之前：
--          DROP TABLE Estimates 会自动移除定义在其上的旧触发器，Step 6 重新创建。
CREATE TABLE IF NOT EXISTS Estimates_new (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    OrgId        INTEGER NOT NULL,
    BomVersionId INTEGER,
    TotalCost    REAL NOT NULL DEFAULT 0,
    LaborHours   REAL NOT NULL DEFAULT 0,
    Notes        TEXT DEFAULT '',
    CreatedAt    TEXT NOT NULL DEFAULT (datetime('now')),
    UpdatedAt    TEXT NOT NULL DEFAULT (datetime('now'))
);

INSERT INTO Estimates_new (Id, OrgId, BomVersionId, TotalCost, LaborHours, Notes, CreatedAt, UpdatedAt)
SELECT Id, OrgId, BomVersionId, TotalCost, LaborHours, Notes, CreatedAt, UpdatedAt
FROM Estimates;

DROP TABLE Estimates;
ALTER TABLE Estimates_new RENAME TO Estimates;
CREATE INDEX IF NOT EXISTS IX_Estimates_BomVersion ON Estimates(BomVersionId);

-- Step 6: 恢复 S004 的 Estimates 触发器（与 S004_EstimatesFK.sql 定义一致）
CREATE TRIGGER IF NOT EXISTS trg_Estimates_BomVersionId
BEFORE INSERT ON Estimates
BEGIN
    SELECT RAISE(ABORT, 'FK violation: BomVersionId not found')
    WHERE NEW.BomVersionId IS NOT NULL
    AND NEW.BomVersionId NOT IN (SELECT Id FROM BomVersions);
END;

CREATE TRIGGER IF NOT EXISTS trg_Estimates_BomVersionId_Update
BEFORE UPDATE OF BomVersionId ON Estimates
BEGIN
    SELECT RAISE(ABORT, 'FK violation: BomVersionId not found on UPDATE')
    WHERE NEW.BomVersionId IS NOT NULL
    AND NEW.BomVersionId NOT IN (SELECT Id FROM BomVersions);
END;

-- Step 7: 版本删除保护 — BomVersions 被直接删除时，将引用它的
--         Estimates.BomVersionId 置 NULL（Step 5b 后列可空），与 ApprovedBy
--         ON DELETE SET NULL 语义一致：保留估算数据，避免悬空引用。
--         注：SQLite 默认 recursive_triggers=OFF，BomStructures 删除触发的
--         FK CASCADE 不会递归触发本触发器（该场景 Estimates 同样不受影响，
--         但悬空 BomVersionId 仍存在）— 本触发器覆盖直接 DELETE 路径。
CREATE TRIGGER IF NOT EXISTS trg_BomVersions_Delete_Estimates_Nullify
BEFORE DELETE ON BomVersions
BEGIN
    UPDATE Estimates SET BomVersionId = NULL WHERE BomVersionId = OLD.Id;
END;
