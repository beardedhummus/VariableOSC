using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VariableOSC
{
    /// <summary>
    /// Compact text form of a pointer chain: "module|hexBase|hexOff1,hexOff2".
    /// A scan can keep thousands of candidates per variable, and a one-line
    /// string each keeps the session file small and readable instead of a
    /// nested JSON object per chain.
    /// </summary>
    public static class PointerChainCodec
    {
        public static string Encode(PointerCandidate candidate)
        {
            return Encode(
                candidate.ModuleName,
                candidate.BaseOffset,
                candidate.Offsets.Select(o => (long)o));
        }

        public static string Encode(PointerPath path)
        {
            return Encode(path.ModuleName, path.BaseOffset, path.IsPointerChain ? path.Offsets : Array.Empty<long>());
        }

        private static string Encode(string moduleName, long baseOffset, IEnumerable<long> offsets)
        {
            string offsetText = string.Join(",", offsets.Select(o => o.ToString("X", CultureInfo.InvariantCulture)));

            return $"{moduleName}|{baseOffset.ToString("X", CultureInfo.InvariantCulture)}|{offsetText}";
        }

        public static bool TryDecode(string? text, out PointerPath path)
        {
            path = new PointerPath();

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] parts = text.Split('|');

            if (parts.Length != 3 || parts[0].Length == 0)
            {
                return false;
            }

            if (!long.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long baseOffset))
            {
                return false;
            }

            List<long> offsets = new();

            if (parts[2].Length > 0)
            {
                foreach (string piece in parts[2].Split(','))
                {
                    if (!long.TryParse(piece, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long offset))
                    {
                        return false;
                    }

                    offsets.Add(offset);
                }
            }

            path = new PointerPath
            {
                ModuleName = parts[0],
                BaseOffset = baseOffset,
                IsPointerChain = offsets.Count > 0,
                Offsets = offsets
            };

            return true;
        }

        public static List<PointerPath> DecodeAll(IEnumerable<string> texts)
        {
            List<PointerPath> paths = new();

            foreach (string text in texts)
            {
                if (TryDecode(text, out PointerPath path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        /// <summary>Short human-readable form for logs and tooltips, e.g. "Hog4PC.exe+1A2B3C -> +10 -> +48".</summary>
        public static string Describe(PointerPath path)
        {
            string text = $"{path.ModuleName}+{path.BaseOffset:X}";

            if (path.IsPointerChain)
            {
                text += string.Concat(path.Offsets.Select(o => $" -> +{o:X}"));
            }

            return text;
        }
    }
}
