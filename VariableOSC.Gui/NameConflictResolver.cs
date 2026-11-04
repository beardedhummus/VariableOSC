using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// GUI equivalent of the console's R/N/C name-conflict prompt, using
    /// MessageBox/TextPromptDialog instead of Console.ReadLine. Shared by
    /// ScanView's save path and DashboardView's rename/create actions so
    /// duplicate names are caught identically everywhere a name gets
    /// assigned.
    /// </summary>
    public static class NameConflictResolver
    {
        public static string? ResolveVariableName(
            IWin32Window? owner,
            List<MemoryVariable> savedVariables,
            OscBroadcastManager broadcastManager,
            string candidateName,
            MemoryVariable? exclude = null)
        {
            string name = candidateName.Trim();

            while (true)
            {
                MemoryVariable? existing =
                    NameRegistry.FindVariableByName(savedVariables, broadcastManager.Jobs, name);

                if (existing == null || existing == exclude)
                {
                    return name;
                }

                DialogResult choice = MessageBox.Show(
                    owner,
                    $"A variable named \"{name}\" already exists (in {existing.SourceProcessName}).\n\n" +
                    "Yes - Replace the existing variable with this one\n" +
                    "No - Enter a different name\n" +
                    "Cancel - Cancel",
                    "Name Taken",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (choice == DialogResult.Yes)
                {
                    NameRegistry.RemoveVariableByName(savedVariables, broadcastManager.Jobs, name);
                    return name;
                }

                if (choice == DialogResult.No)
                {
                    using TextPromptDialog dialog = new TextPromptDialog("New Name", "Enter a different name:");

                    if (dialog.ShowDialog(owner) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value))
                    {
                        return null;
                    }

                    name = dialog.Value;
                    continue;
                }

                return null;
            }
        }

        public static string? ResolveJobName(
            IWin32Window? owner,
            OscBroadcastManager broadcastManager,
            string candidateName,
            OscJob? exclude = null)
        {
            string name = candidateName.Trim();

            while (NameRegistry.IsJobNameTaken(broadcastManager.Jobs, name, exclude))
            {
                OscJob existing = broadcastManager.Jobs.First(j =>
                    j.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                DialogResult choice = MessageBox.Show(
                    owner,
                    $"A broadcast named \"{name}\" already exists.\n\n" +
                    "Yes - Replace the existing broadcast with this one\n" +
                    "No - Enter a different name\n" +
                    "Cancel - Cancel",
                    "Name Taken",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (choice == DialogResult.Yes)
                {
                    existing.Stop();
                    broadcastManager.Jobs.Remove(existing);
                    return name;
                }

                if (choice == DialogResult.No)
                {
                    using TextPromptDialog dialog = new TextPromptDialog("New Name", "Enter a different name:");

                    if (dialog.ShowDialog(owner) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value))
                    {
                        return null;
                    }

                    name = dialog.Value.Trim();
                    continue;
                }

                return null;
            }

            return name;
        }
    }
}
