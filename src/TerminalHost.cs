using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace HollywoodDesktop
{
    internal sealed class HollywoodOptions
    {
        internal readonly int Splits, Delay;
        internal readonly bool Terminal;
        internal const string Help = "HollywoodDesktop.exe [--terminal] [-s splits] [-d seconds]\n" +
            "  -s, --splits  Maximum pane count (1–20)\n  -d, --delay   Seconds between layouts (default 10)\n" +
            "  --terminal    Play in the Windows console\n  --stop        Exit the active wallpaper\n  --license     Show license and credits";

        internal HollywoodOptions(int splits, int delay, bool terminal) { Splits = splits; Delay = delay; Terminal = terminal; }

        internal static bool TryParse(string[] args, out HollywoodOptions options)
        {
            int splits = Math.Min(Environment.ProcessorCount * 2, HollywoodWidget.Names.Length);
            int delay = 10;
            bool terminal = false;
            options = null;
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (key == "--terminal") { terminal = true; continue; }
                int value;
                if (++i >= args.Length || !int.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 1) return false;
                switch (key)
                {
                    case "-s": case "--splits": splits = Math.Min(value, HollywoodWidget.Names.Length); break;
                    case "-d": case "--delay": delay = value; break;
                    default: return false;
                }
            }
            options = new HollywoodOptions(splits, delay, terminal);
            return true;
        }
    }

    internal static class TerminalHost
    {
        internal static bool Run(HollywoodOptions options)
        {
            bool ownsAttachment = GetConsoleCP() == 0;
            if (ownsAttachment && !AttachConsole(uint.MaxValue) && !AllocConsole()) return false;
            try
            {
                using (var console = CreateFile("CONOUT$", 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                using (var data = new WindowsData())
                {
                    uint mode;
                    if (console.IsInvalid || !GetConsoleMode(console, out mode) || !SetConsoleMode(console, mode | 5)) return false;
                    int stopped = 0;
                    ConsoleCancelEventHandler cancel = delegate(object sender, ConsoleCancelEventArgs args)
                    { args.Cancel = true; Interlocked.Exchange(ref stopped, 1); };
                    Console.CancelKeyPress += cancel;
                    try
                    {
                        var session = new HollywoodSession(options, data, Environment.TickCount);
                        var ticker = Stopwatch.StartNew();
                        var buffer = new StringBuilder();
                        if (!Write(console, "\u001b[?1049h\u001b[?25l\u001b[2J")) return false;
                        int previousColumns = 0, previousRows = 0;
                        while (Volatile.Read(ref stopped) == 0)
                        {
                            ScreenInfo screen;
                            if (!GetConsoleScreenBufferInfo(console, out screen)) return false;
                            int columns = Math.Max(1, Math.Min(320, screen.Window.Right - screen.Window.Left));
                            int rows = Math.Max(1, Math.Min(140, screen.Window.Bottom - screen.Window.Top + 1));
                            if ((columns != previousColumns || rows != previousRows) && !Write(console, "\u001b[2J")) return false;
                            previousColumns = columns;
                            previousRows = rows;
                            double time = ticker.Elapsed.TotalSeconds;
                            var grid = session.Frame(columns, rows, (int)(time / options.Delay), time, false);
                            if (!Write(console, Format(grid, buffer))) return false;
                            Thread.Sleep(125);
                        }
                        return true;
                    }
                    finally
                    {
                        Console.CancelKeyPress -= cancel;
                        Write(console, "\u001b[0m\u001b[?25h\u001b[?1049l");
                        SetConsoleMode(console, mode);
                    }
                }
            }
            finally { if (ownsAttachment) FreeConsole(); }
        }

        internal static string Format(TerminalGrid grid, StringBuilder text)
        {
            text.Clear();
            byte foreground = 255, background = 255;
            for (int row = 0; row < grid.Rows; row++)
            {
                text.Append("\u001b[").Append(row + 1).Append(";1H");
                for (int col = 0; col < grid.Columns; col++)
                {
                    int index = row * grid.Columns + col;
                    if (grid.Spans[index] == TerminalGrid.Continuation) continue;
                    if (grid.Foreground[index] != foreground && (grid.Text[index] != ' ' || grid.Elements[index] != null))
                    {
                        foreground = grid.Foreground[index];
                        text.Append("\u001b[38;5;").Append(foreground).Append('m');
                    }
                    if (grid.Background[index] != background)
                    {
                        background = grid.Background[index];
                        text.Append("\u001b[48;5;").Append(background).Append('m');
                    }
                    if (grid.Elements[index] == null) text.Append(grid.Text[index]);
                    else text.Append(grid.Elements[index]);
                }
            }
            return text.ToString();
        }

        private static bool Write(SafeFileHandle console, string text)
        {
            const int chunk = 8192;
            int offset = 0;
            while (offset < text.Length)
            {
                int written;
                int length = Math.Min(chunk, text.Length - offset);
                if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length--;
                string piece = text.Substring(offset, length);
                if (!WriteConsole(console, piece, piece.Length, out written, IntPtr.Zero) || written <= 0) return false;
                offset += written;
            }
            return true;
        }

        [StructLayout(LayoutKind.Sequential)] private struct Coord { internal short X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct SmallRect { internal short Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct ScreenInfo
        {
            internal Coord Size, Cursor;
            internal short Attributes;
            internal SmallRect Window;
            internal Coord MaxSize;
        }

        [DllImport("kernel32.dll")] private static extern uint GetConsoleCP();
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint process);
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern bool FreeConsole();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string filename, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(SafeFileHandle console, out uint mode);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(SafeFileHandle console, uint mode);
        [DllImport("kernel32.dll")] private static extern bool GetConsoleScreenBufferInfo(SafeFileHandle console, out ScreenInfo info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WriteConsole(SafeFileHandle console, string buffer, int count, out int written, IntPtr reserved);
    }
}
