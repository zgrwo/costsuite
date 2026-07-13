using System;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Infrastructure.Models
{
    public class BomVersion
    {
        public long Id { get; set; }
        public long BomId { get; set; }
        public int VersionNumber { get; set; }
        public VersionState State { get; set; } = VersionState.Draft;
        public long? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
