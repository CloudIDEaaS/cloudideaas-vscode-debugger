using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSCodeDebugger.Models
{
    public class DapCommandPacket
    {
        [JsonProperty("seq")]
        public int Sequence { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("command")]
        public string Command { get; set; }
        [JsonProperty("arguments")]
        public Dictionary<string, object> Arguments { get; set; }
    }
}
