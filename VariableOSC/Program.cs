using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace VariableOSC
{
    class Program
    {
        static readonly Dictionary<int, MemoryScanner> openScanners = new();

        static readonly OscBroadcastManager broadcastManager = new();

        static readonly List<MemoryVariable> savedVariables = new();

        static readonly string VersionString =
            "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

        static void Main()
        {
            try
            {
                // Lets Unicode characters (e.g. the monitor screen's ●)
                // render correctly instead of as "?" - Windows consoles
                // default to an OEM/ANSI code page that doesn't include
                // them. Wrapped in try/catch because this throws if output
                // is redirected (e.g. piped to a file).
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch
            {
            }

            bool running = true;

            while (running)
            {
                PrintMainMenu();

                string? input = Console.ReadLine()?.Trim();

                try
                {
                    switch (input?.ToUpperInvariant())
                    {
                        case "1":
                            RunScanSession();
                            break;

                        case "2":
                            ManageSavedVariables();
                            break;

                        case "3":
                            ManageBroadcasts();
                            break;

                        case "S":
                            SaveSession();
                            break;

                        case "L":
                            LoadSession();
                            break;

                        case "Q":
                            running = false;
                            break;

                        default:
                            Console.WriteLine("Unknown selection.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // A bug in one screen should never take the whole app
                    // down (and with it, any broadcasts currently
                    // sending) - report it and drop back to the main menu.
                    Console.WriteLine();
                    Console.WriteLine($"Something went wrong: {ex.Message}");
                    Console.WriteLine("Returning to the main menu.");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Stopping all OSC broadcasts...");

            broadcastManager.StopAll();

            foreach (MemoryScanner scanner in openScanners.Values)
            {
                scanner.Close();
            }

            Console.WriteLine("VariABLE OSC shutting down.");
        }

        // ------------------------------------------------------------
        // Main menu
        // ------------------------------------------------------------

        static void PrintMainMenu()
        {
            Console.WriteLine();
            Console.WriteLine("=================================");
            Console.WriteLine("          VariABLE OSC");
            Console.WriteLine($"             {VersionString}");
            Console.WriteLine("=================================");

            List<OscJob> runningJobs =
                broadcastManager.Jobs.Where(j => j.IsRunning).ToList();

            if (runningJobs.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Sending:");

                for (int i = 0; i < runningJobs.Count; i++)
                {
                    OscJob job = runningJobs[i];
                    string kind = job.IsGroup ? "Group" : "Variable";

                    Console.WriteLine(
                        $"  {i + 1} - [{kind}] {job.Name} -> " +
                        $"{job.IpAddress}:{job.Port} ({job.Items.Count} var(s))");
                }
            }

            Console.WriteLine();
            Console.WriteLine("1 - Scan for a Variable");
            Console.WriteLine($"2 - Saved Variables ({savedVariables.Count})");
            Console.WriteLine($"3 - OSC Broadcasts ({broadcastManager.Jobs.Count})");
            Console.WriteLine("S - Save Session");
            Console.WriteLine("L - Load Session");
            Console.WriteLine("Q - Quit");
            Console.WriteLine();
            Console.Write("Selection: ");
        }

        // ------------------------------------------------------------
        // Scan flow: pick a process -> pick a type -> enter a value ->
        // refine/monitor/save candidates
        // ------------------------------------------------------------

        static void RunScanSession()
        {
            Process? process = ProcessPicker.Choose();

            if (process == null)
            {
                return;
            }

            MemoryScanner? scanner = GetOrOpenScanner(process);

            if (scanner == null)
            {
                return;
            }

            string processName = SafeProcessName(process);

            while (true)
            {
                SearchCategory? category = SelectSearchCategory();

                if (category == null)
                {
                    return;
                }

                bool startOver;

                switch (category.Value)
                {
                    case SearchCategory.Numbers:
                        startOver = RunNumberScanMenu(scanner, processName);
                        break;

                    case SearchCategory.Bool:
                        startOver = RunTypedNumberScan(scanner, processName, DataType.Bool);
                        break;

                    default:
                        startOver = RunStringScan(scanner, processName);
                        break;
                }

                if (!startOver)
                {
                    return;
                }
            }
        }

        // ------------------------------------------------------------
        // Search category: Numbers / Bool / String. Numbers can either be
        // searched all at once (every numeric type checked together, with
        // the matching type shown alongside each result) or, via the
        // advanced option, narrowed to one specific numeric type up front
        // the way the original per-type search worked.
        // ------------------------------------------------------------

        enum SearchCategory
        {
            Numbers,
            Bool,
            String
        }

        static SearchCategory? SelectSearchCategory()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Select the kind of variable you're looking for:");
                Console.WriteLine();
                Console.WriteLine("1 - Numbers");
                Console.WriteLine("2 - Bool");
                Console.WriteLine("3 - String");
                Console.WriteLine();
                Console.WriteLine("B - Back to main menu");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input)
                {
                    case "1": return SearchCategory.Numbers;
                    case "2": return SearchCategory.Bool;
                    case "3": return SearchCategory.String;
                }

                if (input != null && input.Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                Console.WriteLine("Unknown selection.");
            }
        }

        static bool RunNumberScanMenu(MemoryScanner scanner, string processName)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Number search:");
                Console.WriteLine("1 - Search all number types (recommended)");
                Console.WriteLine("2 - Advanced: choose a specific number type");
                Console.WriteLine();
                Console.WriteLine("B - Back");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input?.ToUpperInvariant())
                {
                    case "1":
                        return RunCombinedNumberScan(scanner, processName);

                    case "2":
                        DataType? type = SelectNumericDataType();

                        if (type == null)
                        {
                            continue;
                        }

                        return RunTypedNumberScan(scanner, processName, type.Value);

                    case "B":
                        return true;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static DataType? SelectNumericDataType()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Select the specific number type to search for:");
                Console.WriteLine();
                Console.WriteLine("1 - Byte");
                Console.WriteLine("2 - Int16");
                Console.WriteLine("3 - Int32");
                Console.WriteLine("4 - Int64");
                Console.WriteLine("5 - Float");
                Console.WriteLine("6 - Double");
                Console.WriteLine();
                Console.WriteLine("B - Back");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input)
                {
                    case "1": return DataType.Byte;
                    case "2": return DataType.Int16;
                    case "3": return DataType.Int32;
                    case "4": return DataType.Int64;
                    case "5": return DataType.Float;
                    case "6": return DataType.Double;
                }

                if (input != null && input.Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                Console.WriteLine("Unknown selection.");
            }
        }

        /// <summary>
        /// A search restricted to one specific DataType up front - used
        /// both for Bool (which only ever has one type) and for the
        /// advanced single-type number search.
        /// </summary>
        static bool RunTypedNumberScan(
            MemoryScanner scanner,
            string processName,
            DataType type)
        {
            object? value = PromptForValue(type);

            if (value == null)
            {
                return true;
            }

            Console.WriteLine();
            Console.WriteLine(
                $"Scanning {processName} for {value} ({type})...");

            List<IntPtr> candidates =
                scanner.FirstScan(value, type);

            Console.WriteLine();
            Console.WriteLine($"Candidates found: {candidates.Count}");

            if (candidates.Count == 0)
            {
                Console.WriteLine(
                    "No matches. Try a different value or data type.");

                return true;
            }

            return CandidateLoop(scanner, processName, type, candidates);
        }

        // ------------------------------------------------------------
        // Combined number scan flow: searches every numeric type at once
        // and tags each result with the type that matched, so the user
        // never has to guess the target's storage type up front.
        // ------------------------------------------------------------

        static bool RunCombinedNumberScan(MemoryScanner scanner, string processName)
        {
            Console.Write("Enter a number to search for (or B to go back): ");
            string? input = Console.ReadLine();

            if (input != null && input.Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!ValueParsing.TryParseAnyNumber(input, out Dictionary<DataType, object> valuesByType, out string error))
            {
                Console.WriteLine();
                Console.WriteLine(error);
                return true;
            }

            Console.WriteLine();
            Console.WriteLine(
                $"Scanning {processName} for {input?.Trim()} across all number types...");

            List<NumberMatch> candidates = scanner.FirstScanAnyNumber(valuesByType);

            Console.WriteLine();
            Console.WriteLine($"Candidates found: {candidates.Count}");

            if (candidates.Count == 0)
            {
                Console.WriteLine("No matches. Try a different value.");
                return true;
            }

            return NumberCandidateLoop(scanner, processName, candidates);
        }

        static bool NumberCandidateLoop(
            MemoryScanner scanner,
            string processName,
            List<NumberMatch> candidates)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("---------------------------------");
                Console.WriteLine($"{candidates.Count} candidate(s) in {processName}");
                Console.WriteLine("---------------------------------");
                Console.WriteLine("1 - Next Scan (narrow down with a new value)");
                Console.WriteLine("2 - Show Candidates");
                Console.WriteLine("3 - Monitor a Candidate");
                Console.WriteLine("4 - Save a Candidate as a Variable");
                Console.WriteLine("N - Start a New Scan");
                Console.WriteLine("M - Back to Main Menu");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input?.ToUpperInvariant())
                {
                    case "1":
                        NextNumberScanStep(scanner, candidates);

                        if (candidates.Count == 0)
                        {
                            Console.WriteLine(
                                "No candidates remain. Starting a new scan.");

                            return true;
                        }

                        break;

                    case "2":
                        ShowNumberCandidates(scanner, candidates);
                        break;

                    case "3":
                        MonitorNumberCandidateStep(scanner, candidates);
                        break;

                    case "4":
                        bool wentToMainMenu =
                            SaveNumberCandidateFlow(scanner, processName, candidates);

                        if (wentToMainMenu)
                        {
                            return false;
                        }

                        break;

                    case "N":
                        return true;

                    case "M":
                        return false;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static void NextNumberScanStep(
            MemoryScanner scanner,
            List<NumberMatch> candidates)
        {
            Console.Write("Enter new number (or B to cancel): ");
            string? input = Console.ReadLine();

            if (input != null && input.Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!ValueParsing.TryParseAnyNumber(input, out Dictionary<DataType, object> valuesByType, out string error))
            {
                Console.WriteLine();
                Console.WriteLine(error);
                return;
            }

            if (!valuesByType.ContainsKey(DataType.Int32))
            {
                Console.WriteLine(
                    "Note: a decimal value only narrows Float/Double candidates - " +
                    "whole-number-type candidates will be dropped.");
            }

            Console.WriteLine();
            Console.WriteLine($"Checking {candidates.Count} candidates...");

            List<NumberMatch> updated = scanner.NextScanAnyNumber(candidates, valuesByType);

            candidates.Clear();
            candidates.AddRange(updated);

            Console.WriteLine();
            Console.WriteLine($"Candidates remaining: {candidates.Count}");
        }

        static void ShowNumberCandidates(
            MemoryScanner scanner,
            List<NumberMatch> candidates)
        {
            Console.WriteLine();

            for (int i = 0; i < candidates.Count; i++)
            {
                NumberMatch candidate = candidates[i];

                object? value = scanner.ReadValue(candidate.Address, candidate.Type);

                Console.WriteLine(
                    $"{i + 1} - 0x{candidate.Address.ToInt64():X} [{candidate.Type}] = " +
                    $"{value?.ToString() ?? "<read failed>"}");
            }
        }

        static void MonitorNumberCandidateStep(
            MemoryScanner scanner,
            List<NumberMatch> candidates)
        {
            Console.WriteLine();
            Console.WriteLine("Select a candidate to monitor:");

            ShowNumberCandidates(scanner, candidates);

            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!int.TryParse(input, out int selection) ||
                selection < 1 ||
                selection > candidates.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            NumberMatch candidate = candidates[selection - 1];
            string label = $"0x{candidate.Address.ToInt64():X} ({candidate.Type})";

            MonitorDisplay.Run(
                $"Watching {label}",
                () =>
                {
                    object? value = scanner.ReadValue(candidate.Address, candidate.Type);
                    return new List<(string Label, string Value)>
                    {
                        (label, value?.ToString() ?? "<read failed>")
                    };
                });
        }

        static bool SaveNumberCandidateFlow(
            MemoryScanner scanner,
            string processName,
            List<NumberMatch> candidates)
        {
            Console.WriteLine();
            Console.WriteLine("Select a candidate to save:");

            ShowNumberCandidates(scanner, candidates);

            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(input, out int selection) ||
                selection < 1 ||
                selection > candidates.Count)
            {
                Console.WriteLine("Invalid selection.");
                return false;
            }

            NumberMatch candidate = candidates[selection - 1];

            string? name = PromptUniqueVariableName();

            if (name == null)
            {
                return false;
            }

            MemoryVariable variable = new MemoryVariable(name, candidate.Address, candidate.Type)
            {
                Scanner = scanner,
                SourceProcessName = processName
            };

            Console.WriteLine();
            Console.WriteLine(
                $"Saved \"{variable.Name}\" ({candidate.Type}) at " +
                $"0x{candidate.Address.ToInt64():X} in {processName}.");

            return PostSaveFlow(variable);
        }

        static bool CandidateLoop(
            MemoryScanner scanner,
            string processName,
            DataType type,
            List<IntPtr> candidates)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("---------------------------------");
                Console.WriteLine($"{candidates.Count} candidate(s) in {processName}");
                Console.WriteLine("---------------------------------");
                Console.WriteLine("1 - Next Scan (narrow down with a new value)");
                Console.WriteLine("2 - Show Candidates");
                Console.WriteLine("3 - Monitor a Candidate");
                Console.WriteLine("4 - Save a Candidate as a Variable");
                Console.WriteLine("N - Start a New Scan");
                Console.WriteLine("M - Back to Main Menu");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input?.ToUpperInvariant())
                {
                    case "1":
                        NextScanStep(scanner, type, candidates);

                        if (candidates.Count == 0)
                        {
                            Console.WriteLine(
                                "No candidates remain. Starting a new scan.");

                            return true;
                        }

                        break;

                    case "2":
                        ShowCandidates(candidates);
                        break;

                    case "3":
                        MonitorCandidateStep(scanner, type, candidates);
                        break;

                    case "4":
                        bool wentToMainMenu =
                            SaveCandidateFlow(scanner, processName, type, candidates);

                        if (wentToMainMenu)
                        {
                            return false;
                        }

                        break;

                    case "N":
                        return true;

                    case "M":
                        return false;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static void NextScanStep(
            MemoryScanner scanner,
            DataType type,
            List<IntPtr> candidates)
        {
            object? value = PromptForValue(type);

            if (value == null)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Checking {candidates.Count} candidates...");

            List<IntPtr> updated =
                scanner.NextScan(candidates, value, type);

            candidates.Clear();
            candidates.AddRange(updated);

            Console.WriteLine();
            Console.WriteLine($"Candidates remaining: {candidates.Count}");
        }

        static void ShowCandidates(List<IntPtr> candidates)
        {
            Console.WriteLine();

            for (int i = 0; i < candidates.Count; i++)
            {
                Console.WriteLine($"{i + 1} - 0x{candidates[i].ToInt64():X}");
            }
        }

        static void MonitorCandidateStep(
            MemoryScanner scanner,
            DataType type,
            List<IntPtr> candidates)
        {
            Console.WriteLine();
            Console.WriteLine("Select a candidate to monitor:");

            ShowCandidates(candidates);

            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!int.TryParse(input, out int selection) ||
                selection < 1 ||
                selection > candidates.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            IntPtr address = candidates[selection - 1];
            string label = $"0x{address.ToInt64():X} ({type})";

            MonitorDisplay.Run(
                $"Watching {label}",
                () =>
                {
                    object? value = scanner.ReadValue(address, type);
                    return new List<(string Label, string Value)>
                    {
                        (label, value?.ToString() ?? "<read failed>")
                    };
                });
        }

        // ------------------------------------------------------------
        // String scan flow (separate from the numeric flow above because
        // string matches carry an encoding and a match length that plain
        // IntPtr addresses don't need to track)
        // ------------------------------------------------------------

        static bool RunStringScan(MemoryScanner scanner, string processName)
        {
            StringSearchOptions? options = ConfigureStringSearchOptions();

            if (options == null)
            {
                return true;
            }

            Console.Write("Enter the text to search for (or B to go back): ");
            string? input = Console.ReadLine();

            if (input != null && input.Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("You must enter some text.");
                return true;
            }

            Console.WriteLine();
            Console.WriteLine($"Scanning {processName} for \"{input}\"...");

            List<StringMatch> candidates = scanner.FirstScanString(input, options);

            Console.WriteLine();
            Console.WriteLine($"Candidates found: {candidates.Count}");

            if (candidates.Count == 0)
            {
                Console.WriteLine("No matches. Try different text or options.");
                return true;
            }

            return StringCandidateLoop(scanner, processName, options, candidates);
        }

        static StringSearchOptions? ConfigureStringSearchOptions()
        {
            StringSearchOptions options = new StringSearchOptions();

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("String search options:");
                Console.WriteLine($"  1 - Case sensitive:  [{(options.CaseSensitive ? "X" : " ")}]");
                Console.WriteLine($"  2 - Null-terminated: [{(options.NullTerminated ? "X" : " ")}]");
                Console.WriteLine(
                    $"  3 - Fixed length:    [{(options.FixedLength ? "X" : " ")}]" +
                    (options.FixedLength ? $"  ({options.FixedLengthChars} characters)" : ""));
                Console.WriteLine();
                Console.WriteLine("Enter a number to toggle it, Enter when ready to search, or B to go back.");
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                {
                    return options;
                }

                if (input.Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                switch (input)
                {
                    case "1":
                        options.CaseSensitive = !options.CaseSensitive;
                        break;

                    case "2":
                        options.NullTerminated = !options.NullTerminated;
                        break;

                    case "3":
                        options.FixedLength = !options.FixedLength;

                        if (options.FixedLength)
                        {
                            Console.Write("Field length, in characters: ");
                            string? lengthInput = Console.ReadLine();

                            if (int.TryParse(lengthInput, out int length) && length > 0)
                            {
                                options.FixedLengthChars = length;
                            }
                            else
                            {
                                Console.WriteLine("Invalid length. Keeping the previous value.");
                            }
                        }

                        break;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static bool StringCandidateLoop(
            MemoryScanner scanner,
            string processName,
            StringSearchOptions options,
            List<StringMatch> candidates)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("---------------------------------");
                Console.WriteLine($"{candidates.Count} candidate(s) in {processName}");
                Console.WriteLine("---------------------------------");
                Console.WriteLine("1 - Next Scan (narrow down with new text)");
                Console.WriteLine("2 - Show Candidates");
                Console.WriteLine("3 - Monitor a Candidate");
                Console.WriteLine("4 - Save a Candidate as a Variable");
                Console.WriteLine("N - Start a New Scan");
                Console.WriteLine("M - Back to Main Menu");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input?.ToUpperInvariant())
                {
                    case "1":
                        NextStringScanStep(scanner, options, candidates);

                        if (candidates.Count == 0)
                        {
                            Console.WriteLine(
                                "No candidates remain. Starting a new scan.");

                            return true;
                        }

                        break;

                    case "2":
                        ShowStringCandidates(scanner, candidates);
                        break;

                    case "3":
                        MonitorStringCandidateStep(scanner, candidates);
                        break;

                    case "4":
                        bool wentToMainMenu =
                            SaveStringCandidateFlow(scanner, processName, options, candidates);

                        if (wentToMainMenu)
                        {
                            return false;
                        }

                        break;

                    case "N":
                        return true;

                    case "M":
                        return false;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static void NextStringScanStep(
            MemoryScanner scanner,
            StringSearchOptions options,
            List<StringMatch> candidates)
        {
            Console.Write("Enter new text (or B to cancel): ");
            string? input = Console.ReadLine();

            if (input == null || input.Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("You must enter some text.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Checking {candidates.Count} candidates...");

            List<StringMatch> updated = scanner.NextScanString(candidates, input, options);

            candidates.Clear();
            candidates.AddRange(updated);

            Console.WriteLine();
            Console.WriteLine($"Candidates remaining: {candidates.Count}");
        }

        static void ShowStringCandidates(
            MemoryScanner scanner,
            List<StringMatch> candidates)
        {
            Console.WriteLine();

            for (int i = 0; i < candidates.Count; i++)
            {
                StringMatch candidate = candidates[i];

                string? rawPreview = scanner.ReadString(
                    candidate.Address,
                    candidate.Encoding,
                    Math.Max(candidate.MatchedCharLength, 32));

                string preview = rawPreview != null ? DisplayText.Sanitize(rawPreview) : "<read failed>";

                Console.WriteLine(
                    $"{i + 1} - 0x{candidate.Address.ToInt64():X} [{candidate.Encoding}] \"{preview}\"");
            }
        }

        static void MonitorStringCandidateStep(
            MemoryScanner scanner,
            List<StringMatch> candidates)
        {
            Console.WriteLine();
            Console.WriteLine("Select a candidate to monitor:");

            ShowStringCandidates(scanner, candidates);

            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!int.TryParse(input, out int selection) ||
                selection < 1 ||
                selection > candidates.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            StringMatch candidate = candidates[selection - 1];
            int readLength = Math.Max(candidate.MatchedCharLength, 32);
            string label = $"0x{candidate.Address.ToInt64():X} (String/{candidate.Encoding})";

            MonitorDisplay.Run(
                $"Watching {label}",
                () =>
                {
                    string? value = scanner.ReadString(candidate.Address, candidate.Encoding, readLength);
                    return new List<(string Label, string Value)>
                    {
                        (label, value != null ? $"\"{DisplayText.Sanitize(value)}\"" : "<read failed>")
                    };
                });
        }

        static bool SaveStringCandidateFlow(
            MemoryScanner scanner,
            string processName,
            StringSearchOptions options,
            List<StringMatch> candidates)
        {
            Console.WriteLine();
            Console.WriteLine("Select a candidate to save:");

            ShowStringCandidates(scanner, candidates);

            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(input, out int selection) ||
                selection < 1 ||
                selection > candidates.Count)
            {
                Console.WriteLine("Invalid selection.");
                return false;
            }

            StringMatch candidate = candidates[selection - 1];

            string? name = PromptUniqueVariableName();

            if (name == null)
            {
                return false;
            }

            int readLength = options.FixedLength
                ? options.FixedLengthChars
                : Math.Max(candidate.MatchedCharLength, 32);

            MemoryVariable variable = new MemoryVariable(name, candidate.Address, DataType.String)
            {
                Scanner = scanner,
                SourceProcessName = processName,
                StringEncoding = candidate.Encoding,
                StringLength = readLength
            };

            Console.WriteLine();
            Console.WriteLine(
                $"Saved \"{variable.Name}\" (String/{candidate.Encoding}) at " +
                $"0x{candidate.Address.ToInt64():X} in {processName}.");

            return PostSaveFlow(variable);
        }

        // ------------------------------------------------------------
        // Saving a candidate + deciding what happens to it
        // ------------------------------------------------------------

        static bool SaveCandidateFlow(
            MemoryScanner scanner,
            string processName,
            DataType type,
            List<IntPtr> candidates)
        {
            Console.WriteLine();
            Console.WriteLine("Select a candidate to save:");

            ShowCandidates(candidates);

            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(input, out int selection) ||
                selection < 1 ||
                selection > candidates.Count)
            {
                Console.WriteLine("Invalid selection.");
                return false;
            }

            IntPtr address = candidates[selection - 1];

            string? name = PromptUniqueVariableName();

            if (name == null)
            {
                return false;
            }

            MemoryVariable variable = new MemoryVariable(name, address, type)
            {
                Scanner = scanner,
                SourceProcessName = processName
            };

            Console.WriteLine();
            Console.WriteLine(
                $"Saved \"{variable.Name}\" ({type}) at 0x{address.ToInt64():X} in {processName}.");

            return PostSaveFlow(variable);
        }

        static bool PostSaveFlow(MemoryVariable variable)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("What would you like to do with this variable?");
                Console.WriteLine("1 - Send it individually via OSC now");
                Console.WriteLine("2 - Add it to an existing OSC broadcast");
                Console.WriteLine("3 - Create a new OSC broadcast with it");
                Console.WriteLine("4 - Just save it for later");
                Console.WriteLine("5 - Convert to a pointer path (survive a restart)");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input)
                {
                    case "1":
                        return StartIndividualBroadcast(variable);

                    case "2":
                        return AddVariableToExistingJob(variable);

                    case "3":
                        return CreateNewBroadcast(variable);

                    case "4":
                        savedVariables.Add(variable);

                        Console.WriteLine(
                            "Saved for later. You'll find it under \"Saved Variables\" on the main menu.");

                        return false;

                    case "5":
                        TryConvertToPointerPath(variable);
                        break;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static bool StartIndividualBroadcast(MemoryVariable variable)
        {
            string? jobName = ResolveUniqueJobName(variable.Name);

            if (jobName == null)
            {
                return false;
            }

            string? oscAddress = PromptOscAddress();

            if (oscAddress == null)
            {
                return false;
            }

            (string ip, int port)? destination = PromptDestination();

            if (destination == null)
            {
                return false;
            }

            OscJob job = new OscJob
            {
                Name = jobName,
                IpAddress = destination.Value.ip,
                Port = destination.Value.port,
                IsGroup = false
            };

            job.Items.Add(new OscBroadcastItem(variable, oscAddress));

            broadcastManager.Jobs.Add(job);

            try
            {
                job.Start();

                Console.WriteLine();
                Console.WriteLine(
                    $"Sending \"{variable.Name}\" to {job.IpAddress}:{job.Port}{oscAddress}");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"Could not start sending: {ex.Message}");
                Console.WriteLine(
                    "It's been saved under OSC Broadcasts - fix the destination and start it from there.");
            }

            return true;
        }

        static bool AddVariableToExistingJob(MemoryVariable variable)
        {
            if (broadcastManager.Jobs.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "There are no existing OSC broadcasts yet. Try creating one instead.");

                return false;
            }

            Console.WriteLine();
            Console.WriteLine("Choose a broadcast to add this variable to:");

            for (int i = 0; i < broadcastManager.Jobs.Count; i++)
            {
                OscJob j = broadcastManager.Jobs[i];
                string status = j.IsRunning ? "sending" : "stopped";

                Console.WriteLine(
                    $"{i + 1} - {j.Name} -> {j.IpAddress}:{j.Port} " +
                    $"[{status}] ({j.Items.Count} var(s))");
            }

            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(input, out int selection) ||
                selection < 1 ||
                selection > broadcastManager.Jobs.Count)
            {
                Console.WriteLine("Invalid selection.");
                return false;
            }

            OscJob job = broadcastManager.Jobs[selection - 1];

            string? oscAddress = PromptOscAddress();

            if (oscAddress == null)
            {
                return false;
            }

            job.Items.Add(new OscBroadcastItem(variable, oscAddress));

            Console.WriteLine();
            Console.WriteLine($"Added \"{variable.Name}\" to \"{job.Name}\".");

            if (!job.IsRunning)
            {
                Console.Write("This broadcast isn't currently running. Start it now? (Y/N): ");

                string? startInput = Console.ReadLine();

                if (startInput != null &&
                    startInput.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        job.Start();
                        Console.WriteLine("Started.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not start: {ex.Message}");
                    }
                }
            }

            return true;
        }

        static bool CreateNewBroadcast(MemoryVariable variable)
        {
            string? name = PromptUniqueJobName("Name for this new OSC broadcast (group): ");

            if (name == null)
            {
                return false;
            }

            (string ip, int port)? destination = PromptDestination();

            if (destination == null)
            {
                return false;
            }

            string? oscAddress = PromptOscAddress();

            if (oscAddress == null)
            {
                return false;
            }

            OscJob job = new OscJob
            {
                Name = name,
                IpAddress = destination.Value.ip,
                Port = destination.Value.port,
                IsGroup = true
            };

            job.Items.Add(new OscBroadcastItem(variable, oscAddress));

            broadcastManager.Jobs.Add(job);

            try
            {
                job.Start();

                Console.WriteLine();
                Console.WriteLine(
                    $"Created \"{job.Name}\" and started sending to {job.IpAddress}:{job.Port}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"Created \"{job.Name}\" but could not start sending: {ex.Message}");
            }

            return true;
        }

        static string? PromptOscAddress()
        {
            Console.Write("OSC address (e.g. /osc/myvar), or B to cancel: ");
            string? input = Console.ReadLine();

            if (input == null || input.Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string address = input.Trim();

            if (string.IsNullOrWhiteSpace(address))
            {
                Console.WriteLine("OSC address cannot be empty.");
                return null;
            }

            if (!address.StartsWith("/"))
            {
                address = "/" + address;
            }

            return address;
        }

        static (string ip, int port)? PromptDestination()
        {
            Console.Write("Destination IP address (or B to cancel): ");
            string? ip = Console.ReadLine();

            if (ip == null || ip.Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            ip = ip.Trim();

            if (string.IsNullOrWhiteSpace(ip))
            {
                Console.WriteLine("IP address cannot be empty.");
                return null;
            }

            Console.Write("Destination port: ");
            string? portInput = Console.ReadLine();

            if (!int.TryParse(portInput, out int port) || port < 1 || port > 65535)
            {
                Console.WriteLine("Invalid port. Please enter a number from 1 to 65535.");
                return null;
            }

            return (ip, port);
        }

        // ------------------------------------------------------------
        // Duplicate-name detection: variable names must be unique across
        // savedVariables AND every broadcast's members (a variable only
        // ever lives in one place at a time, but the name is how the user
        // identifies it, so collisions between the two are just as
        // confusing as collisions within one list). Broadcast/group names
        // must be unique across broadcastManager.Jobs.
        // ------------------------------------------------------------

        static MemoryVariable? FindVariableByName(string name) =>
            NameRegistry.FindVariableByName(savedVariables, broadcastManager.Jobs, name);

        static void RemoveVariableByName(string name) =>
            NameRegistry.RemoveVariableByName(savedVariables, broadcastManager.Jobs, name);

        /// <summary>
        /// Resolves a candidate variable name to something safe to use:
        /// returns it unchanged if free, or walks the user through
        /// replacing the existing variable / picking a different name /
        /// cancelling if it's taken. excludeVariable lets a rename skip
        /// flagging a conflict against the variable being renamed itself.
        /// </summary>
        static string? ResolveUniqueVariableName(string candidateName, MemoryVariable? excludeVariable = null)
        {
            string name = candidateName;

            while (true)
            {
                MemoryVariable? existing = FindVariableByName(name);

                if (existing == null || existing == excludeVariable)
                {
                    return name;
                }

                Console.WriteLine();
                Console.WriteLine(
                    $"A variable named \"{name}\" already exists (in {existing.SourceProcessName}).");
                Console.WriteLine("R - Replace the existing variable with this one");
                Console.WriteLine("N - Use a different name");
                Console.WriteLine("C - Cancel");
                Console.Write("Selection: ");

                string? choice = Console.ReadLine()?.Trim();

                if (choice != null && choice.Equals("R", StringComparison.OrdinalIgnoreCase))
                {
                    RemoveVariableByName(name);
                    return name;
                }

                if (choice != null && choice.Equals("N", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("New name: ");
                    string? newName = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        Console.WriteLine("Name cannot be empty.");
                        continue;
                    }

                    name = newName.Trim();
                    continue;
                }

                return null;
            }
        }

        static string? PromptUniqueVariableName()
        {
            Console.Write("Name this variable: ");
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Variable name cannot be empty. Not saved.");
                return null;
            }

            return ResolveUniqueVariableName(input.Trim());
        }

        static bool IsJobNameTaken(string name, OscJob? excludeJob = null) =>
            NameRegistry.IsJobNameTaken(broadcastManager.Jobs, name, excludeJob);

        static string? ResolveUniqueJobName(string candidateName, OscJob? excludeJob = null)
        {
            string name = candidateName;

            while (IsJobNameTaken(name, excludeJob))
            {
                Console.WriteLine();
                Console.WriteLine($"A broadcast named \"{name}\" already exists.");
                Console.WriteLine("R - Replace the existing broadcast with this one");
                Console.WriteLine("N - Use a different name");
                Console.WriteLine("C - Cancel");
                Console.Write("Selection: ");

                string? choice = Console.ReadLine()?.Trim();

                if (choice != null && choice.Equals("R", StringComparison.OrdinalIgnoreCase))
                {
                    OscJob existing = broadcastManager.Jobs.First(j =>
                        j.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                    existing.Stop();
                    broadcastManager.Jobs.Remove(existing);
                    return name;
                }

                if (choice != null && choice.Equals("N", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("New name: ");
                    string? newName = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        Console.WriteLine("Name cannot be empty.");
                        continue;
                    }

                    name = newName.Trim();
                    continue;
                }

                return null;
            }

            return name;
        }

        static string? PromptUniqueJobName(string promptText, OscJob? excludeJob = null)
        {
            Console.Write(promptText);
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Name cannot be empty.");
                return null;
            }

            return ResolveUniqueJobName(input.Trim(), excludeJob);
        }

        // ------------------------------------------------------------
        // Pointer paths: converting a raw address into a module+offset
        // (or a hand-built pointer chain) so a saved variable can be
        // re-found after the target process restarts. See PointerPath.cs
        // in Core for the full explanation of why this is necessary.
        // ------------------------------------------------------------

        static void TryConvertToPointerPath(MemoryVariable variable)
        {
            if (variable.Scanner == null)
            {
                Console.WriteLine();
                Console.WriteLine("This variable has no active connection to its process right now.");
                return;
            }

            ProcessModule? module = variable.Scanner.FindModuleContaining(variable.Address);

            if (module != null)
            {
                long offset = variable.Address.ToInt64() - module.BaseAddress.ToInt64();

                Console.WriteLine();
                Console.WriteLine(
                    $"This address is inside \"{module.ModuleName}\" at offset 0x{offset:X}.");
                Console.WriteLine(
                    "That looks like a static/global variable - a direct module+offset " +
                    "pointer path should survive the target restarting.");
                Console.WriteLine();
                Console.WriteLine("1 - Use this (recommended)");
                Console.WriteLine("2 - Build a manual pointer chain instead");
                Console.WriteLine("B - Cancel");
                Console.Write("Selection: ");

                string? choice = Console.ReadLine()?.Trim();

                if (choice == "1")
                {
                    variable.PointerPath = new PointerPath
                    {
                        ModuleName = module.ModuleName,
                        BaseOffset = offset,
                        IsPointerChain = false
                    };

                    Console.WriteLine();
                    Console.WriteLine(
                        $"Converted. This variable will now be re-found via " +
                        $"{module.ModuleName}+0x{offset:X} even after a restart.");

                    return;
                }

                if (choice != "2")
                {
                    return;
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"This address (0x{variable.Address.ToInt64():X}) isn't inside any loaded module - " +
                    "it's likely a dynamic/heap allocation. A simple module+offset won't reliably " +
                    "survive a restart.");
                Console.WriteLine();
                Console.WriteLine(
                    "1 - Build a manual pointer chain (useful if you've already found one with " +
                    "another tool's pointer scanner)");
                Console.WriteLine("2 - Skip (keep the raw address; it may not survive a restart)");
                Console.WriteLine("B - Cancel");
                Console.Write("Selection: ");

                string? choice = Console.ReadLine()?.Trim();

                if (choice != "1")
                {
                    return;
                }
            }

            PointerPath? built = BuildManualPointerChain(variable.Scanner, variable.Type);

            if (built != null)
            {
                variable.PointerPath = built;

                Console.WriteLine();
                Console.WriteLine("Converted to a manual pointer chain.");
            }
        }

        static PointerPath? BuildManualPointerChain(MemoryScanner scanner, DataType previewType)
        {
            List<string> modules = scanner.ListModuleNames();

            Console.WriteLine();
            Console.WriteLine("Modules loaded in the target process:");

            for (int i = 0; i < modules.Count; i++)
            {
                Console.WriteLine($"  {i + 1} - {modules[i]}");
            }

            Console.WriteLine();
            Console.Write("Base module (number or name), or B to cancel: ");
            string? moduleInput = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(moduleInput) ||
                moduleInput.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string moduleName;

            if (int.TryParse(moduleInput, out int moduleIndex) &&
                moduleIndex >= 1 &&
                moduleIndex <= modules.Count)
            {
                moduleName = modules[moduleIndex - 1];
            }
            else
            {
                moduleName = moduleInput;
            }

            Console.Write($"Base offset from {moduleName} (hex, e.g. 0x10A2F0), or B to cancel: ");
            string? offsetInput = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(offsetInput) ||
                offsetInput.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!TryParseOffset(offsetInput, out long baseOffset))
            {
                Console.WriteLine("Could not parse that as an offset.");
                return null;
            }

            Console.WriteLine();
            Console.WriteLine($"Does {moduleName}+0x{baseOffset:X} hold a POINTER that needs to be");
            Console.Write("dereferenced, or is it the variable itself? 1=Pointer  2=Direct: ");
            string? kindChoice = Console.ReadLine()?.Trim();

            if (kindChoice == "2")
            {
                return new PointerPath
                {
                    ModuleName = moduleName,
                    BaseOffset = baseOffset,
                    IsPointerChain = false
                };
            }

            List<long> offsets = new();

            while (true)
            {
                PointerPath preview = new PointerPath
                {
                    ModuleName = moduleName,
                    BaseOffset = baseOffset,
                    IsPointerChain = true,
                    Offsets = offsets
                };

                IntPtr? resolved = scanner.ResolvePointerPath(preview);

                Console.WriteLine();

                if (resolved == null)
                {
                    Console.WriteLine("Current chain does not resolve (a pointer along the way is invalid).");
                }
                else
                {
                    object? previewValue = previewType == DataType.String
                        ? scanner.ReadString(resolved.Value, StringEncodingType.Ascii, 32)
                        : scanner.ReadValue(resolved.Value, previewType);

                    Console.WriteLine(
                        $"If this is the last offset, it resolves to 0x{resolved.Value.ToInt64():X} " +
                        $"(current value: {previewValue?.ToString() ?? "<read failed>"})");
                }

                Console.WriteLine();
                Console.Write(
                    $"Offset {offsets.Count + 1} (hex, e.g. 0x18), D if the chain above is complete, or B to cancel: ");

                string? input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input) || input.Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (input.Equals("D", StringComparison.OrdinalIgnoreCase))
                {
                    if (offsets.Count == 0)
                    {
                        Console.WriteLine("You need at least one offset for a pointer chain.");
                        continue;
                    }

                    return new PointerPath
                    {
                        ModuleName = moduleName,
                        BaseOffset = baseOffset,
                        IsPointerChain = true,
                        Offsets = offsets
                    };
                }

                if (!TryParseOffset(input, out long nextOffset))
                {
                    Console.WriteLine("Could not parse that as an offset.");
                    continue;
                }

                offsets.Add(nextOffset);
            }
        }

        static bool TryParseOffset(string? input, out long value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string text = input.Trim();
            bool negative = text.StartsWith("-");

            if (negative)
            {
                text = text.Substring(1);
            }

            bool isHex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

            if (isHex)
            {
                text = text.Substring(2);
            }

            bool ok = isHex
                ? long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed)
                : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

            if (!ok)
            {
                return false;
            }

            value = negative ? -parsed : parsed;
            return true;
        }

        /// <summary>
        /// Lets the user re-find a variable's target from scratch when its
        /// old address or pointer path no longer resolves - e.g. after a
        /// target restart or an update changed its memory layout. Keeps
        /// the variable's Name and wherever it's referenced (savedVariables
        /// / a job's Items) untouched; only Address/Type/PointerPath/string
        /// metadata change. Uses the exact same First Scan -> Next Scan ->
        /// Show/Monitor loop as an original scan (just ending in "use this
        /// candidate" instead of "save a new variable"), so narrowing a
        /// too-broad first search works here too.
        /// </summary>
        static void ReplaceVariableTarget(MemoryVariable variable)
        {
            if (variable.Scanner == null)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "This variable has no active connection to its process. Start a new scan " +
                    "session against that process first.");
                return;
            }

            MemoryScanner scanner = variable.Scanner;

            Console.WriteLine();
            Console.WriteLine($"Re-scanning {variable.SourceProcessName} to replace \"{variable.Name}\"...");

            SearchCategory? category = SelectSearchCategory();

            if (category == null)
            {
                return;
            }

            switch (category.Value)
            {
                case SearchCategory.Numbers:
                    ReplaceViaNumberMenu(variable, scanner);
                    break;

                case SearchCategory.Bool:
                    ReplaceViaTypedSearch(variable, scanner, DataType.Bool);
                    break;

                default:
                    ReplaceViaStringSearch(variable, scanner);
                    break;
            }
        }

        static void ReplaceViaNumberMenu(MemoryVariable variable, MemoryScanner scanner)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Number search:");
                Console.WriteLine("1 - Search all number types (recommended)");
                Console.WriteLine("2 - Advanced: choose a specific number type");
                Console.WriteLine();
                Console.WriteLine("B - Back");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input?.ToUpperInvariant())
                {
                    case "1":
                        ReplaceViaNumbersAllSearch(variable, scanner);
                        return;

                    case "2":
                        DataType? type = SelectNumericDataType();

                        if (type == null)
                        {
                            continue;
                        }

                        ReplaceViaTypedSearch(variable, scanner, type.Value);
                        return;

                    case "B":
                        return;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static void ReplaceViaTypedSearch(MemoryVariable variable, MemoryScanner scanner, DataType type)
        {
            object? value = PromptForValue(type);

            if (value == null)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Scanning for {value} ({type})...");

            List<IntPtr> candidates = scanner.FirstScan(value, type);

            Console.WriteLine();
            Console.WriteLine($"Candidates found: {candidates.Count}");

            if (candidates.Count == 0)
            {
                Console.WriteLine("No matches. Try a different value or data type.");
                return;
            }

            ReplaceCandidateLoop(variable, scanner, type, candidates);
        }

        static void ReplaceCandidateLoop(
            MemoryVariable variable,
            MemoryScanner scanner,
            DataType type,
            List<IntPtr> candidates)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("---------------------------------");
                Console.WriteLine($"{candidates.Count} candidate(s)");
                Console.WriteLine("---------------------------------");
                Console.WriteLine("1 - Next Scan (narrow down with a new value)");
                Console.WriteLine("2 - Show Candidates");
                Console.WriteLine("3 - Monitor a Candidate");
                Console.WriteLine("4 - Use a Candidate to Replace the Target");
                Console.WriteLine("B - Cancel");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input?.ToUpperInvariant())
                {
                    case "1":
                        NextScanStep(scanner, type, candidates);

                        if (candidates.Count == 0)
                        {
                            Console.WriteLine("No candidates remain.");
                            return;
                        }

                        break;

                    case "2":
                        ShowCandidates(candidates);
                        break;

                    case "3":
                        MonitorCandidateStep(scanner, type, candidates);
                        break;

                    case "4":
                        IntPtr? chosen = SelectCandidate(candidates);

                        if (chosen != null)
                        {
                            ApplyReplacement(variable, chosen.Value, type);
                            return;
                        }

                        break;

                    case "B":
                        return;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static void ReplaceViaNumbersAllSearch(MemoryVariable variable, MemoryScanner scanner)
        {
            Console.Write("Enter a number to search for (or B to go back): ");
            string? input = Console.ReadLine();

            if (input != null && input.Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!ValueParsing.TryParseAnyNumber(input, out Dictionary<DataType, object> valuesByType, out string error))
            {
                Console.WriteLine();
                Console.WriteLine(error);
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Scanning for {input?.Trim()} across all number types...");

            List<NumberMatch> candidates = scanner.FirstScanAnyNumber(valuesByType);

            Console.WriteLine();
            Console.WriteLine($"Candidates found: {candidates.Count}");

            if (candidates.Count == 0)
            {
                Console.WriteLine("No matches. Try a different value.");
                return;
            }

            ReplaceNumberCandidateLoop(variable, scanner, candidates);
        }

        static void ReplaceNumberCandidateLoop(
            MemoryVariable variable,
            MemoryScanner scanner,
            List<NumberMatch> candidates)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("---------------------------------");
                Console.WriteLine($"{candidates.Count} candidate(s)");
                Console.WriteLine("---------------------------------");
                Console.WriteLine("1 - Next Scan (narrow down with a new value)");
                Console.WriteLine("2 - Show Candidates");
                Console.WriteLine("3 - Monitor a Candidate");
                Console.WriteLine("4 - Use a Candidate to Replace the Target");
                Console.WriteLine("B - Cancel");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input?.ToUpperInvariant())
                {
                    case "1":
                        NextNumberScanStep(scanner, candidates);

                        if (candidates.Count == 0)
                        {
                            Console.WriteLine("No candidates remain.");
                            return;
                        }

                        break;

                    case "2":
                        ShowNumberCandidates(scanner, candidates);
                        break;

                    case "3":
                        MonitorNumberCandidateStep(scanner, candidates);
                        break;

                    case "4":
                        NumberMatch? chosen = SelectNumberCandidate(scanner, candidates);

                        if (chosen != null)
                        {
                            ApplyReplacement(variable, chosen.Address, chosen.Type);
                            return;
                        }

                        break;

                    case "B":
                        return;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static void ReplaceViaStringSearch(MemoryVariable variable, MemoryScanner scanner)
        {
            StringSearchOptions? options = ConfigureStringSearchOptions();

            if (options == null)
            {
                return;
            }

            Console.Write("Enter the text to search for (or B to go back): ");
            string? input = Console.ReadLine();

            if (input != null && input.Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("You must enter some text.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Scanning for \"{input}\"...");

            List<StringMatch> candidates = scanner.FirstScanString(input, options);

            Console.WriteLine();
            Console.WriteLine($"Candidates found: {candidates.Count}");

            if (candidates.Count == 0)
            {
                Console.WriteLine("No matches. Try different text or options.");
                return;
            }

            ReplaceStringCandidateLoop(variable, scanner, options, candidates);
        }

        static void ReplaceStringCandidateLoop(
            MemoryVariable variable,
            MemoryScanner scanner,
            StringSearchOptions options,
            List<StringMatch> candidates)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("---------------------------------");
                Console.WriteLine($"{candidates.Count} candidate(s)");
                Console.WriteLine("---------------------------------");
                Console.WriteLine("1 - Next Scan (narrow down with new text)");
                Console.WriteLine("2 - Show Candidates");
                Console.WriteLine("3 - Monitor a Candidate");
                Console.WriteLine("4 - Use a Candidate to Replace the Target");
                Console.WriteLine("B - Cancel");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input?.ToUpperInvariant())
                {
                    case "1":
                        NextStringScanStep(scanner, options, candidates);

                        if (candidates.Count == 0)
                        {
                            Console.WriteLine("No candidates remain.");
                            return;
                        }

                        break;

                    case "2":
                        ShowStringCandidates(scanner, candidates);
                        break;

                    case "3":
                        MonitorStringCandidateStep(scanner, candidates);
                        break;

                    case "4":
                        StringMatch? chosen = SelectStringCandidate(scanner, candidates);

                        if (chosen != null)
                        {
                            int readLength = options.FixedLength
                                ? options.FixedLengthChars
                                : Math.Max(chosen.MatchedCharLength, 32);

                            variable.Type = DataType.String;
                            variable.Address = chosen.Address;
                            variable.StringEncoding = chosen.Encoding;
                            variable.StringLength = readLength;
                            variable.PointerPath = null;

                            OfferPointerPathConversion(variable);
                            return;
                        }

                        break;

                    case "B":
                        return;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        /// <summary>
        /// Selects one candidate from a numbered list, correctly handling
        /// B to cancel (unlike a plain int.TryParse check, which used to
        /// report "B" as just an invalid selection).
        /// </summary>
        static IntPtr? SelectCandidate(List<IntPtr> candidates)
        {
            Console.WriteLine();
            Console.WriteLine("Select a candidate to use:");
            ShowCandidates(candidates);
            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!int.TryParse(input, out int selection) || selection < 1 || selection > candidates.Count)
            {
                Console.WriteLine("Invalid selection.");
                return null;
            }

            return candidates[selection - 1];
        }

        static NumberMatch? SelectNumberCandidate(MemoryScanner scanner, List<NumberMatch> candidates)
        {
            Console.WriteLine();
            Console.WriteLine("Select a candidate to use:");
            ShowNumberCandidates(scanner, candidates);
            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!int.TryParse(input, out int selection) || selection < 1 || selection > candidates.Count)
            {
                Console.WriteLine("Invalid selection.");
                return null;
            }

            return candidates[selection - 1];
        }

        static StringMatch? SelectStringCandidate(MemoryScanner scanner, List<StringMatch> candidates)
        {
            Console.WriteLine();
            Console.WriteLine("Select a candidate to use:");
            ShowStringCandidates(scanner, candidates);
            Console.WriteLine();
            Console.Write("Selection (or B to cancel): ");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!int.TryParse(input, out int selection) || selection < 1 || selection > candidates.Count)
            {
                Console.WriteLine("Invalid selection.");
                return null;
            }

            return candidates[selection - 1];
        }

        static void ApplyReplacement(MemoryVariable variable, IntPtr address, DataType type)
        {
            variable.Type = type;
            variable.Address = address;
            variable.PointerPath = null;

            OfferPointerPathConversion(variable);
        }

        static void OfferPointerPathConversion(MemoryVariable variable)
        {
            Console.WriteLine();
            Console.WriteLine($"\"{variable.Name}\" now points at 0x{variable.Address.ToInt64():X}.");
            Console.Write("Convert this to a pointer path now, so it survives a restart too? (Y/N): ");

            string? convert = Console.ReadLine();

            if (convert != null && convert.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                TryConvertToPointerPath(variable);
            }
        }

        /// <summary>
        /// Shared edit menu for one variable, used both from the Saved
        /// Variables list and from a broadcast's member list.
        /// </summary>
        static void EditVariable(MemoryVariable variable)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("=================================");
                Console.WriteLine($"  Edit: {variable.Name}");
                Console.WriteLine("=================================");
                Console.WriteLine($"Process: {variable.SourceProcessName}");
                Console.WriteLine($"Type: {variable.Type}");
                Console.WriteLine($"Address: 0x{variable.Address.ToInt64():X}");
                Console.WriteLine(
                    variable.PointerPath != null
                        ? $"Pointer path: {DescribePointerPath(variable.PointerPath)}"
                        : "Pointer path: none (will not survive a restart of the target)");
                Console.WriteLine();
                Console.WriteLine("1 - Rename");
                Console.WriteLine("2 - Replace target (re-scan to find it again)");
                Console.WriteLine("3 - Convert to / rebuild pointer path");
                Console.WriteLine("B - Back");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input)
                {
                    case "1":
                        Console.Write("New name (or blank to cancel): ");
                        string? newNameInput = Console.ReadLine();

                        if (string.IsNullOrWhiteSpace(newNameInput))
                        {
                            break;
                        }

                        string? resolved = ResolveUniqueVariableName(newNameInput.Trim(), variable);

                        if (resolved != null)
                        {
                            variable.Name = resolved;
                            Console.WriteLine($"Renamed to \"{variable.Name}\".");
                        }

                        break;

                    case "2":
                        ReplaceVariableTarget(variable);
                        break;

                    case "3":
                        TryConvertToPointerPath(variable);
                        break;

                    case "B":
                        return;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static string DescribePointerPath(PointerPath path)
        {
            if (!path.IsPointerChain)
            {
                return $"{path.ModuleName}+0x{path.BaseOffset:X}";
            }

            string offsets = string.Join(
                " -> ",
                path.Offsets.Select(o => $"0x{o:X}"));

            return $"{path.ModuleName}+0x{path.BaseOffset:X} -> {offsets}";
        }

        // ------------------------------------------------------------
        // Saved Variables menu
        // ------------------------------------------------------------

        static void ManageSavedVariables()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("=================================");
                Console.WriteLine("       SAVED VARIABLES");
                Console.WriteLine("=================================");

                if (savedVariables.Count == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        "No saved variables yet. Scan for one from the main menu.");

                    Console.WriteLine();
                    Console.WriteLine("B - Back");
                    Console.Write("Selection: ");
                    Console.ReadLine();

                    return;
                }

                Console.WriteLine();

                for (int i = 0; i < savedVariables.Count; i++)
                {
                    MemoryVariable v = savedVariables[i];
                    string pointerTag = v.PointerPath != null ? " [pointer path]" : "";

                    Console.WriteLine(
                        $"{i + 1} - {v.Name} ({v.Type}) in {v.SourceProcessName} " +
                        $"@ 0x{v.Address.ToInt64():X}{pointerTag}");
                }

                Console.WriteLine();
                Console.WriteLine(
                    "Select a number to send via OSC, E to edit one, M to monitor all, " +
                    "R to remove one, or B to go back.");
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (input.Equals("M", StringComparison.OrdinalIgnoreCase))
                {
                    LiveVariableMonitor monitor = new LiveVariableMonitor(savedVariables);
                    monitor.Start();
                    continue;
                }

                if (input.Equals("E", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("Number to edit: ");
                    string? editInput = Console.ReadLine();

                    if (int.TryParse(editInput, out int editIdx) &&
                        editIdx >= 1 &&
                        editIdx <= savedVariables.Count)
                    {
                        EditVariable(savedVariables[editIdx - 1]);
                    }
                    else
                    {
                        Console.WriteLine("Invalid selection.");
                    }

                    continue;
                }

                if (input.Equals("R", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("Number to remove: ");
                    string? removeInput = Console.ReadLine();

                    if (int.TryParse(removeInput, out int removeIdx) &&
                        removeIdx >= 1 &&
                        removeIdx <= savedVariables.Count)
                    {
                        Console.WriteLine(
                            $"Removed \"{savedVariables[removeIdx - 1].Name}\".");

                        savedVariables.RemoveAt(removeIdx - 1);
                    }
                    else
                    {
                        Console.WriteLine("Invalid selection.");
                    }

                    continue;
                }

                if (int.TryParse(input, out int selection) &&
                    selection >= 1 &&
                    selection <= savedVariables.Count)
                {
                    MemoryVariable variable = savedVariables[selection - 1];
                    savedVariables.RemoveAt(selection - 1);

                    PostSaveFlow(variable);

                    continue;
                }

                Console.WriteLine("Invalid selection.");
            }
        }

        // ------------------------------------------------------------
        // OSC Broadcasts menu (covers both individual sends and groups)
        // ------------------------------------------------------------

        static void ManageBroadcasts()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("=================================");
                Console.WriteLine("       OSC BROADCASTS");
                Console.WriteLine("=================================");

                if (broadcastManager.Jobs.Count == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        "No OSC broadcasts yet. Save a variable and send it from there.");

                    Console.WriteLine();
                    Console.WriteLine("B - Back");
                    Console.Write("Selection: ");
                    Console.ReadLine();

                    return;
                }

                Console.WriteLine();

                for (int i = 0; i < broadcastManager.Jobs.Count; i++)
                {
                    OscJob job = broadcastManager.Jobs[i];
                    string status = job.IsRunning ? "sending" : "stopped";
                    string kind = job.IsGroup ? "Group" : "Variable";

                    Console.WriteLine(
                        $"{i + 1} - [{kind}] {job.Name} -> {job.IpAddress}:{job.Port} " +
                        $"[{status}] ({job.Items.Count} var(s))");

                    if (job.LastError != null)
                    {
                        Console.WriteLine($"      last error: {job.LastError}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Select a number to manage it, or B to go back.");
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                if (input == null || input.Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (int.TryParse(input, out int selection) &&
                    selection >= 1 &&
                    selection <= broadcastManager.Jobs.Count)
                {
                    ManageSingleJob(broadcastManager.Jobs[selection - 1]);
                    continue;
                }

                Console.WriteLine("Invalid selection.");
            }
        }

        static void ManageSingleJob(OscJob job)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("=================================");
                Console.WriteLine($"  {job.Name}");
                Console.WriteLine("=================================");
                Console.WriteLine($"Destination: {job.IpAddress}:{job.Port}");
                Console.WriteLine($"Status: {(job.IsRunning ? "sending" : "stopped")}");

                if (job.LastError != null)
                {
                    Console.WriteLine($"Last error: {job.LastError}");
                }

                Console.WriteLine();
                Console.WriteLine("Members:");

                for (int i = 0; i < job.Items.Count; i++)
                {
                    OscBroadcastItem item = job.Items[i];
                    string value = item.Variable.CurrentValue?.ToString() ?? "-";
                    string pointerTag = item.Variable.PointerPath != null ? " [pointer path]" : "";

                    Console.WriteLine(
                        $"  {i + 1} - {item.Variable.Name} ({item.Variable.Type}) -> " +
                        $"{item.OscAddress}  = {value}{pointerTag}");
                }

                Console.WriteLine();
                Console.WriteLine(job.IsRunning ? "1 - Stop" : "1 - Start");
                Console.WriteLine("2 - Remove a Member");
                Console.WriteLine("3 - Delete This Broadcast");
                Console.WriteLine("4 - Rename This Broadcast");
                Console.WriteLine("5 - Edit a Member");
                Console.WriteLine("B - Back");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                switch (input?.ToUpperInvariant())
                {
                    case "1":
                        if (job.IsRunning)
                        {
                            job.Stop();
                            Console.WriteLine("Stopped.");
                        }
                        else
                        {
                            try
                            {
                                job.Start();
                                Console.WriteLine("Started.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Could not start: {ex.Message}");
                            }
                        }

                        break;

                    case "2":
                        if (job.Items.Count == 0)
                        {
                            Console.WriteLine("No members to remove.");
                            break;
                        }

                        Console.Write("Number to remove: ");
                        string? removeInput = Console.ReadLine();

                        if (int.TryParse(removeInput, out int removeIdx) &&
                            removeIdx >= 1 &&
                            removeIdx <= job.Items.Count)
                        {
                            Console.WriteLine(
                                $"Removed \"{job.Items[removeIdx - 1].Variable.Name}\".");

                            job.Items.RemoveAt(removeIdx - 1);
                        }
                        else
                        {
                            Console.WriteLine("Invalid selection.");
                        }

                        break;

                    case "3":
                        job.Stop();
                        broadcastManager.Jobs.Remove(job);
                        Console.WriteLine("Deleted.");
                        return;

                    case "4":
                        Console.Write("New name for this broadcast (or blank to cancel): ");
                        string? newJobName = Console.ReadLine();

                        if (!string.IsNullOrWhiteSpace(newJobName))
                        {
                            string? resolvedJobName = ResolveUniqueJobName(newJobName.Trim(), job);

                            if (resolvedJobName != null)
                            {
                                job.Name = resolvedJobName;
                                Console.WriteLine($"Renamed to \"{job.Name}\".");
                            }
                        }

                        break;

                    case "5":
                        EditJobMember(job);
                        break;

                    case "B":
                        return;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        static void EditJobMember(OscJob job)
        {
            if (job.Items.Count == 0)
            {
                Console.WriteLine("No members to edit.");
                return;
            }

            Console.Write("Number to edit (or blank to cancel): ");
            string? input = Console.ReadLine();

            if (!int.TryParse(input, out int index) || index < 1 || index > job.Items.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            OscBroadcastItem item = job.Items[index - 1];

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("=================================");
                Console.WriteLine($"  Edit Member: {item.Variable.Name}");
                Console.WriteLine("=================================");
                Console.WriteLine($"OSC Address: {item.OscAddress}");
                Console.WriteLine();
                Console.WriteLine("1 - Edit the Variable (rename / replace target / pointer path)");
                Console.WriteLine("2 - Change OSC Address");
                Console.WriteLine("B - Back");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? choice = Console.ReadLine()?.Trim();

                switch (choice)
                {
                    case "1":
                        EditVariable(item.Variable);
                        break;

                    case "2":
                        string? newAddress = PromptOscAddress();

                        if (newAddress != null)
                        {
                            item.OscAddress = newAddress;
                            Console.WriteLine($"OSC address changed to {newAddress}.");
                        }

                        break;

                    case "B":
                        return;

                    default:
                        Console.WriteLine("Unknown selection.");
                        break;
                }
            }
        }

        // ------------------------------------------------------------
        // Shared helpers
        // ------------------------------------------------------------

        static MemoryScanner? GetOrOpenScanner(Process process)
        {
            if (openScanners.TryGetValue(process.Id, out MemoryScanner? existing))
            {
                return existing;
            }

            try
            {
                MemoryScanner scanner = new MemoryScanner(process);
                openScanners[process.Id] = scanner;
                return scanner;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"Could not open {SafeProcessName(process)}: {ex.Message}");

                Console.WriteLine(
                    "(Tip: VariABLE OSC and the target program usually need to match - " +
                    "both 32-bit or both 64-bit - and you may need to run as Administrator.)");

                return null;
            }
        }

        static string SafeProcessName(Process process)
        {
            try
            {
                return process.ProcessName;
            }
            catch
            {
                return $"PID {process.Id}";
            }
        }

        static object? PromptForValue(DataType type)
        {
            while (true)
            {
                Console.Write($"Enter a {type} value to search for (or B to go back): ");
                string? input = Console.ReadLine();

                if (input != null && input.Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (ValueParsing.TryParseValue(input, type, out object? value, out string error))
                {
                    return value;
                }

                Console.WriteLine();
                Console.WriteLine(error);
                Console.WriteLine();
            }
        }

        // ------------------------------------------------------------
        // Session persistence: save every standalone saved variable and
        // every OSC broadcast (with its members) to a JSON file, and load
        // one back. Loading assumes the same target process(es) are still
        // running - it reconnects by process name and reuses the saved
        // addresses rather than re-scanning.
        // ------------------------------------------------------------

        static void SaveSession()
        {
            Console.WriteLine();

            if (savedVariables.Count == 0 && broadcastManager.Jobs.Count == 0)
            {
                Console.WriteLine("Nothing to save yet - scan for a variable first.");
                return;
            }

            Console.Write("File name to save this session as (e.g. mysession.json): ");
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("File name cannot be empty.");
                return;
            }

            string path = NormalizeSessionPath(input.Trim());

            SessionData data = SessionStore.BuildSessionData(savedVariables, broadcastManager.Jobs);

            try
            {
                SessionStore.Save(path, data);

                Console.WriteLine();
                Console.WriteLine(
                    $"Saved {data.SavedVariables.Count} variable(s) and {data.Jobs.Count} " +
                    $"broadcast(s) to \"{path}\".");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"Could not save session: {ex.Message}");
            }
        }

        static void LoadSession()
        {
            Console.WriteLine();
            Console.Write("File name to load (e.g. mysession.json): ");
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("File name cannot be empty.");
                return;
            }

            string path = NormalizeSessionPath(input.Trim());

            if (!File.Exists(path))
            {
                Console.WriteLine();
                Console.WriteLine($"Could not find \"{path}\".");
                return;
            }

            SessionData data;

            try
            {
                data = SessionStore.Load(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"Could not read session file: {ex.Message}");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Loading {data.SavedVariables.Count} variable(s) and {data.Jobs.Count} broadcast(s)...");

            Dictionary<string, MemoryScanner> resolvedByProcessName = new(StringComparer.OrdinalIgnoreCase);

            foreach (SessionVariableData variableData in data.SavedVariables)
            {
                MemoryVariable? variable = ResolveSessionVariable(variableData, resolvedByProcessName);

                if (variable != null)
                {
                    savedVariables.Add(variable);
                }
            }

            foreach (SessionJobData jobData in data.Jobs)
            {
                string? jobName = ResolveUniqueJobName(jobData.Name);

                if (jobName == null)
                {
                    Console.WriteLine($"  Skipped broadcast \"{jobData.Name}\" - cancelled.");
                    continue;
                }

                OscJob job = new OscJob
                {
                    Name = jobName,
                    IpAddress = jobData.IpAddress,
                    Port = jobData.Port,
                    IsGroup = jobData.IsGroup
                };

                foreach (SessionJobItemData itemData in jobData.Items)
                {
                    MemoryVariable? variable =
                        ResolveSessionVariable(itemData.Variable, resolvedByProcessName);

                    if (variable != null)
                    {
                        job.Items.Add(new OscBroadcastItem(variable, itemData.OscAddress));
                    }
                }

                if (job.Items.Count == 0)
                {
                    Console.WriteLine(
                        $"  Skipped broadcast \"{job.Name}\" - none of its variables could be reconnected.");

                    continue;
                }

                broadcastManager.Jobs.Add(job);

                Console.WriteLine(
                    $"  Loaded broadcast \"{job.Name}\" -> {job.IpAddress}:{job.Port} " +
                    $"({job.Items.Count}/{jobData.Items.Count} var(s) reconnected).");

                if (jobData.WasRunning)
                {
                    Console.Write($"  Resume sending \"{job.Name}\" now? (Y/N): ");
                    string? resumeInput = Console.ReadLine();

                    if (resumeInput != null &&
                        resumeInput.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            job.Start();
                            Console.WriteLine("  Started.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  Could not start: {ex.Message}");
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("Session loaded.");
        }

        static MemoryVariable? ResolveSessionVariable(
            SessionVariableData data,
            Dictionary<string, MemoryScanner> resolvedByProcessName)
        {
            if (!resolvedByProcessName.TryGetValue(data.ProcessName, out MemoryScanner? scanner))
            {
                scanner = ResolveScannerByProcessName(data.ProcessName);
                resolvedByProcessName[data.ProcessName] = scanner!;
            }

            if (scanner == null)
            {
                Console.WriteLine(
                    $"  Skipped \"{data.Name}\" - \"{data.ProcessName}\" is not currently running.");

                return null;
            }

            string? name = ResolveUniqueVariableName(data.Name);

            if (name == null)
            {
                Console.WriteLine($"  Skipped \"{data.Name}\" - cancelled.");
                return null;
            }

            PointerPath? pointerPath = SessionStore.ToPointerPath(data.PointerPath);
            IntPtr address = new IntPtr(data.Address);

            if (pointerPath != null)
            {
                IntPtr? resolved = scanner.ResolvePointerPath(pointerPath);

                if (resolved != null)
                {
                    address = resolved.Value;
                }
                else
                {
                    Console.WriteLine(
                        $"  Warning: \"{name}\"'s pointer path did not resolve (a module isn't " +
                        "loaded yet, or the chain is broken). Loaded anyway with its last-known " +
                        "address - use Edit > Replace Target if it doesn't read correctly.");
                }
            }
            else
            {
                Console.WriteLine(
                    $"  Note: \"{name}\" has no pointer path - if {data.ProcessName} was restarted, " +
                    "its address is likely stale. Use Edit > Replace Target (and consider " +
                    "converting it to a pointer path) if it doesn't read correctly.");
            }

            return new MemoryVariable(name, address, data.Type)
            {
                Scanner = scanner,
                SourceProcessName = data.ProcessName,
                StringEncoding = data.StringEncoding,
                StringLength = data.StringLength,
                PointerPath = pointerPath
            };
        }

        static MemoryScanner? ResolveScannerByProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            Process[] matches = Process.GetProcessesByName(processName);

            try
            {
                if (matches.Length == 0)
                {
                    return null;
                }

                Process chosen = matches[0];

                if (matches.Length > 1)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        $"Multiple running instances of \"{processName}\" found:");

                    for (int i = 0; i < matches.Length; i++)
                    {
                        Console.WriteLine($"  {i + 1} - PID {matches[i].Id}");
                    }

                    Console.Write($"Which one has the variables for \"{processName}\"? (1-{matches.Length}): ");
                    string? input = Console.ReadLine();

                    if (int.TryParse(input, out int selection) &&
                        selection >= 1 &&
                        selection <= matches.Length)
                    {
                        chosen = matches[selection - 1];
                    }
                }

                return GetOrOpenScanner(chosen);
            }
            finally
            {
                foreach (Process process in matches)
                {
                    process.Dispose();
                }
            }
        }

        static string NormalizeSessionPath(string fileName)
        {
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".json";
            }

            return Path.GetFullPath(fileName);
        }
    }
}
