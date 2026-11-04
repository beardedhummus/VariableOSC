using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace VariableOSC
{
    public sealed class VerificationResult
    {
        /// <summary>Chains in the biggest group agreeing on one address, best-ranked first.</summary>
        public List<PointerPath> Survivors { get; } = new();

        /// <summary>
        /// Every chain that read the expected value, whichever address it
        /// resolved to, best-ranked first. A common value (a small number)
        /// can match a bigger-but-wrong group than the right one, so the
        /// restart check keeps all of these and lets a later change of the
        /// value pick the right group.
        /// </summary>
        public List<PointerPath> AllMatching { get; } = new();

        public int Checked { get; set; }

        /// <summary>Chains that resolved to somewhere readable.</summary>
        public int Resolved { get; set; }

        /// <summary>Chains whose resolved address held the expected value.</summary>
        public int Matching { get; set; }

        /// <summary>The address most of the matching chains agree on (0 when nothing matched).</summary>
        public long ConsensusAddress { get; set; }

        /// <summary>What the chains actually read, most common first (up to 5): shown when nothing matched so the cause is visible.</summary>
        public List<(string Text, int Count)> Readings { get; } = new();
    }

    /// <summary>
    /// Re-checks learned chains against a value the user can see: keeps the
    /// chains that resolve to an address currently holding that value. Used
    /// twice - after a restart (against the whole scan) and after changing
    /// the value (against the chains that matched the first time).
    /// </summary>
    public static class PermanenceVerifier
    {
        private const int MaxTrackedReadings = 200;

        public static VerificationResult Verify(
            IPointerMemory memory,
            IReadOnlyList<PointerPath> candidates,
            Func<long, object?> readAt,
            object expected,
            CancellationToken cancel = default)
        {
            VerificationResult result = new() { Checked = candidates.Count };

            // Chains may share a resolved address; the cache avoids reading
            // and comparing the same address over and over.
            Dictionary<long, bool> matchCache = new();
            Dictionary<string, int> readings = new();
            List<(PointerPath Path, long Address)> matches = new();

            for (int i = 0; i < candidates.Count; i++)
            {
                if (cancel.IsCancellationRequested)
                {
                    break;
                }

                if (!PointerResolver.TryResolve(memory, candidates[i], out long address))
                {
                    continue;
                }

                result.Resolved++;

                if (!matchCache.TryGetValue(address, out bool holds))
                {
                    object? actual = readAt(address);
                    holds = ValueMatcher.Matches(actual, expected);
                    matchCache[address] = holds;

                    // Tally what is there (once per distinct address) so a
                    // failed check can say what the chains read instead.
                    string text = DescribeReading(actual);

                    if (readings.ContainsKey(text) || readings.Count < MaxTrackedReadings)
                    {
                        readings[text] = readings.GetValueOrDefault(text) + 1;
                    }
                }

                if (holds)
                {
                    matches.Add((candidates[i], address));
                }
            }

            result.Matching = matches.Count;

            foreach (KeyValuePair<string, int> reading in readings.OrderByDescending(r => r.Value).Take(5))
            {
                result.Readings.Add((reading.Key, reading.Value));
            }

            if (matches.Count == 0)
            {
                return result;
            }

            foreach ((PointerPath path, long _) in matches)
            {
                result.AllMatching.Add(path);
            }

            // Largest group of chains agreeing on one address; a tie goes to
            // the group containing the best-ranked chain (the earliest,
            // since candidates arrive sorted best-first).
            Dictionary<long, int> counts = new();
            Dictionary<long, int> firstIndex = new();

            for (int i = 0; i < matches.Count; i++)
            {
                long address = matches[i].Address;
                counts[address] = counts.GetValueOrDefault(address) + 1;
                firstIndex.TryAdd(address, i);
            }

            long winner = counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => firstIndex[pair.Key])
                .First().Key;

            result.ConsensusAddress = winner;

            foreach ((PointerPath path, long address) in matches)
            {
                if (address == winner)
                {
                    result.Survivors.Add(path);
                }
            }

            return result;
        }

        private static string DescribeReading(object? value)
        {
            if (value == null)
            {
                return "(unreadable)";
            }

            string text = value.ToString() ?? "";
            char[] cleaned = text.Select(c => c < 0x20 ? '.' : c).ToArray();
            string shown = new string(cleaned);

            return shown.Length > 24 ? shown.Substring(0, 24) + "..." : shown;
        }
    }

    /// <summary>
    /// Decides whether a value read from memory is the value the user said
    /// they could see. Strings compare trimmed and case-insensitively;
    /// floating point compares with a small tolerance since the on-screen
    /// text is usually rounded.
    /// </summary>
    public static class ValueMatcher
    {
        public static bool Matches(object? actual, object expected)
        {
            if (actual == null)
            {
                return false;
            }

            if (expected is string expectedText)
            {
                return actual is string actualText &&
                    string.Equals(actualText.Trim(), expectedText.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            if (expected is bool expectedBool)
            {
                return actual is bool actualBool && actualBool == expectedBool;
            }

            if (expected is float || expected is double)
            {
                if (!TryToDouble(actual, out double actualNumber))
                {
                    return false;
                }

                double expectedNumber = Convert.ToDouble(expected);

                if (double.IsNaN(actualNumber) || double.IsInfinity(actualNumber))
                {
                    return false;
                }

                double tolerance = Math.Max(1e-4, Math.Abs(expectedNumber) * 1e-4);
                return Math.Abs(actualNumber - expectedNumber) <= tolerance;
            }

            try
            {
                return Convert.ToInt64(actual) == Convert.ToInt64(expected);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryToDouble(object value, out double number)
        {
            try
            {
                number = Convert.ToDouble(value);
                return true;
            }
            catch (Exception)
            {
                number = 0;
                return false;
            }
        }
    }

    /// <summary>
    /// Sanity gate for variables that were found through learned chains: a
    /// chain that has drifted onto the wrong memory tends to produce
    /// control characters or NaN, and sending that out as if it were real
    /// data is worse than sending nothing.
    /// </summary>
    public static class ValuePlausibility
    {
        public static bool IsPlausible(object? value)
        {
            switch (value)
            {
                case null:
                    return false;

                case string text:
                    foreach (char c in text)
                    {
                        if (c < 0x20 && c != '\t' && c != '\r' && c != '\n')
                        {
                            return false;
                        }

                        if (c == '�')
                        {
                            return false;
                        }
                    }

                    return true;

                case float f:
                    return !float.IsNaN(f) && !float.IsInfinity(f);

                case double d:
                    return !double.IsNaN(d) && !double.IsInfinity(d);

                default:
                    return true;
            }
        }
    }
}
