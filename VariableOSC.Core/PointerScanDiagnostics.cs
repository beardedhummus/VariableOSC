using System.Collections.Generic;
using System.Linq;

namespace VariableOSC
{
    /// <summary>
    /// Turns the per-hop counts of a scan into a plain-language reason for
    /// an empty result, so "no chains found" says where the trail died and
    /// what to try instead of leaving it a mystery.
    /// </summary>
    public static class PointerScanDiagnostics
    {
        public static string DescribeEmptyResult(PointerScanResult result)
        {
            List<ScanHopStat> hops = result.Hops;
            int deepest = 0;

            foreach (ScanHopStat hop in hops)
            {
                if (hop.Links > 0 || hop.Chains > 0)
                {
                    deepest = hop.Hop;
                }
            }

            if (deepest == 0)
            {
                return "Nothing in the program's memory points at this address, so there is nothing to trace " +
                       "back from. It is most likely a temporary or display copy of the value - try saving a " +
                       "different candidate.";
            }

            string perHop = string.Join(" > ", hops.Select(h => h.Links.ToString("N0")));

            if (deepest < hops.Count)
            {
                return $"The trail leads back {deepest} hop(s) and then stops - nothing points at the pointers " +
                       $"found there (links per hop: {perHop}). The value is probably held through temporary " +
                       "memory rather than anything fixed in the program; try a different candidate.";
            }

            return $"Pointers lead back the full {hops.Count} hops but never reach the program's fixed memory " +
                   $"(links per hop: {perHop}). Try a deeper search or a different candidate; the value may " +
                   "sit deeper than the search reaches, or inside a long list.";
        }
    }
}
