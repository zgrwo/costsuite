using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class SupplierRepository : ISupplierRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public SupplierRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public Supplier? GetById(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<Supplier>(
                "SELECT * FROM Suppliers WHERE Id = @Id", new { Id = id });
        }

        public Supplier? GetByCode(long orgId, string code)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<Supplier>(
                "SELECT * FROM Suppliers WHERE OrgId = @OrgId AND Code = @Code",
                new { OrgId = orgId, Code = code });
        }

        public IEnumerable<Supplier> GetAll(long orgId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<Supplier>(
                "SELECT * FROM Suppliers WHERE OrgId = @OrgId ORDER BY Code",
                new { OrgId = orgId });
        }

        public void Add(Supplier supplier)
        {
            using var conn = _connectionFactory.CreateConnection();
            supplier.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO Suppliers (OrgId, Code, Name, Contact, Rating, CreatedAt, UpdatedAt)
                  VALUES (@OrgId, @Code, @Name, @Contact, @Rating, @CreatedAt, @UpdatedAt);
                  SELECT last_insert_rowid();",
                supplier);
        }

        public void Update(Supplier supplier)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                @"UPDATE Suppliers SET Code=@Code, Name=@Name, Contact=@Contact,
                  Rating=@Rating, UpdatedAt=@UpdatedAt
                  WHERE Id=@Id AND OrgId=@OrgId",
                supplier);
        }

        public void Delete(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                "DELETE FROM Suppliers WHERE Id = @Id", new { Id = id });
        }
    }
}
