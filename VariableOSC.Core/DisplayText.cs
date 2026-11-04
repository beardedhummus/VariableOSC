namespace VariableOSC
{
    /// <summary>
    /// Scanned string values come from arbitrary process memory - bytes
    /// past the real end of a string (or a false-positive match) can
    /// decode as control characters (\r, \b, etc.). Printed raw to a
    /// console, those can move the cursor and visibly corrupt whatever
    /// was already on screen (e.g. a numbered candidate list losing its
    /// numbering). Sanitizing before display is cheap insurance, and
    /// worth doing in the GUI too since embedded tabs/newlines can mangle
    /// a DataGridView cell's row height.
    /// </summary>
    public static class DisplayText
    {
        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? "";
            }

            char[] chars = text.ToCharArray();

            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] < 0x20 || chars[i] == 0x7F)
                {
                    chars[i] = '.';
                }
            }

            return new string(chars);
        }
    }
}
