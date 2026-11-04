using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace VariableOSC
{
    public enum ScanThoroughness
    {
        Quick,
        Standard,
        Deep
    }

    public sealed class LearnSummary
    {
        public MemoryVariable Variable { get; init; } = null!;

        public int CandidateCount { get; init; }

        /// <summary>The variable already sits at a fixed spot inside a module, so it needed no chain at all.</summary>
        public bool IsStatic { get; init; }

        public bool Truncated { get; init; }

        /// <summary>When no chains were found: where the trail died and what to try.</summary>
        public string Trail { get; init; } = "";
    }

    public sealed class VerifyOutcome
    {
        public MemoryVariable Variable { get; init; } = null!;

        public int Checked { get; init; }

        public int Resolved { get; init; }

        public int Matching { get; init; }

        public int Survivors { get; init; }

        public bool Succeeded { get; init; }

        public PermanenceState NewState { get; init; }

        public string Message { get; init; } = "";
    }

    /// <summary>
    /// The two halves of Make Permanent, kept free of any UI: Learn finds
    /// candidate chains in the running target, Verify (after a restart)
    /// keeps the ones that still lead to the value the user can see.
    /// </summary>
    public static class PermanenceWorkflow
    {
        /// <summary>Most chains put into service for one variable.</summary>
        public const int MaxPermanentChains = 8;

        public static PointerScanOptions OptionsFor(ScanThoroughness thoroughness)
        {
            return thoroughness switch
            {
                ScanThoroughness.Quick => new PointerScanOptions
                {
                    MaxLevels = 3,
                    MaxOffset = 0x200,
                    MaxFrontier = 60_000,
                    MaxNodesPerLevel = 300_000,
                    MaxCandidatesPerTarget = 4_000
                },
                ScanThoroughness.Deep => new PointerScanOptions
                {
                    MaxLevels = 5,
                    MaxOffset = 0x800,
                    MaxFrontier = 250_000,
                    MaxNodesPerLevel = 1_000_000,
                    MaxCandidatesPerTarget = 15_000
                },
                _ => new PointerScanOptions()
            };
        }

        /// <summary>
        /// Scans the target for chains leading to each variable's current
        /// address and stores them on the variables as Learned. All
        /// variables must belong to the scanner passed in. Blocking and
        /// CPU/IO heavy: run it off the UI thread.
        /// </summary>
        public static List<LearnSummary> Learn(
            MemoryScanner scanner,
            IReadOnlyList<MemoryVariable> variables,
            PointerScanOptions options,
            IProgress<PointerScanProgress>? progress,
            CancellationToken cancellation)
        {
            // Resolve each variable's live address first (this refreshes
            // Address for variables that already have a path).
            foreach (MemoryVariable variable in variables)
            {
                scanner.ReadValue(variable);
            }

            IPointerScanMemory memory = scanner.CreateScanMemory();
            List<long> targets = variables.Select(v => v.Address.ToInt64()).ToList();

            List<PointerScanResult> results =
                PointerScanner.Scan(memory, targets, options, progress, cancellation);

            List<LearnSummary> summaries = new();

            for (int i = 0; i < variables.Count; i++)
            {
                MemoryVariable variable = variables[i];
                PointerScanResult result = results[i];

                List<string> candidates = new();
                bool isStatic = false;

                ModuleRange? owner = memory.Modules.FirstOrDefault(m => m.Contains(targets[i]));

                PointerPath? direct = null;

                if (owner != null)
                {
                    // Already a fixed spot in a module: the simplest and
                    // most trustworthy "chain" there is.
                    direct = new PointerPath
                    {
                        ModuleName = owner.Name,
                        BaseOffset = targets[i] - owner.Base,
                        IsPointerChain = false
                    };

                    candidates.Add(PointerChainCodec.Encode(direct));
                    isStatic = true;
                }

                candidates.AddRange(result.Candidates.Select(PointerChainCodec.Encode));

                variable.PermanenceTargetVersion = scanner.TargetVersion;
                variable.PermanenceLearnedProcessStartTicks = scanner.StartTimeUtcTicks;
                variable.PermanenceLearnedProcessId = scanner.ProcessId;

                if (direct != null)
                {
                    // A module+offset needs no restart check: it is the same
                    // on every launch by construction.
                    variable.PermanentPaths = new List<PointerPath> { direct };
                    variable.PermanenceCandidates = new List<string>();
                    variable.PermanenceState = PermanenceState.Permanent;
                }
                else
                {
                    variable.PermanenceCandidates = candidates;
                    variable.PermanenceShortlist = new List<string>();
                    variable.PermanenceLastCheckValue = "";
                    variable.PermanenceState = PermanenceState.Learned;
                }

                summaries.Add(new LearnSummary
                {
                    Variable = variable,
                    CandidateCount = candidates.Count,
                    IsStatic = isStatic,
                    Truncated = result.Truncated,
                    Trail = candidates.Count == 0 ? PointerScanDiagnostics.DescribeEmptyResult(result) : ""
                });
            }

            return summaries;
        }

        /// <summary>
        /// True when the target has been relaunched since the chains were
        /// learned - the precondition for a meaningful restart check.
        /// </summary>
        public static bool HasRestartedSinceLearning(MemoryVariable variable)
        {
            MemoryScanner? scanner = variable.Scanner;

            if (scanner == null || scanner.HasExited)
            {
                return false;
            }

            if (variable.PermanenceLearnedProcessStartTicks != 0 && scanner.StartTimeUtcTicks != 0)
            {
                return variable.PermanenceLearnedProcessStartTicks != scanner.StartTimeUtcTicks;
            }

            return variable.PermanenceLearnedProcessId != scanner.ProcessId;
        }

        private const int MaxShortlist = 5000;

        private static object? ReadAt(MemoryScanner scanner, MemoryVariable variable, long address)
        {
            return variable.Type == DataType.String
                ? scanner.ReadString(new IntPtr(address), variable.StringEncoding, variable.StringLength)
                : scanner.ReadValue(new IntPtr(address), variable.Type);
        }

        private static string DescribeReadings(VerificationResult check)
        {
            if (check.Readings.Count == 0)
            {
                return "";
            }

            return " Right now the chains read: " +
                   string.Join(", ", check.Readings.Select(r => $"{r.Text} ({r.Count:N0})")) + ".";
        }

        private static void MarkCheckedNow(MemoryVariable variable, MemoryScanner scanner)
        {
            variable.PermanenceTargetVersion = scanner.TargetVersion;
            variable.PermanenceLearnedProcessStartTicks = scanner.StartTimeUtcTicks;
            variable.PermanenceLearnedProcessId = scanner.ProcessId;
        }

        /// <summary>
        /// Step 2, the restart check: against the WHOLE original scan, keep
        /// every chain that, in the relaunched target, leads to something
        /// reading as <paramref name="expectedValue"/>. All groups are kept
        /// (a common value like a small number can match a wrong group
        /// that is bigger than the right one), and the biggest group goes
        /// into service as a best guess until step 3 confirms it. A long,
        /// distinctive text that points at a single place needs no step 3.
        /// Can also be run again later on a lost variable, in the same
        /// session or after another restart, to recover without scanning.
        /// </summary>
        public static VerifyOutcome RestartCheck(MemoryVariable variable, object expectedValue, string typedText)
        {
            MemoryScanner? scanner = variable.Scanner;

            if (scanner == null || scanner.HasExited)
            {
                return Fail(variable, 0, 0, 0, "The target program isn't running.");
            }

            List<PointerPath> candidates = PointerChainCodec.DecodeAll(variable.PermanenceCandidates);

            if (candidates.Count == 0)
            {
                return Fail(variable, 0, 0, 0, "There are no saved scan results to check. Run the scan first.");
            }

            VerificationResult check = PermanenceVerifier.Verify(
                scanner, candidates, address => ReadAt(scanner, variable, address), expectedValue);

            if (check.AllMatching.Count == 0)
            {
                string reason = check.Resolved == 0
                    ? "None of the saved chains lead anywhere right now. Make sure the program has finished " +
                      "loading and the value is on screen (for the active cue, fire a cue first), then try again."
                    : "The chains lead somewhere, but none read that value." + DescribeReadings(check) +
                      " Check you typed exactly what the program shows right now (for the active cue, fire a cue first).";

                return Fail(variable, check.Checked, check.Resolved, check.Matching, reason);
            }

            variable.PermanentPaths = check.Survivors.Take(MaxPermanentChains).ToList();
            variable.PermanenceShortlist = check.AllMatching.Take(MaxShortlist).Select(PointerChainCodec.Encode).ToList();
            variable.PermanenceLastCheckValue = typedText.Trim();
            variable.Address = new IntPtr(check.ConsensusAddress);
            MarkCheckedNow(variable, scanner);

            bool distinctive =
                variable.Type == DataType.String &&
                typedText.Trim().Length >= 4 &&
                check.Survivors.Count == check.AllMatching.Count;

            variable.PermanenceState = distinctive ? PermanenceState.Permanent : PermanenceState.Provisional;

            string message = distinctive
                ? $"{check.Survivors.Count} chain(s) matched and all agree - this text is distinctive enough, so it is permanent."
                : $"{check.AllMatching.Count:N0} chain(s) matched. A best guess is in use; step 3 confirms it by " +
                  "following a change in the value.";

            return new VerifyOutcome
            {
                Variable = variable,
                Checked = check.Checked,
                Resolved = check.Resolved,
                Matching = check.Matching,
                Survivors = check.AllMatching.Count,
                Succeeded = true,
                NewState = variable.PermanenceState,
                Message = message
            };
        }

        /// <summary>
        /// Step 3, the confirm: after the value has been changed in the
        /// program, keep only the shortlisted chains that read the NEW
        /// value. A chain that matched the first value by coincidence does
        /// not follow the change. Can be repeated with further changes.
        /// </summary>
        public static VerifyOutcome Confirm(MemoryVariable variable, object expectedValue, string typedText)
        {
            MemoryScanner? scanner = variable.Scanner;

            if (scanner == null || scanner.HasExited)
            {
                return Fail(variable, 0, 0, 0, "The target program isn't running.");
            }

            List<PointerPath> shortlist = PointerChainCodec.DecodeAll(variable.PermanenceShortlist);

            if (shortlist.Count == 0)
            {
                return Fail(variable, 0, 0, 0, "There's nothing to confirm yet. Run the restart check (step 2) first.");
            }

            if (string.Equals(typedText.Trim(), variable.PermanenceLastCheckValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    variable, 0, 0, 0,
                    "That's the same value as the last check, which proves nothing new. Change the value in the " +
                    "program first (for the active cue, go to a different cue), then enter the new value.");
            }

            VerificationResult check = PermanenceVerifier.Verify(
                scanner, shortlist, address => ReadAt(scanner, variable, address), expectedValue);

            if (check.Survivors.Count == 0)
            {
                return Fail(
                    variable, check.Checked, check.Resolved, check.Matching,
                    $"None of the {shortlist.Count:N0} chains followed the change - the earlier match was a coincidence." +
                    DescribeReadings(check) +
                    " Go back to step 2 to re-check against the original scan.");
            }

            variable.PermanentPaths = check.Survivors.Take(MaxPermanentChains).ToList();
            variable.PermanenceShortlist = check.Survivors.Take(MaxShortlist).Select(PointerChainCodec.Encode).ToList();
            variable.PermanenceLastCheckValue = typedText.Trim();
            variable.PermanenceState = PermanenceState.Permanent;
            variable.Address = new IntPtr(check.ConsensusAddress);
            MarkCheckedNow(variable, scanner);

            return new VerifyOutcome
            {
                Variable = variable,
                Checked = check.Checked,
                Resolved = check.Resolved,
                Matching = check.Matching,
                Survivors = check.Survivors.Count,
                Succeeded = true,
                NewState = PermanenceState.Permanent,
                Message = $"{check.Survivors.Count} chain(s) followed the change and are now in use. This variable is permanent."
            };
        }

        private static VerifyOutcome Fail(MemoryVariable variable, int checkedCount, int resolved, int matching, string message)
        {
            return new VerifyOutcome
            {
                Variable = variable,
                Checked = checkedCount,
                Resolved = resolved,
                Matching = matching,
                Survivors = 0,
                Succeeded = false,
                NewState = variable.PermanenceState,
                Message = message
            };
        }

        /// <summary>Plain-language status for the dashboard's Permanence column.</summary>
        public static string DescribeStatus(MemoryVariable variable)
        {
            MemoryScanner? scanner = variable.Scanner;

            bool hasChains = variable.PermanentPaths.Count > 0;

            if (variable.PermanenceState == PermanenceState.None && variable.PointerPath != null)
            {
                return scanner == null || scanner.HasExited ? "Manual path - target closed" : "Manual path";
            }

            if (variable.PermanenceState == PermanenceState.None)
            {
                return "Not permanent";
            }

            if (scanner == null || scanner.HasExited)
            {
                return $"Waiting for {variable.SourceProcessName}";
            }

            if (!hasChains)
            {
                return HasRestartedSinceLearning(variable)
                    ? "Learned - ready for restart check"
                    : "Learned - restart target, then check";
            }

            string versionNote =
                variable.PermanenceTargetVersion.Length > 0 &&
                scanner != null &&
                scanner.TargetVersion.Length > 0 &&
                !string.Equals(variable.PermanenceTargetVersion, scanner.TargetVersion, StringComparison.Ordinal)
                    ? " - app updated, re-verify"
                    : "";

            ConsensusResult consensus = variable.LastConsensus;

            if (consensus.Health == PermanenceHealth.Lost)
            {
                return "Lost - not found";
            }

            if (variable.PermanenceState == PermanenceState.Provisional)
            {
                return $"Provisional - confirm in step 3{versionNote}";
            }

            return consensus.Health switch
            {
                PermanenceHealth.Good => $"Permanent {consensus.Agreeing}/{consensus.Total}{versionNote}",
                PermanenceHealth.Degraded => $"Degraded {consensus.Agreeing}/{consensus.Total}{versionNote}",
                _ => "Permanent"
            };
        }
    }
}
