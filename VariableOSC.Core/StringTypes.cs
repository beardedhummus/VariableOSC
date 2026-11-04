using System;

namespace VariableOSC
{
    public enum StringEncodingType
    {
        Ascii,
        Utf16
    }

    /// <summary>
    /// The three toggles the user can set before a string search:
    /// case sensitivity, requiring a null terminator right after the
    /// match, and reading back a fixed number of characters instead of
    /// just the search text's own length.
    /// </summary>
    public class StringSearchOptions
    {
        public bool CaseSensitive { get; set; } = true;

        public bool NullTerminated { get; set; } = false;

        public bool FixedLength { get; set; } = false;

        public int FixedLengthChars { get; set; } = 32;
    }

    /// <summary>
    /// One location in memory where the search text was found, along with
    /// which encoding it matched under.
    /// </summary>
    public class StringMatch
    {
        public IntPtr Address { get; set; }

        public StringEncodingType Encoding { get; set; }

        public int MatchedCharLength { get; set; }
    }
}
