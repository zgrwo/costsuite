using System;

namespace BomAddIn.Infrastructure.Models
{
    public class Estimate
    {
        public long Id { get; set; }
        public long OrgId { get; set; }
        public long BomVersionId { get; set; }
        public decimal TotalCost { get; set; }
        public double LaborHours { get; set; }
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
