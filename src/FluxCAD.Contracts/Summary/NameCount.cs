using System;
using System.Collections.Generic;
using System.Text;

namespace FluxCAD.Contracts.Summary
{
    public sealed class NameCount
    {
        public NameCount() { }
        public NameCount(string name, int count) { Name = name; Count = count; }

        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
}
