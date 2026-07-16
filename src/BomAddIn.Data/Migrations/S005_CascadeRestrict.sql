-- S005: CASCADE → RESTRICT on BOM-related Foreign Keys + SyncLogs Index
-- Date: 2026-07-17
-- Change: 将 7 个 CASCADE DELETE 外键替换为 RESTRICT 语义，防止误删物料时级联丢失关联数据。
--         同时补建 SyncLogs 查询索引。
-- Status: SQLite 不支持 ALTER TABLE DROP/ALTER CONSTRAINT。
--         使用 BEFORE DELETE 触发器拦截级联删除，与 S004 模式一致。
--         V2.0 迁移至 PostgreSQL/SQL Server 时转换为正式 RESTRICT 约束。

-- ==============================
-- Part 1: Materials 删除保护（5 个子表依赖）
-- ==============================

-- 触发器：删除 Materials 前检查 BomStructures 依赖
CREATE TRIGGER IF NOT EXISTS trg_Materials_Restrict_BomStructures
BEFORE DELETE ON Materials
BEGIN
    SELECT RAISE(ABORT, 'RESTRICT: Cannot delete material — dependent rows exist in BomStructures')
    WHERE EXISTS (
        SELECT 1 FROM BomStructures
        WHERE ParentMaterialId = OLD.Id OR ChildMaterialId = OLD.Id
    );
END;

-- 触发器：删除 Materials 前检查 Prices 依赖
CREATE TRIGGER IF NOT EXISTS trg_Materials_Restrict_Prices
BEFORE DELETE ON Materials
BEGIN
    SELECT RAISE(ABORT, 'RESTRICT: Cannot delete material — dependent rows exist in Prices')
    WHERE EXISTS (SELECT 1 FROM Prices WHERE MaterialId = OLD.Id);
END;

-- 触发器：删除 Materials 前检查 Inventories 依赖
CREATE TRIGGER IF NOT EXISTS trg_Materials_Restrict_Inventories
BEFORE DELETE ON Materials
BEGIN
    SELECT RAISE(ABORT, 'RESTRICT: Cannot delete material — dependent rows exist in Inventories')
    WHERE EXISTS (SELECT 1 FROM Inventories WHERE MaterialId = OLD.Id);
END;

-- 触发器：删除 Materials 前检查 Orders 依赖
CREATE TRIGGER IF NOT EXISTS trg_Materials_Restrict_Orders
BEFORE DELETE ON Materials
BEGIN
    SELECT RAISE(ABORT, 'RESTRICT: Cannot delete material — dependent rows exist in Orders')
    WHERE EXISTS (SELECT 1 FROM Orders WHERE MaterialId = OLD.Id);
END;

-- 触发器：删除 Materials 前检查 BomVersions 依赖
CREATE TRIGGER IF NOT EXISTS trg_Materials_Restrict_BomVersions
BEFORE DELETE ON Materials
BEGIN
    SELECT RAISE(ABORT, 'RESTRICT: Cannot delete material — dependent rows exist in BomVersions')
    WHERE EXISTS (SELECT 1 FROM BomVersions WHERE BomId = OLD.Id);
END;

-- ==============================
-- Part 2: Suppliers 删除保护（1 个子表依赖）
-- ==============================

-- 触发器：删除 Suppliers 前检查 Prices 依赖
CREATE TRIGGER IF NOT EXISTS trg_Suppliers_Restrict_Prices
BEFORE DELETE ON Suppliers
BEGIN
    SELECT RAISE(ABORT, 'RESTRICT: Cannot delete supplier — dependent rows exist in Prices')
    WHERE EXISTS (SELECT 1 FROM Prices WHERE SupplierId = OLD.Id);
END;

-- ==============================
-- Part 3: SyncLogs 复合索引（FIX 6）
-- ==============================

CREATE INDEX IF NOT EXISTS IX_SyncLogs_StatusCompleted
    ON SyncLogs(Status, CompletedAt);
