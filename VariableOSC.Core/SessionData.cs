using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VariableOSC
{
    /// <summary>
    /// JSON-serializable snapshot of one saved variable. Addresses are only
    /// meaningful while the target process stays alive - loading a session
    /// assumes the same process instance is still running, and reconnects
    /// to it by process name rather than by PID (PIDs don't survive a
    /// relaunch of VariABLE OSC itself).
    /// </summary>
    public class SessionVariableData
    {
        public string Name { get; set; } = "";

        public string ProcessName { get; set; } = "";

        public long Address { get; set; }

        public DataType Type { get; set; }

        public StringEncodingType StringEncoding { get; set; } = StringEncodingType.Ascii;

        public int StringLength { get; set; } = 64;

        /// <summary>
        /// When present, this is what actually gets used to re-find the
        /// variable on load - Address above becomes just a last-known
        /// value shown to the user, since a raw address doesn't survive
        /// the target process restarting.
        /// </summary>
        public SessionPointerPathData? PointerPath { get; set; }

        // --- Make Permanent (v0.8.0). All optional, so older session
        // files load unchanged and newer ones still open in older builds
        // (which simply ignore these fields). ---

        public PermanenceState PermanenceState { get; set; } = PermanenceState.None;

        /// <summary>Chains in use, in PointerChainCodec form.</summary>
        public List<string> PermanentPaths { get; set; } = new();

        /// <summary>Learned chains still awaiting the restart check, in PointerChainCodec form.</summary>
        public List<string> PermanenceCandidates { get; set; } = new();

        /// <summary>Chains that matched at the restart check, awaiting the confirm step.</summary>
        public List<string> PermanenceShortlist { get; set; } = new();

        public string PermanenceLastCheckValue { get; set; } = "";

        public string PermanenceTargetVersion { get; set; } = "";

        public long PermanenceLearnedProcessStartTicks { get; set; }

        public int PermanenceLearnedProcessId { get; set; }
    }

    public class SessionPointerPathData
    {
        public string ModuleName { get; set; } = "";

        public long BaseOffset { get; set; }

        public bool IsPointerChain { get; set; }

        public List<long> Offsets { get; set; } = new();
    }

    public class SessionJobItemData
    {
        public SessionVariableData Variable { get; set; } = new();

        public string OscAddress { get; set; } = "";
    }

    public class SessionJobData
    {
        public string Name { get; set; } = "";

        public string IpAddress { get; set; } = "";

        public int Port { get; set; }

        public bool IsGroup { get; set; }

        /// <summary>
        /// Whether this broadcast was actively sending at the moment the
        /// session was saved. Used to offer resuming it on load.
        /// </summary>
        public bool WasRunning { get; set; }

        public List<SessionJobItemData> Items { get; set; } = new();
    }

    /// <summary>
    /// The full save file: every standalone saved variable (not currently
    /// attached to a broadcast) plus every OSC broadcast and its members.
    /// </summary>
    public class SessionData
    {
        public int Version { get; set; } = 1;

        public List<SessionVariableData> SavedVariables { get; set; } = new();

        public List<SessionJobData> Jobs { get; set; } = new();
    }

    public static class SessionStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static SessionData BuildSessionData(
            List<MemoryVariable> savedVariables,
            List<OscJob> jobs)
        {
            SessionData data = new();

            foreach (MemoryVariable variable in savedVariables)
            {
                data.SavedVariables.Add(ToVariableData(variable));
            }

            foreach (OscJob job in jobs)
            {
                SessionJobData jobData = new()
                {
                    Name = job.Name,
                    IpAddress = job.IpAddress,
                    Port = job.Port,
                    IsGroup = job.IsGroup,
                    WasRunning = job.IsRunning
                };

                foreach (OscBroadcastItem item in job.Items)
                {
                    jobData.Items.Add(new SessionJobItemData
                    {
                        Variable = ToVariableData(item.Variable),
                        OscAddress = item.OscAddress
                    });
                }

                data.Jobs.Add(jobData);
            }

            return data;
        }

        private static SessionVariableData ToVariableData(MemoryVariable variable)
        {
            return new SessionVariableData
            {
                Name = variable.Name,
                ProcessName = variable.SourceProcessName,
                Address = variable.Address.ToInt64(),
                Type = variable.Type,
                StringEncoding = variable.StringEncoding,
                StringLength = variable.StringLength,
                PointerPath = ToPointerPathData(variable.PointerPath),
                PermanenceState = variable.PermanenceState,
                PermanentPaths = variable.PermanentPaths.Select(PointerChainCodec.Encode).ToList(),
                PermanenceCandidates = new List<string>(variable.PermanenceCandidates),
                PermanenceShortlist = new List<string>(variable.PermanenceShortlist),
                PermanenceLastCheckValue = variable.PermanenceLastCheckValue,
                PermanenceTargetVersion = variable.PermanenceTargetVersion,
                PermanenceLearnedProcessStartTicks = variable.PermanenceLearnedProcessStartTicks,
                PermanenceLearnedProcessId = variable.PermanenceLearnedProcessId
            };
        }

        /// <summary>Copies the saved Make Permanent data onto a freshly loaded variable.</summary>
        public static void ApplyPermanence(SessionVariableData data, MemoryVariable variable)
        {
            variable.PermanenceState = data.PermanenceState;
            variable.PermanentPaths = PointerChainCodec.DecodeAll(data.PermanentPaths);
            variable.PermanenceCandidates = new List<string>(data.PermanenceCandidates);
            variable.PermanenceShortlist = new List<string>(data.PermanenceShortlist);
            variable.PermanenceLastCheckValue = data.PermanenceLastCheckValue;
            variable.PermanenceTargetVersion = data.PermanenceTargetVersion;
            variable.PermanenceLearnedProcessStartTicks = data.PermanenceLearnedProcessStartTicks;
            variable.PermanenceLearnedProcessId = data.PermanenceLearnedProcessId;

            // Sessions saved before v0.8.2 kept the restart-check survivors
            // in the candidate list and had no shortlist; those survivors
            // are exactly what step 3 needs to confirm.
            if (variable.PermanenceState == PermanenceState.Provisional &&
                variable.PermanenceShortlist.Count == 0)
            {
                variable.PermanenceShortlist = new List<string>(variable.PermanenceCandidates);
            }
        }

        public static SessionPointerPathData? ToPointerPathData(PointerPath? path)
        {
            if (path == null)
            {
                return null;
            }

            return new SessionPointerPathData
            {
                ModuleName = path.ModuleName,
                BaseOffset = path.BaseOffset,
                IsPointerChain = path.IsPointerChain,
                Offsets = new List<long>(path.Offsets)
            };
        }

        public static PointerPath? ToPointerPath(SessionPointerPathData? data)
        {
            if (data == null)
            {
                return null;
            }

            return new PointerPath
            {
                ModuleName = data.ModuleName,
                BaseOffset = data.BaseOffset,
                IsPointerChain = data.IsPointerChain,
                Offsets = new List<long>(data.Offsets)
            };
        }

        public static void Save(string path, SessionData data)
        {
            string json = JsonSerializer.Serialize(data, Options);

            File.WriteAllText(path, json);
        }

        public static SessionData Load(string path)
        {
            string json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<SessionData>(json, Options)
                ?? new SessionData();
        }
    }
}
