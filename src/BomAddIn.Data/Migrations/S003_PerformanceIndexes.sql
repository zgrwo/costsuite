-- S003: Performance Indexes
-- Date: 2026-07-12
-- Change: 添加 BomStructures 复合索引优化 BOM 展开查询性能

-- 复合索引：覆盖 BOM CTE 递归中最常见的过滤条件
CREATE INDEX IF NOT EXISTS IX_BomStructures_ParentState
    ON BomStructures(ParentMaterialId, VersionState, ValidFrom);

-- 物料编码查找索引（DuckDB 预热查询）
CREATE INDEX IF NOT EXISTS IX_Materials_Code ON Materials(Code);

-- 价格历史查询索引（BOMCOST / 价格差异）
CREATE INDEX IF NOT EXISTS IX_Prices_MaterialDate
    ON Prices(MaterialId, EffectiveDate DESC);

-- Token 查询索引（按 hash 查找 + 过期清理）
CREATE INDEX IF NOT EXISTS IX_UserTokens_TokenHash
    ON UserTokens(TokenHash, IsRevoked, ExpiresAt);

-- Token 过期清理索引
CREATE INDEX IF NOT EXISTS IX_UserTokens_Expires
    ON UserTokens(ExpiresAt, IsRevoked);

-- 审计日志多维查询索引
CREATE INDEX IF NOT EXISTS IX_AuditLogs_TableRecord
    ON AuditLogs(TableName, RecordId);
CREATE INDEX IF NOT EXISTS IX_AuditLogs_User
    ON AuditLogs(UserId);
CREATE INDEX IF NOT EXISTS IX_AuditLogs_Timestamp
    ON AuditLogs(Timestamp);

-- 库存快照查询索引
CREATE INDEX IF NOT EXISTS IX_Inventories_MaterialDate
    ON Inventories(MaterialId, SnapshotDate);

-- 数据快照查询索引
CREATE INDEX IF NOT EXISTS IX_DataSnapshots_Created
    ON DataSnapshots(CreatedAt);
