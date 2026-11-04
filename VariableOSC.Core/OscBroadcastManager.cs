using System.Collections.Generic;

namespace VariableOSC
{
    /// <summary>
    /// Holds every OSC broadcast (individual variable sends and named
    /// groups) that's been created this session, running or not.
    /// </summary>
    public class OscBroadcastManager
    {
        public List<OscJob> Jobs { get; } = new();

        public void StopAll()
        {
            foreach (OscJob job in Jobs)
            {
                job.Stop();
            }
        }
    }
}
