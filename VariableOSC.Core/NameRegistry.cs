using System;
using System.Collections.Generic;
using System.Linq;

namespace VariableOSC
{
    /// <summary>
    /// Pure name-collision checks shared by both front ends: variable
    /// names must be unique across standalone saved variables AND every
    /// broadcast's members combined, and broadcast/group names must be
    /// unique among themselves. Deliberately stateless (takes the current
    /// lists as parameters) - each front end owns its own conflict
    /// resolution UX (console prompts vs. a GUI dialog) around these.
    /// </summary>
    public static class NameRegistry
    {
        public static MemoryVariable? FindVariableByName(
            List<MemoryVariable> savedVariables,
            List<OscJob> jobs,
            string name)
        {
            MemoryVariable? found = savedVariables.FirstOrDefault(v =>
                v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (found != null)
            {
                return found;
            }

            foreach (OscJob job in jobs)
            {
                OscBroadcastItem? item = job.Items.FirstOrDefault(i =>
                    i.Variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (item != null)
                {
                    return item.Variable;
                }
            }

            return null;
        }

        public static void RemoveVariableByName(
            List<MemoryVariable> savedVariables,
            List<OscJob> jobs,
            string name)
        {
            savedVariables.RemoveAll(v =>
                v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            foreach (OscJob job in jobs)
            {
                job.Items.RemoveAll(i =>
                    i.Variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static bool IsJobNameTaken(List<OscJob> jobs, string name, OscJob? excludeJob = null)
        {
            return jobs.Any(j =>
                j != excludeJob && j.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
