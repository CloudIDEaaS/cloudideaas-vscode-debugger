using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSCodeDebugger.Models
{
    public class ScopeReference
    {
        public string CallFrameId { get; set; } = string.Empty;
        public int ScopeNumber { get; set; }
        public string ObjectId { get; set; } = string.Empty;
    }
}
