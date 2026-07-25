-- S006: BOM Closure Table — 预计算所有祖先-后代关系
-- Date: 2026-07-26
-- Change: 创建 BomClosure 表，支持 O(1) 子树/祖先查询，替代逐层 BFS 多次 SQL。
--         包含触发器自动维护（INSERT/UPDATE/DELETE on BomStructures）。
-- Ref: refactoring-plan.md §2.4 Closure Table 设计

-- ==============================
-- Part 1: Closure Table DDL
-- ==============================

CREATE TABLE IF NOT EXISTS BomClosure (
    AncestorId    INTEGER NOT NULL,
    DescendantId  INTEGER NOT NULL,
    Depth         INTEGER NOT NULL,
    PathQuantity  REAL NOT NULL DEFAULT 1.0,
    PRIMARY KEY (AncestorId, DescendantId)
);

-- 查询索引：按祖先查所有后代（BOM 展开）
CREATE INDEX IF NOT EXISTS IX_BomClosure_Ancestor
    ON BomClosure(AncestorId, Depth);

-- 查询索引：按后代查所有祖先（Where-Used）
CREATE INDEX IF NOT EXISTS IX_BomClosure_Descendant
    ON BomClosure(DescendantId, Depth);

-- ==============================
-- Part 2: 初始填充（从 BomStructures 递归计算）
-- ==============================

-- 使用递归 CTE 填充（仅处理 Released + 当前有效的边）
-- 注：SQLite 支持 WITH RECURSIVE，此处用于一次性数据迁移
INSERT OR IGNORE INTO BomClosure (AncestorId, DescendantId, Depth, PathQuantity)
WITH RECURSIVE closure(ancestor, descendant, depth, qty) AS (
    -- 基础：每个节点是自身的祖先（depth=0, qty=1）
    SELECT DISTINCT ParentMaterialId, ParentMaterialId, 0, 1.0
    FROM BomStructures
    WHERE VersionState = 'Released'
    UNION ALL
    SELECT DISTINCT ChildMaterialId, ChildMaterialId, 0, 1.0
    FROM BomStructures
    WHERE VersionState = 'Released'
    UNION ALL
    -- 递归：parent→child 边
    SELECT c.ancestor, b.ChildMaterialId, c.depth + 1, c.qty * b.Quantity
    FROM closure c
    JOIN BomStructures b ON b.ParentMaterialId = c.descendant
    WHERE b.VersionState = 'Released'
      AND c.depth < 50  -- 防止循环引用无限递归
)
SELECT ancestor, descendant, MIN(depth), SUM(qty)
FROM closure
WHERE depth > 0
GROUP BY ancestor, descendant;

-- ==============================
-- Part 3: 自动维护触发器
-- ==============================

-- 触发器：BomStructures INSERT → 增量更新 Closure
CREATE TRIGGER IF NOT EXISTS trg_BomClosure_Insert
AFTER INSERT ON BomStructures
WHEN NEW.VersionState = 'Released'
BEGIN
    -- 新边: Parent→Child
    -- 1. 添加 Parent 的所有祖先到 Child 的所有后代的关系
    INSERT INTO BomClosure (AncestorId, DescendantId, Depth, PathQuantity)
    SELECT a.AncestorId, d.DescendantId, a.Depth + d.Depth + 1, a.PathQuantity * d.PathQuantity * NEW.Quantity
    FROM BomClosure a
    CROSS JOIN BomClosure d
    WHERE a.DescendantId = NEW.ParentMaterialId
      AND d.AncestorId = NEW.ChildMaterialId
    ON CONFLICT(AncestorId, DescendantId) DO UPDATE SET
        Depth = MIN(BomClosure.Depth, excluded.Depth),
        PathQuantity = BomClosure.PathQuantity + excluded.PathQuantity;

    -- 2. Parent 到 Child 的直接关系
    INSERT INTO BomClosure (AncestorId, DescendantId, Depth, PathQuantity)
    VALUES (NEW.ParentMaterialId, NEW.ChildMaterialId, 1, NEW.Quantity)
    ON CONFLICT(AncestorId, DescendantId) DO UPDATE SET
        Depth = MIN(BomClosure.Depth, excluded.Depth),
        PathQuantity = BomClosure.PathQuantity + excluded.PathQuantity;

    -- 3. Parent 的所有祖先到 Child 的关系
    INSERT INTO BomClosure (AncestorId, DescendantId, Depth, PathQuantity)
    SELECT AncestorId, NEW.ChildMaterialId, Depth + 1, PathQuantity * NEW.Quantity
    FROM BomClosure
    WHERE DescendantId = NEW.ParentMaterialId AND Depth > 0
    ON CONFLICT(AncestorId, DescendantId) DO UPDATE SET
        Depth = MIN(BomClosure.Depth, excluded.Depth),
        PathQuantity = BomClosure.PathQuantity + excluded.PathQuantity;

    -- 4. Parent 到 Child 的所有后代的关系
    INSERT INTO BomClosure (AncestorId, DescendantId, Depth, PathQuantity)
    SELECT NEW.ParentMaterialId, DescendantId, Depth + 1, PathQuantity * NEW.Quantity
    FROM BomClosure
    WHERE AncestorId = NEW.ChildMaterialId AND Depth > 0
    ON CONFLICT(AncestorId, DescendantId) DO UPDATE SET
        Depth = MIN(BomClosure.Depth, excluded.Depth),
        PathQuantity = BomClosure.PathQuantity + excluded.PathQuantity;
END;

-- 触发器：BomStructures DELETE → 全量重建（安全但较慢，V2.0 优化为增量）
CREATE TRIGGER IF NOT EXISTS trg_BomClosure_Delete
AFTER DELETE ON BomStructures
WHEN OLD.VersionState = 'Released'
BEGIN
    -- 删除涉及被删边的所有 closure 记录，然后全量重建
    DELETE FROM BomClosure;

    INSERT OR IGNORE INTO BomClosure (AncestorId, DescendantId, Depth, PathQuantity)
    WITH RECURSIVE closure(ancestor, descendant, depth, qty) AS (
        SELECT DISTINCT ParentMaterialId, ParentMaterialId, 0, 1.0
        FROM BomStructures WHERE VersionState = 'Released'
        UNION ALL
        SELECT DISTINCT ChildMaterialId, ChildMaterialId, 0, 1.0
        FROM BomStructures WHERE VersionState = 'Released'
        UNION ALL
        SELECT c.ancestor, b.ChildMaterialId, c.depth + 1, c.qty * b.Quantity
        FROM closure c
        JOIN BomStructures b ON b.ParentMaterialId = c.descendant
        WHERE b.VersionState = 'Released' AND c.depth < 50
    )
    SELECT ancestor, descendant, MIN(depth), SUM(qty)
    FROM closure WHERE depth > 0
    GROUP BY ancestor, descendant;
END;

-- 触发器：BomStructures UPDATE → 全量重建（Quantity/VersionState 变更时触发）
CREATE TRIGGER IF NOT EXISTS trg_BomClosure_Update
AFTER UPDATE OF Quantity, VersionState ON BomStructures
WHEN OLD.VersionState = 'Released' OR NEW.VersionState = 'Released'
BEGIN
    DELETE FROM BomClosure;

    INSERT OR IGNORE INTO BomClosure (AncestorId, DescendantId, Depth, PathQuantity)
    WITH RECURSIVE closure(ancestor, descendant, depth, qty) AS (
        SELECT DISTINCT ParentMaterialId, ParentMaterialId, 0, 1.0
        FROM BomStructures WHERE VersionState = 'Released'
        UNION ALL
        SELECT DISTINCT ChildMaterialId, ChildMaterialId, 0, 1.0
        FROM BomStructures WHERE VersionState = 'Released'
        UNION ALL
        SELECT c.ancestor, b.ChildMaterialId, c.depth + 1, c.qty * b.Quantity
        FROM closure c
        JOIN BomStructures b ON b.ParentMaterialId = c.descendant
        WHERE b.VersionState = 'Released' AND c.depth < 50
    )
    SELECT ancestor, descendant, MIN(depth), SUM(qty)
    FROM closure WHERE depth > 0
    GROUP BY ancestor, descendant;
END;
