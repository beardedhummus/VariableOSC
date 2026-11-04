using System;

namespace VariableOSC.Gui
{
    /// <summary>
    /// Latest automatic-reconnect event, kept in one place so whichever
    /// screen is showing (the reconnect timer runs on every screen) can
    /// display it without being wired to Program.
    /// </summary>
    internal static class ReconnectStatus
    {
        public static string Message { get; private set; } = "";

        public static DateTime When { get; private set; }

        public static void Report(string message)
        {
            Message = message;
            When = DateTime.Now;
        }
    }
}
