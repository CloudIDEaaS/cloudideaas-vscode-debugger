using Newtonsoft.Json.Linq;

namespace VSCodeDebugger.Models
{
    public class CdpPausedState
    {
        public string? Reason { get; set; }
        public JArray CallFrames { get; set; } = new JArray();
        public JObject? Data { get; set; }
        public List<string> HitBreakpoints { get; set; } = new();
    }
}