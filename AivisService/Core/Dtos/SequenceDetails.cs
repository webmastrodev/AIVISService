using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AivisService.Core.Dtos
{
    public class SequenceDetails
    {
        public int Id { get; set; }
        public int DataVaultId { get; set; }
        public string SequenceNumber { get; set; }
        public string SequenceUri { get; set; }
        public int ImportedBy { get; set; }
        public System.DateTime ImportedOn { get; set; }
        public bool IsActive { get; set; }
        public Nullable<int> Status { get; set; }

        public int SiteId { get; set; }
       
    }
}
