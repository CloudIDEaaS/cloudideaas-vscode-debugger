using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSCodeDebugger.Models
{
    public class DapEventPacket
    {
        [JsonProperty("seq")]
        public int Sequence { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; } = "event";
        [JsonProperty("event")]
        public string Event { get; set; } = string.Empty;

        [JsonProperty("body")]
        public object? Body { get; set; }
    }
}
