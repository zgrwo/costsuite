-- S002: Approval Workflow — 扩展 BomVersions 状态支持
-- Date: 2026-07-12
-- Change: 新增版本状态 PendingReview, Approved, Rejected
--         添加状态查询索引
--
-- 不需要 ALTER TABLE: BomVersions.State 字段类型是 TEXT，
-- 天然支持新值 ("PendingReview", "Approved", "Rejected")。
-- 本迁移仅新增索引以优化状态过滤查询。

CREATE INDEX IF NOT EXISTS IX_BomVersions_State ON BomVersions(State);
CREATE INDEX IF NOT EXISTS IX_BomVersions_BomState ON BomVersions(BomId, State);
