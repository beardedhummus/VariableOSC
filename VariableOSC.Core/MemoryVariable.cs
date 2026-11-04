using System;
using System.Collections.Generic;

namespace VariableOSC
{
    /// <summary>
    /// Where a variable stands in the Make Permanent workflow.
    /// </summary>
    public enum PermanenceState
    {
        /// <summary>No learned chains. Only a raw address or a hand-made PointerPath.</summary>
        None,

        /// <summary>A pointer scan found candidate chains; none have been checked across a restart yet.</summary>
        Learned,

        /// <summary>Passed the restart check, so a best guess is in use, but not yet confirmed by changing the value (step 3). A number can match by coincidence; following a change cannot.</summary>
        Provisional,

        /// <summary>Confirmed: survived a restart and then followed a change in the value.</summary>
        Permanent
    }

    public class MemoryVariable
    {
        public string Name { get; set; }

        public IntPtr Address { get; set; }

        public DataType Type { get; set; }

        public object? CurrentValue { get; set; }

        /// <summary>
        /// The scanner (and therefore the target process) this variable was
        /// found in. Kept so the variable can be read later on independent
        /// of whatever scan session originally found it - this is what lets
        /// a broadcast group mix variables from different target programs.
        /// </summary>
        public MemoryScanner? Scanner { get; set; }

        public string SourceProcessName { get; set; } = "";

        /// <summary>
        /// When set, Address is re-derived from this on every read instead
        /// of being trusted as-is (see MemoryScanner.ReadValue). A variable
        /// without one only works until the target process restarts, since
        /// a raw address becomes garbage the moment ASLR relocates things.
        /// </summary>
        public PointerPath? PointerPath { get; set; }

        /// <summary>
        /// Only meaningful when Type is DataType.String: which encoding
        /// this variable was found under, and how many characters to
        /// read back on each poll.
        /// </summary>
        public StringEncodingType StringEncoding { get; set; } = StringEncodingType.Ascii;

        public int StringLength { get; set; } = 64;

        // ------------------------------------------------------------
        // Make Permanent data (see PointerScanner / PermanenceVerifier)
        // ------------------------------------------------------------

        public PermanenceState PermanenceState { get; set; } = PermanenceState.None;

        /// <summary>
        /// Chains in use right now. When there are any, they take priority
        /// over PointerPath: every read resolves all of them and goes with
        /// the address most of them agree on.
        /// </summary>
        public List<PointerPath> PermanentPaths { get; set; } = new();

        /// <summary>
        /// The whole scan: every encoded chain (see PointerChainCodec) found
        /// for this variable. Kept until the variable is scanned again, so a
        /// lost variable can be re-checked against the original scan
        /// instead of starting over. Kept as strings because a scan can
        /// leave thousands of them.
        /// </summary>
        public List<string> PermanenceCandidates { get; set; } = new();

        /// <summary>Every chain that matched at the restart check (all groups, not only the biggest); step 3 narrows this by following a change.</summary>
        public List<string> PermanenceShortlist { get; set; } = new();

        /// <summary>The value typed at the last check, so step 3 can insist the value was really changed.</summary>
        public string PermanenceLastCheckValue { get; set; } = "";

        /// <summary>Main-module file version of the target when the chains were learned; a change means the app was updated and the chains need re-checking.</summary>
        public string PermanenceTargetVersion { get; set; } = "";

        /// <summary>
        /// When the target process had started at the moment chains were
        /// learned (UTC ticks). A different start time later means the
        /// target has been restarted since, so the restart check can run.
        /// </summary>
        public long PermanenceLearnedProcessStartTicks { get; set; }

        /// <summary>PID the chains were learned under; the fallback restart signal when a start time isn't available.</summary>
        public int PermanenceLearnedProcessId { get; set; }

        /// <summary>Result of the most recent consensus read; refreshed on every ReadValue of a permanent variable.</summary>
        public ConsensusResult LastConsensus { get; set; }

        /// <summary>True when the variable has anything that lets it be found again after the target restarts.</summary>
        public bool CanSurviveRestart =>
            PermanentPaths.Count > 0 ||
            PointerPath != null ||
            PermanenceCandidates.Count > 0;

        /// <summary>Drops all Make Permanent data (used when the variable is re-targeted).</summary>
        public void ClearPermanence()
        {
            PermanenceState = PermanenceState.None;
            PermanentPaths = new List<PointerPath>();
            PermanenceCandidates = new List<string>();
            PermanenceShortlist = new List<string>();
            PermanenceLastCheckValue = "";
            PermanenceTargetVersion = "";
            PermanenceLearnedProcessStartTicks = 0;
            PermanenceLearnedProcessId = 0;
            LastConsensus = default;
        }

        public MemoryVariable(
            string name,
            IntPtr address,
            DataType type)
        {
            Name = name;
            Address = address;
            Type = type;
        }
    }
}
