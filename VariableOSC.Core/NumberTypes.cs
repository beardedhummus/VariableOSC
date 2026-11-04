using System;

namespace VariableOSC
{
    /// <summary>
    /// One location in memory where a combined "search all number types"
    /// scan found a match, along with which specific numeric DataType
    /// matched there. Mirrors StringMatch's role for the string search.
    /// </summary>
    public class NumberMatch
    {
        public IntPtr Address { get; set; }

        public DataType Type { get; set; }
    }
}
