using System;

namespace BomAddIn.Infrastructure.Models
{
    public class Supplier
    {
        public long Id { get; set; }
        public long OrgId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        /// <summary>供应商评级 (1-5 星)</summary>
        public int Rating { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
