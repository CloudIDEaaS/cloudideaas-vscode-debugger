using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VSCodeDebugger.Models;

namespace ChromeDebugger
{
    public class DapResponsePacket : DapCommandPacket
    {
        [JsonProperty("request_seq")]
        public int RequestSequence { get; set; }
        [JsonProperty("success")]
        public bool Success { get; set; }
        [JsonProperty("body")]
        public Dictionary<string, object> Body { get; set; }
    }
}
