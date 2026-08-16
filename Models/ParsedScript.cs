using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSCodeDebugger.Models
{
    public sealed class ParsedScript
    {
        public required string ScriptId { get; init; }
        public string? Url { get; init; }
        public int StartLine { get; init; }
        public int StartColumn { get; init; }
        public int EndLine { get; init; }
        public int EndColumn { get; init; }
    }
}
