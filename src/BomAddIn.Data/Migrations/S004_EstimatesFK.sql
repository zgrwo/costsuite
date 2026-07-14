-- S004: Estimates FK Constraints
-- Date: 2026-07-13
-- Change: 添加 Estimates.BomVersionId → BomVersions(Id) 外键约束 (v2 L-7)
-- Status: SQLite 不支持 ALTER TABLE ADD CONSTRAINT。
--         使用触发器代替 FK 约束确保引用完整性。
--         V2.0 迁移至 PostgreSQL/SQL Server 时转换为正式 FK 约束。
--
-- 注意: 此迁移不修改表结构。IX_Estimates_BomVersion 索引
--       已在 S001_InitialSchema.sql 中创建，此处无需重复。

-- 触发器级联 FK 检查：BEFORE INSERT 时验证 BomVersionId 存在
CREATE TRIGGER IF NOT EXISTS trg_Estimates_BomVersionId
BEFORE INSERT ON Estimates
BEGIN
    SELECT RAISE(ABORT, 'FK violation: BomVersionId not found')
    WHERE NEW.BomVersionId IS NOT NULL
    AND NEW.BomVersionId NOT IN (SELECT Id FROM BomVersions);
END;
