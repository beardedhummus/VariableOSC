# VariABLE OSC

**Find a live value inside another Windows program and stream it out over OSC.**

VariABLE OSC scans a running program's memory for a variable you can see on screen (a score, a level, a cue name, a status string), lets you save it, and broadcasts its live value to any OSC receiver: lighting and video software, show control, TouchOSC, Companion, Max, Resolume, and so on. It works with programs that have no API or integration of their own.

Its signature feature is **Make Permanent**: after a one-time guided setup, a saved variable finds itself again every time the target program is closed and reopened, and the broadcast resumes on its own.

> **Status: pre-release (v0.8.2).** It is under active testing and the saved-session format can still change between versions. See [CHANGELOG.md](CHANGELOG.md) for the full history. It is read-only: VariABLE OSC only *reads* the target program's memory and never writes to it.

---

## What you need

- Windows (the app is a Windows program)
- The target program running
- Sometimes: run VariABLE OSC **as Administrator**, if the target is elevated itself or Windows refuses the connection
- Nothing to install: the published `.exe` is self-contained (no .NET runtime needed)

VariABLE OSC is a 64-bit program. It can read both 64-bit and 32-bit target programs, and Make Permanent handles both.

---

## Quick start

1. **Start a session** and **pick the target process** from the list. By default only windowed programs are shown; tick *Show all processes* for background ones.
2. **Scan for the variable.**
   - *Numbers:* leave *Advanced* unticked to search every numeric type at once, so you don't need to know whether the program stores it as an integer or a float.
   - *Bool* or *String* (ASCII or UTF-16, with case, null-terminator and fixed-length options).
   - Enter the value you see on screen and press **First Scan**. Change the value in the target program, enter the new value, and press **Next Scan**. Repeat until one candidate is left, then **Save Selected as Variable** and name it.
3. On the **Dashboard**, send it out:
   - **Send Individually**: one variable to one destination.
   - **Create New Broadcast** / **Add to Broadcast**: several variables sharing one destination (IP and port), each with its own OSC address.
   - Broadcasts send about ten times a second, from a background thread, so the UI stays responsive.
4. **Save Session** so everything (variables, broadcasts, destinations) reloads next time, even after the target restarts.

Fuller walkthrough: [VariableOSC-User-Guide.txt](#documentation).

---

## OSC output

Each value is sent as a standard OSC message to the address you choose (suggested from the variable's name, e.g. `/Health`):

| Variable type | OSC type tag |
|---|---|
| Byte, Int16, Int32 | `i` (32-bit integer) |
| Int64 | `h` (64-bit integer) |
| Float | `f` |
| Double | `d` |
| Bool | `T` / `F` |
| String | `s` |

A broadcast can mix variables from different target programs.

---

## Make Permanent (surviving restarts)

A raw memory address is only valid for one run of a program: restart it and the same value lives somewhere else. **Make Permanent** teaches VariABLE OSC how to *find* the value again. It scans the live program for every chain of pointers that leads from a fixed spot in the program to your variable, then uses restarts to throw away the false ones.

Select a variable and press **Make Permanent...**. The wizard has three steps:

1. **Scan.** Searches memory for candidate pointer chains (Quick, Standard or Deep). Variables already at a fixed spot inside the program are made permanent immediately.
2. **Restart check.** Close and reopen the target program. VariABLE OSC notices the restart on its own and re-attaches. Type what the variable shows now, and the wizard keeps every chain that leads to that value.
3. **Confirm.** Change the value in the program and type the new value. Only chains that *follow the change* survive, which rules out chains that matched by coincidence (a small number like 40 appears all over memory). Long, distinctive text skips this step.

The **Permanence** column then shows live health (green *Permanent*, amber *Provisional* / *Degraded*, red *Lost*). While a variable is permanent:

- Every read resolves **all** of its chains and goes with the address most of them agree on.
- If nothing resolves, or there is no clear majority, the variable reads as *not found* and its broadcast **pauses** instead of sending a wrong value. Obviously wrong data (control characters in text, NaN in floats) is rejected the same way.
- A background check re-attaches variables to a relaunched program automatically. A broadcast left running resumes when the program comes back, and a saved session loaded while the program is closed waits for it to appear.
- The full scan is kept (in the session file) until you scan again, so a lost variable can be recovered with **Re-check Saved Scan** instead of starting over.

**Good to know**

- It needs one restart-and-check cycle per variable; it is guided, not instant.
- It cannot help with programs that constantly move or rebuild their objects, such as some garbage-collected game engines, or values that only exist on screen briefly.
- A value reached through a short-lived object may read *Lost* until the program creates it (for example, an "active item" that only exists once something has been selected).
- If an app update changes how the program lays out its data, the chains may stop working; the Permanence column flags when the program's file version has changed since the scan.
- Making several variables permanent in one scan is faster than doing them one at a time.

---

## Sessions and files

- A **session** is a plain `.json` file holding your variables, their saved pointer chains and scan results, and your broadcasts. Loading a session reconnects to the target **by process name**, not by process ID, so it survives restarts.
- The last few sessions you opened are remembered in `%APPDATA%\VariableOSC\recent-sessions.json`.
- Duplicate variable or broadcast names are caught in both the scan screen and the dashboard, with a choice to replace or rename.

---

## Troubleshooting

| Symptom | Try |
|---|---|
| "Could not open the process" | Run VariABLE OSC as Administrator. |
| The scan finds nothing | Check the value is exactly what the program stores (a displayed `3.14` may be a float or double); use the all-types number search. For text, try the other encoding or turn off *case sensitive*. |
| Too many candidates | Change the value in the program and **Next Scan** again; repeat a few times. |
| Make Permanent finds "no chains" | The wizard explains where the trail ended (nothing points at the address, the trail stops, or it never reaches the program's fixed memory). Try a different candidate: a text search often finds several copies and only one is the live one. |
| *Lost* right after a restart | The program may not have created the value yet. Get it to show the value (for example, trigger the action) and it recovers by itself. |
| Values stop updating after the target closes | Normal. Permanent variables (and ones with a pointer path) resume when it returns; others must be re-scanned. |

---

## Build from source

You need the **.NET 10 SDK**. The WinForms projects target `net10.0-windows` but can be built on macOS or Linux (`EnableWindowsTargeting` is already set); they can only be *run* on Windows.

```bash
# Build everything the app uses
dotnet build VariableOSC.Gui/VariableOSC.Gui.csproj

# Publish a self-contained, single-file Windows executable
cd VariableOSC.Gui
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish/win-x64
```

Run the algorithm tests (cross-platform; they exercise the pointer scanner, restart check and consensus logic against a simulated process, 32-bit and 64-bit):

```bash
dotnet run --project VariableOSC.SelfTest -c Release
dotnet run --project VariableOSC.SelfTest -c Release -- --perf   # adds a 256 MB scan
```

### Project layout

| Project | What it is |
|---|---|
| `VariableOSC.Core` | All non-UI logic: memory scanning, OSC output, sessions, the pointer scanner, Make Permanent workflow, auto-reconnect. Shared by both front ends. |
| `VariableOSC.Gui` | The main WinForms app: welcome, process picker, scan, dashboard, Make Permanent wizard, the dark "Cyber" theme. |
| `VariableOSC` | The original menu-driven console edition. Same core, older feature set (no Make Permanent wizard). Handy as a debugging tool. |
| `VariableOSC.SelfTest` | Plain console project that links the pure algorithm files and tests them against a fake process. Developer tool, not shipped. |

The version lives in one place, [`Directory.Build.props`](Directory.Build.props), and every project inherits it.

