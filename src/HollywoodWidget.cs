using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HollywoodDesktop
{
    internal sealed class HollywoodWidget
    {
        internal static readonly string[] Names = { "apg", "atop", "bat", "bmon", "cmatrix", "code", "errno", "figlet",
            "hexdump", "htop", "jp2a", "logs", "man", "map", "mplayer", "pv", "speedometer", "sshart", "stat", "tree" };
        private static readonly byte[] LogColors = { 1, 2, 3, 4, 5, 6 };
        private static readonly Regex SourceTokens = new Regex(@"//.*|#.*|""(?:\\.|[^""\\])*""|\b\d+\b|\b(using|internal|public|private|static|sealed|return|if|foreach|for|while|class|struct|new|void|int|string|bool|var|do|done|case|then|fi|exit)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Lazy<string[]> Errors = new Lazy<string[]>(delegate { return Lines(HollywoodAssets.ReadText("errno.txt")); });
        private static readonly string[] Spelling = { "Alfa", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliet",
            "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray", "Yankee", "Zulu" };
        private const string Digits = "0123456789";
        private const long MiB = 1048576;
        private readonly int seed;
        private readonly Random random;
        private readonly RandomNumberGenerator entropy = SharedEntropy.Provider;
        private string[] cachedLines;
        private int lastBlock = -1;
        private long pvBytes;
        private int pvCycle = -1;
        private byte[] transfer;
        internal readonly int Kind;

        internal HollywoodWidget(int kind, int seed)
        {
            Kind = kind;
            this.seed = seed;
            random = new Random(seed);
        }

        internal void Draw(PaneView view, WindowsData windows, double time)
        {
            switch (Kind)
            {
                case 0: Passwords(view, time); break;
                case 1: Processes(view, windows.SystemStatus(), true); break;
                case 2: Source(view, time, true); break;
                case 3: Network(view, windows.SystemStatus(), view.Height < 11 || view.Width < 48); break;
                case 4: Matrix(view, time); break;
                case 5: Source(view, time, false); break;
                case 6: Errno(view, time); break;
                case 7: Banner(view, time); break;
                case 8: Hexdump(view, time); break;
                case 9: Processes(view, windows.SystemStatus(), false); break;
                case 10: Pictures(view, time); break;
                case 11: Logs(view, windows.Logs(), time); break;
                case 12: Manual(view, windows.Manuals(), time); break;
                case 13: HollywoodAssets.Map.Value.Draw(view, false, true); break;
                case 14: Video(view, time); break;
                case 15: Transfer(view, time); break;
                case 16: Network(view, windows.SystemStatus(), true); break;
                case 17: RandomArt(view, windows.SshArt(), time); break;
                case 18: Stat(view, time); break;
                case 19: Tree(view, time); break;
            }
        }

        private void Passwords(PaneView view, double time)
        {
            int block = (int)(time * 5);
            if (block != lastBlock)
            {
                int count = Math.Min(view.Height, 24);
                var next = new string[count];
                if (cachedLines != null && block == lastBlock + 1 && cachedLines.Length == count)
                    Array.Copy(cachedLines, 1, next, 0, count - 1);
                else
                    for (int i = 0; i < count - 1; i++) next[i] = Password();
                next[count - 1] = Password();
                cachedLines = next;
                lastBlock = block;
            }
            int top = Math.Max(0, view.Height - cachedLines.Length);
            for (int i = 0; i < cachedLines.Length; i++)
                view.Write(0, top + i, cachedLines[i], LogColors[PositiveHash(seed + block + i) % LogColors.Length]);
        }

        private static string Password()
        {
            const string consonants = "bdfghjklmnprstvwxz", vowels = "aeiou";
            var bytes = new byte[12];
            SharedEntropy.Provider.GetBytes(bytes);
            int syllables = 2 + bytes[0] % 3;
            var words = new string[syllables];
            for (int i = 0; i < words.Length; i++)
                words[i] = new string(new[] { consonants[bytes[i * 2 + 1] % consonants.Length], vowels[bytes[i * 2 + 2] % vowels.Length] });
            string pronounce = string.Join("-", words);
            string letters = string.Concat(words);
            string password = char.ToUpperInvariant(letters[0]) + letters.Substring(1) + Digits[bytes[9] % 10];
            if ((bytes[10] & 1) == 1) password += "!?-+"[bytes[11] % 4];
            string spelling = string.Join(" ", password.Select(ch => char.IsLetter(ch) ? Spelling[char.ToUpperInvariant(ch) - 'A'] : ch.ToString()));
            return password + " (" + pronounce + ")   " + spelling;
        }

        private static void Processes(PaneView view, SystemSample sample, bool atop)
        {
            int y = 0;
            if (atop)
            {
                view.Write(0, y++, "ATOP - " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), 0, 6);
                view.Write(0, y++, string.Format(CultureInfo.InvariantCulture, "PRC | #proc {0,5} | #threads {1,5} |", sample.Processes.Length, sample.Threads), 6);
                view.Write(0, y++, string.Format(CultureInfo.InvariantCulture, "CPU | busy {0,5:F0}% | idle {1,5:F0}% |", sample.Cpu, 100 - sample.Cpu), 2);
                view.Write(0, y++, string.Format(CultureInfo.InvariantCulture, "MEM | tot {0,7} | used {1,7} |", Units(sample.TotalMemory), Units(sample.UsedMemory)), 6);
                view.Write(0, y++, string.Format(CultureInfo.InvariantCulture, "SWP | lim {0,7} | used {1,7} |", Units(sample.CommitLimit), Units(sample.Committed)), 6);
                view.Write(0, y++, "  PID   SYSCPU   MEM     CPU CMD", 0, 6);
            }
            else
            {
                Meter(view, y++, "CPU", sample.Cpu / 100, sample.Cpu.ToString("F1", CultureInfo.InvariantCulture) + "%", 2);
                Meter(view, y++, "Mem", Fraction(sample.UsedMemory, sample.TotalMemory), Units(sample.UsedMemory) + "/" + Units(sample.TotalMemory), 2);
                Meter(view, y++, "Swp", Fraction(sample.Committed, sample.CommitLimit), Units(sample.Committed) + "/" + Units(sample.CommitLimit), 1);
                view.Write(0, y++, "  Tasks: " + sample.Processes.Length + ", " + sample.Threads + " thr", 6);
                string header = "   PID   RES     CPU%   MEM%   TIME+     Command";
                view.Write(0, y, new string(' ', view.Width), 0, 2);
                view.Write(0, y++, header, 0, 2);
            }
            for (int i = 0; i < sample.Processes.Length && y < view.Height; i++, y++)
            {
                var row = sample.Processes[i];
                var mem = (ulong)Math.Max(0, row.Memory);
                string line = atop
                    ? string.Format(CultureInfo.InvariantCulture, "{0,5} {1,8:mm\\:ss} {2,7} {3,6:F1}% {4}", row.Id, row.CpuTime, Units(mem), row.Cpu, row.Name)
                    : string.Format(CultureInfo.InvariantCulture, "{0,6} {1,7} {2,6:F1} {3,6:F1} {4,10:mm\\:ss\\.ff} {5}", row.Id, Units(mem), row.Cpu, 100 * Fraction(mem, sample.TotalMemory), row.CpuTime, row.Name);
                view.Write(0, y, line, row.Cpu >= 1 ? (byte)10 : (byte)7);
            }
        }

        private static void Meter(PaneView view, int y, string label, double ratio, string amount, byte color)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            int length = Math.Max(4, Math.Min(36, view.Width - amount.Length - 8));
            view.Write(0, y, label + "[" + new string(' ', length) + "] " + amount, 6);
            view.Write(4, y, new string('|', (int)(length * ratio)), color);
        }

        private void Source(PaneView view, double time, bool bat)
        {
            string[] files = HollywoodAssets.Sources.Value;
            int block = (int)(time / (bat ? 3 : 2));
            int fileIndex = (PositiveHash(seed + block) % files.Length);
            if (block != lastBlock)
            {
                lastBlock = block;
                cachedLines = Lines(HollywoodAssets.ReadText(files[fileIndex]));
            }
            int top = bat ? 2 : 0;
            int first = bat ? 0 : Math.Max(0, cachedLines.Length - view.Height - (int)((time % 2) * 15));
            if (bat)
            {
                view.Write(0, 0, "───────┬" + new string('─', Math.Max(0, view.Width - 8)), 8);
                view.Write(1, 1, "File: " + files[fileIndex], 3);
            }
            for (int i = 0; first + i < cachedLines.Length && top + i < view.Height; i++)
            {
                string line = cachedLines[first + i].Replace("\t", "    ");
                int x = bat ? 9 : 0;
                if (bat) view.Write(0, top + i, (i + 1).ToString().PadLeft(6) + " │ ", 8);
                view.Write(x, top + i, line, 7);
                foreach (Match match in SourceTokens.Matches(line))
                {
                    char firstChar = match.Value[0];
                    byte color = firstChar == '"' ? (byte)3 : firstChar == '/' || firstChar == '#' ? (byte)8 : char.IsDigit(firstChar) ? (byte)13 : (byte)12;
                    view.Write(x + TerminalText.Columns(line, match.Index), top + i, match.Value, color);
                }
            }
        }

        private static void Network(PaneView view, SystemSample sample, bool speedometer)
        {
            var active = sample.Network.FirstOrDefault();
            if (active == null) { view.Write(0, 0, "Waiting for Windows network counters...", 6); return; }
            if (!speedometer)
            {
                int y = 0;
                view.Write(0, y++, " bmon  |  Interfaces               RX Rate       TX Rate", 0, 6);
                foreach (var net in sample.Network.Take(Math.Max(1, view.Height / 3)))
                    view.Write(0, y++, string.Format(CultureInfo.InvariantCulture, "{0}   {1,10}/s {2,10}/s", TerminalText.FitPadded(net.Name, 27), Units(net.RxRate), Units(net.TxRate)), 7);
                y++;
                Plot(view, 0, y, view.Width, Math.Max(2, (view.Height - y) / 2), sample.History, true, 2);
                Plot(view, 0, y + Math.Max(2, (view.Height - y) / 2), view.Width, Math.Max(2, (view.Height - y) / 2), sample.History, false, 4);
                return;
            }
            int split = view.Height / 2;
            Plot(view, 0, 0, view.Width, split, sample.History, true, 2);
            Plot(view, 0, split, view.Width, view.Height - split, sample.History, false, 6);
        }

        private static void Plot(PaneView view, int left, int top, int columns, int rows, NetSample[] history, bool rx, byte color)
        {
            if (rows <= 0 || columns < 1) return;
            var latest = history.LastOrDefault();
            double rate = latest == null ? 0 : rx ? latest.RxRate : latest.TxRate;
            string title = (rx ? "RX" : "TX") + "  " + (latest == null ? "" : latest.Name + "  ") + Units(rate) + "/s";
            view.Write(left, top, title, color);
            if (rows <= 1 || history.Length == 0) return;
            double highest = Math.Max(1, history.Max(point => rx ? point.RxRate : point.TxRate));
            int visible = Math.Min(columns, history.Length), start = history.Length - visible;
            for (int i = 0; i < visible; i++)
            {
                double value = rx ? history[start + i].RxRate : history[start + i].TxRate;
                int blocks = (int)Math.Ceiling(value * (rows - 1) / highest);
                for (int level = 0; level < blocks; level++)
                    view.Put(left + columns - visible + i, top + rows - 1 - level, level == 0 ? '▄' : '█', color);
            }
        }

        private void Matrix(PaneView view, double time)
        {
            for (int x = 0; x < view.Width; x += 2)
            {
                int value = PositiveHash(seed + x * 171);
                int trail = 3 + value % Math.Max(1, view.Height - 3);
                int head = ((int)(time * 18) + value % 997) % (view.Height + trail + 7);
                for (int y = Math.Max(0, head - trail); y < view.Height && y <= head; y++)
                {
                    int distance = head - y;
                    int character = 33 + PositiveHash(value + y * 73 + (int)(time * 4)) % 90;
                    byte color = distance == 0 ? (byte)15 : (character & 1) == 0 ? (byte)10 : (byte)2;
                    view.Put(x, y, (char)character, color);
                }
            }
        }

        private void Errno(PaneView view, double time)
        {
            int block = (int)(time / 0.6);
            string[] values = Errors.Value;
            if (block != lastBlock)
            {
                lastBlock = block;
                cachedLines = values.OrderBy(item => random.Next()).Take(view.Height).ToArray();
            }
            for (int i = 0; i < cachedLines.Length; i++)
            {
                string[] pieces = cachedLines[i].Split(new[] { ' ' }, 3);
                if (pieces.Length != 3) continue;
                view.Write(0, i, string.Format("{0,-17} {1,3} {2}", pieces[0], pieces[1], pieces[2]), LogColors[i % LogColors.Length]);
            }
        }

        private void Banner(PaneView view, double time)
        {
            var banners = HollywoodAssets.Banners.Value;
            string[] lines = Lines(banners[PositiveHash(seed + (int)(time / 2)) % banners.Length].TrimStart('\n'));
            for (int i = 0; i < lines.Length; i++) view.Write(0, i, lines[i], 7);
        }

        private void Hexdump(PaneView view, double time)
        {
            var samples = HollywoodAssets.Binaries.Value;
            if (samples.Length == 0) { view.Write(0, 0, "hexdump: Windows system binaries unavailable", 9); return; }
            int block = (int)(time / 0.7);
            byte[] bytes = samples[PositiveHash(seed + block) % samples.Length].Bytes;
            int total = (bytes.Length + 15) / 16;
            int skip = total > view.Height ? (block * 17) % Math.Max(1, total - view.Height) : 0;
            for (int row = 0; row < view.Height && skip + row < total; row++)
            {
                int offset = (skip + row) * 16;
                var hex = new StringBuilder(offset.ToString("x8") + "  ");
                var ascii = new StringBuilder(" |");
                for (int n = 0; n < 16; n++)
                {
                    if (n == 8) hex.Append(' ');
                    if (offset + n >= bytes.Length) { hex.Append("   "); continue; }
                    byte item = bytes[offset + n];
                    hex.Append(item.ToString("x2")).Append(' ');
                    ascii.Append(item >= 32 && item < 127 ? (char)item : '.');
                }
                view.Write(0, row, hex.ToString() + ascii + "|", row % 4 == 0 ? (byte)10 : (byte)2);
            }
        }

        private void Pictures(PaneView view, double time)
        {
            var pictures = HollywoodAssets.Pictures.Value;
            pictures[PositiveHash(seed + (int)(time / 0.57)) % pictures.Length].Draw(view, false, false);
        }

        private static void Logs(PaneView view, LogLine[] logs, double time)
        {
            if (logs.Length == 0) { view.Write(0, 0, "Waiting for Windows event logs...", 6); return; }
            int latest = (int)(time * 2) % logs.Length;
            for (int row = 0; row < Math.Min(view.Height, logs.Length); row++)
            {
                var log = logs[(latest + row) % logs.Length];
                byte color = log.Level <= 2 ? (byte)9 : log.Level == 3 ? (byte)11 : (byte)6;
                view.Write(0, row, log.Time.ToString("MMM dd HH:mm:ss", CultureInfo.InvariantCulture), 3);
                string prefix = log.Provider + "[" + log.Id + "]: ";
                view.Write(16, row, prefix, color);
                view.Write(16 + TerminalText.Columns(prefix, prefix.Length), row, log.Text, 7);
            }
        }

        private void Manual(PaneView view, string[][] manuals, double time)
        {
            if (manuals.Length == 0) { view.Write(0, 0, "Waiting for Windows command help...", 6); return; }
            int block = (int)(time / 0.8);
            string[] lines = manuals[PositiveHash(seed + block / 4) % manuals.Length];
            int skip = Math.Min(Math.Max(0, lines.Length - view.Height), block % 4 * 8);
            for (int y = 0; y < view.Height && y + skip < lines.Length; y++)
                view.Write(0, y, lines[y + skip], y + skip == 0 ? (byte)14 : (byte)7);
        }

        private void Video(PaneView view, double time)
        {
            var pack = HollywoodAssets.Soundwave.Value;
            int index = (int)((seed % (100 * pack.Rate) + time * 100 * pack.Rate) % pack.Frames.Length);
            pack.Frames[index].Draw(view, true, true);
        }

        private void Transfer(PaneView view, double time)
        {
            int cycle = (int)(time / 11);
            double elapsed = Math.Min(10, time % 11);
            if (cycle != pvCycle) { pvCycle = cycle; pvBytes = 0; }
            if (transfer == null) transfer = new byte[65536];
            long target = Math.Min(10 * MiB, (long)(elapsed * MiB));
            while (pvBytes + transfer.Length <= target)
            {
                entropy.GetBytes(transfer);
                pvBytes += transfer.Length;
            }
            int percent = (int)(pvBytes * 100 / (10 * MiB));
            int bar = Math.Max(4, view.Width - 47);
            int filled = Math.Min(bar, bar * percent / 100);
            var arrow = new string('=', Math.Max(0, filled - 1)) + (filled > 0 ? ">" : "");
            var remaining = TimeSpan.FromSeconds(Math.Max(0, 10 - elapsed));
            string line = string.Format(CultureInfo.InvariantCulture, "{0,7} {1:hh\\:mm\\:ss} [1.00MiB/s] [{2}] {3,3}% ETA {4:hh\\:mm\\:ss}",
                Units(pvBytes), TimeSpan.FromSeconds(elapsed), arrow.PadRight(bar), percent, remaining);
            view.Write(0, Math.Max(0, view.Height - 1), line, 7);
        }

        private void RandomArt(PaneView view, string[] keygen, double time)
        {
            if (keygen.Length > 0)
            {
                int first = view.Height < keygen.Length ? Math.Max(0, Array.FindIndex(keygen, line => line.StartsWith("+---", StringComparison.Ordinal))) : 0;
                for (int y = 0; y + first < keygen.Length && y < view.Height; y++) view.Write(0, y, keygen[y + first], 1);
                return;
            }
            int block = (int)(time / 3);
            if (block != lastBlock)
            {
                lastBlock = block;
                var publicKey = new byte[32];
                entropy.GetBytes(publicKey);
                byte[] fingerprint;
                using (var sha = SHA256.Create()) fingerprint = sha.ComputeHash(publicKey);
                cachedLines = Art(fingerprint);
            }
            for (int y = 0; y < cachedLines.Length && y < view.Height; y++) view.Write(0, y, cachedLines[y], 1);
        }

        private static string[] Art(byte[] fingerprint)
        {
            const int width = 17, height = 9;
            var field = new int[width, height];
            int x = width / 2, y = height / 2;
            foreach (byte value in fingerprint)
                for (int step = 0; step < 4; step++)
                {
                    int direction = value >> (step * 2);
                    x = Math.Max(0, Math.Min(width - 1, x + ((direction & 1) == 0 ? -1 : 1)));
                    y = Math.Max(0, Math.Min(height - 1, y + ((direction & 2) == 0 ? -1 : 1)));
                    field[x, y]++;
                }
            var lines = new string[height + 4];
            lines[0] = "Randomart fingerprint:";
            lines[1] = "SHA256:" + Convert.ToBase64String(fingerprint).TrimEnd('=');
            lines[2] = "+-----[SHA256]----+";
            const string visits = " .o+=*BOX@%&#/^";
            for (int row = 0; row < height; row++)
            {
                var line = new StringBuilder("|");
                for (int col = 0; col < width; col++)
                    line.Append(col == x && row == y ? 'E' : col == width / 2 && row == height / 2 ? 'S' : visits[Math.Min(visits.Length - 1, field[col, row])]);
                lines[row + 3] = line.Append('|').ToString();
            }
            lines[lines.Length - 1] = "+-----------------+";
            return lines;
        }

        private void Stat(PaneView view, double time)
        {
            int block = (int)(time / 0.8);
            if (block != lastBlock)
            {
                lastBlock = block;
                string[] paths = HollywoodAssets.SystemPaths.Value;
                cachedLines = paths.Length == 0 ? new[] { "stat: Windows system paths unavailable" } : Metadata(paths[PositiveHash(seed + block) % paths.Length]);
            }
            for (int y = 0; y < cachedLines.Length; y++) view.Write(0, y, cachedLines[y], 3);
        }

        private static string[] Metadata(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                bool folder = (attributes & FileAttributes.Directory) != 0;
                FileSystemInfo info = folder ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
                string bytes = folder ? "-" : ((FileInfo)info).Length.ToString(CultureInfo.InvariantCulture);
                return new[] { "  File: " + path, "  Size: " + bytes + "    " + (folder ? "directory" : "regular file"),
                    "Access: " + attributes, "Access: " + info.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    "Modify: " + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), " Birth: " + info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss") };
            }
            catch (IOException) { return new[] { "stat: " + path }; }
            catch (UnauthorizedAccessException) { return new[] { "stat: access denied: " + path }; }
        }

        private void Tree(PaneView view, double time)
        {
            int block = (int)(time / 0.5);
            if (block != lastBlock)
            {
                lastBlock = block;
                string[] folders = HollywoodAssets.SystemPaths.Value.Where(Directory.Exists).ToArray();
                string root = folders.Length == 0 ? Environment.SystemDirectory : folders[PositiveHash(seed + block) % folders.Length];
                var lines = new System.Collections.Generic.List<string> { root };
                Walk(root, "", 0, lines, Math.Min(20, view.Height));
                cachedLines = lines.ToArray();
            }
            for (int y = 0; y < cachedLines.Length; y++) view.Write(0, y, cachedLines[y], y == 0 ? (byte)12 : (byte)7);
        }

        private static void Walk(string root, string prefix, int depth, System.Collections.Generic.List<string> lines, int limit)
        {
            if (depth > 2 || lines.Count >= limit) return;
            try
            {
                var entries = Directory.EnumerateFileSystemEntries(root).Take(8).ToArray();
                for (int i = 0; i < entries.Length && lines.Count < limit; i++)
                {
                    string path = entries[i];
                    bool last = i == entries.Length - 1;
                    lines.Add(prefix + (last ? "└── " : "├── ") + Path.GetFileName(path));
                    var attr = File.GetAttributes(path);
                    if ((attr & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
                        Walk(path, prefix + (last ? "    " : "│   "), depth + 1, lines, limit);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static double Fraction(ulong part, ulong full) { return full == 0 ? 0 : (double)part / full; }
        private static string[] Lines(string text) { return text.Replace("\r", "").Split('\n'); }

        private static string Units(double bytes)
        {
            var units = new[] { "B", "KiB", "MiB", "GiB", "TiB" };
            int i = 0;
            while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
            return bytes.ToString(bytes >= 10 || i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + units[i];
        }

        private static int PositiveHash(int number)
        {
            unchecked
            {
                uint value = (uint)number + 0x9e3779b9;
                value = (value ^ (value >> 16)) * 0x85ebca6b;
                value = (value ^ (value >> 13)) * 0xc2b2ae35;
                return (int)((value ^ (value >> 16)) & 0x7fffffff);
            }
        }

        private static class SharedEntropy { internal static readonly RandomNumberGenerator Provider = RandomNumberGenerator.Create(); }
    }
}
