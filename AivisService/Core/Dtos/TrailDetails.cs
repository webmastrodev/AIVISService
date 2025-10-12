using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AivisService.Core.Dtos
{
    public class TrailDetails
    {
        public long Id { get; set; }
        public int SequenceId { get; set; }
        public string InstrumentId { get; set; }
        public Nullable<int> InterventionTypeId { get; set; }
        public string InjectionName { get; set; }
        public int ChromTrailNumber { get; set; }
        public string Trails { get; set; }

        public Nullable<System.DateTime> AddedOn { get; set; }
        public Nullable<int> ImportedBy { get; set; }
        public Nullable<System.DateTime> ImportedOn { get; set; }
        public Nullable<int> ModifiedBy { get; set; }
        public Nullable<int> ModifiedOn { get; set; }
        public bool ResolveStatus { get; set; }
        public Nullable<int> ResolveBy { get; set; }
        public string ResolvedRemarks { get; set; }

        public string InterventionCode { get; set; }

        public string ProductName { get; set; }
        public string TestName { get; set; }
        public string BatchNo { get; set; }
        public string ARNo { get; set; }

        public Nullable<Guid> TrailId { get; set; }

        public string Operator { get; set; }
    }
}
