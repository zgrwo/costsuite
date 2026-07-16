using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class MaterialRepository : IMaterialRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public MaterialRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public Material? GetById(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<Material>(
                "SELECT * FROM Materials WHERE Id = @Id", new { Id = id });
        }

        public Material? GetByCode(long orgId, string code)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<Material>(
                "SELECT * FROM Materials WHERE OrgId = @OrgId AND Code = @Code",
                new { OrgId = orgId, Code = code });
        }

        public Material? GetByCode(long orgId, string code, IDbConnection conn, IDbTransaction tx)
        {
            return conn.QueryFirstOrDefault<Material>(
                "SELECT * FROM Materials WHERE OrgId = @OrgId AND Code = @Code",
                new { OrgId = orgId, Code = code }, tx);
        }

        /// <summary>批量按编码查询物料 — 一条 SQL 返回所有匹配行，构建 code→Material 映射 (R2-16)</summary>
        public Dictionary<string, Material> GetByCodes(long orgId, HashSet<string> codes, IDbConnection conn, IDbTransaction tx)
        {
            if (codes == null || codes.Count == 0)
                return new Dictionary<string, Material>();

            var results = conn.Query<Material>(
                "SELECT * FROM Materials WHERE OrgId = @OrgId AND Code IN @Codes",
                new { OrgId = orgId, Codes = codes }, tx);

            return results.ToDictionary(m => m.Code, m => m);
        }

        public IEnumerable<Material> GetAll(long orgId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<Material>(
                "SELECT * FROM Materials WHERE OrgId = @OrgId AND IsActive = 1 ORDER BY Code",
                new { OrgId = orgId }).ToList();
        }

        public IEnumerable<Material> Search(long orgId, string? category = null, string? keyword = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM Materials WHERE OrgId = @OrgId AND IsActive = 1";
            var parameters = new DynamicParameters();
            parameters.Add("OrgId", orgId);

            if (!string.IsNullOrWhiteSpace(category))
            {
                sql += " AND Category = @Category";
                parameters.Add("Category", category);
            }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql += " AND (Code LIKE @Keyword OR Name LIKE @Keyword)";
                parameters.Add("Keyword", $"%{keyword}%");
            }
            sql += " ORDER BY Code";
            return conn.Query<Material>(sql, parameters).ToList();
        }

        public void Add(Material material)
        {
            using var conn = _connectionFactory.CreateConnection();
            material.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive, CreatedAt, UpdatedAt)
                  VALUES (@OrgId, @Code, @Name, @Spec, @Unit, @Category, @IsActive, @CreatedAt, @UpdatedAt);
                  SELECT last_insert_rowid();",
                material);
        }

        public void Add(Material material, IDbConnection conn, IDbTransaction tx)
        {
            material.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive, CreatedAt, UpdatedAt)
                  VALUES (@OrgId, @Code, @Name, @Spec, @Unit, @Category, @IsActive, @CreatedAt, @UpdatedAt);
                  SELECT last_insert_rowid();",
                material, tx);
        }

        public void Update(Material material)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                @"UPDATE Materials SET Code=@Code, Name=@Name, Spec=@Spec, Unit=@Unit,
                  Category=@Category, IsActive=@IsActive, UpdatedAt=@UpdatedAt
                  WHERE Id=@Id AND OrgId=@OrgId",
                material);
        }

        public void Update(Material material, IDbConnection conn, IDbTransaction tx)
        {
            conn.Execute(
                @"UPDATE Materials SET Code=@Code, Name=@Name, Spec=@Spec, Unit=@Unit,
                  Category=@Category, IsActive=@IsActive, UpdatedAt=@UpdatedAt
                  WHERE Id=@Id AND OrgId=@OrgId",
                material, tx);
        }

        /// <summary>高效 COUNT 查询 — 避免 GetAll().Count() 拉取全量数据 (v2 M-29)</summary>
        public int GetCount(long orgId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Materials WHERE OrgId = @OrgId AND IsActive = 1",
                new { OrgId = orgId });
        }

        public void Delete(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                "DELETE FROM Materials WHERE Id = @Id", new { Id = id });
        }
    }
}
