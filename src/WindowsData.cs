using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HollywoodDesktop
{
    internal sealed class WindowsData : IDisposable
    {
        private volatile SystemSample system = new SystemSample();
        private volatile LogLine[] logs = new LogLine[0];
        private volatile string[][] manuals = new string[0][];
        private volatile string[] sshArt = new string[0];
        private readonly Dictionary<int, long> previousCpu = new Dictionary<int, long>();
        private readonly Dictionary<string, NetSample> previousNet = new Dictionary<string, NetSample>();
        private readonly List<NetSample> history = new List<NetSample>();
        private long sampledAt, previousIdle, previousTotal, logUpdateAt, sshUpdateAt;
        private int systemPending, logPending, manualPending, sshPending;
        private static readonly string SshKeygen = Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh-keygen.exe");
        private static readonly bool HasSshKeygen = File.Exists(SshKeygen);
        private readonly object sshLock = new object();
        private bool disposed;

        internal SystemSample SystemStatus()
        {
            if (Stopwatch.GetTimestamp() - Interlocked.Read(ref sampledAt) >= Stopwatch.Frequency &&
                Interlocked.CompareExchange(ref systemPending, 1, 0) == 0)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { system = ReadSystem(); }
                    catch (Win32Exception) { }
                    finally { Interlocked.Exchange(ref systemPending, 0); }
                });
            }
            return system;
        }

        internal LogLine[] Logs()
        {
            if (Stopwatch.GetTimestamp() - Interlocked.Read(ref logUpdateAt) >= 4 * Stopwatch.Frequency &&
                Interlocked.CompareExchange(ref logPending, 1, 0) == 0)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { logs = ReadLogs(); }
                    finally
                    {
                        Interlocked.Exchange(ref logUpdateAt, Stopwatch.GetTimestamp());
                        Interlocked.Exchange(ref logPending, 0);
                    }
                });
            }
            return logs;
        }

        internal string[][] Manuals()
        {
            if (Interlocked.CompareExchange(ref manualPending, 1, 0) == 0)
                ThreadPool.QueueUserWorkItem(delegate { manuals = ReadManuals(); });
            return manuals;
        }

        internal string[] SshArt()
        {
            if (HasSshKeygen && Stopwatch.GetTimestamp() - Interlocked.Read(ref sshUpdateAt) >= 3 * Stopwatch.Frequency &&
                Interlocked.CompareExchange(ref sshPending, 1, 0) == 0)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        lock (sshLock)
                        {
                            if (disposed) return;
                            var art = ReadSshArt();
                            if (art.Length > 0) sshArt = art;
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref sshUpdateAt, Stopwatch.GetTimestamp());
                        Interlocked.Exchange(ref sshPending, 0);
                    }
                });
            }
            return sshArt;
        }

        public void Dispose()
        {
            lock (sshLock) disposed = true;
        }

        private SystemSample ReadSystem()
        {
            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Exchange(ref sampledAt, now);
            double elapsed = last == 0 ? 0 : (now - last) / (double)Stopwatch.Frequency;
            var sample = new SystemSample();
            long idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                long total = kernel + user;
                if (previousTotal != 0 && total > previousTotal)
                    sample.Cpu = 100.0 * (1 - Math.Max(0, idle - previousIdle) / (double)(total - previousTotal));
                previousIdle = idle;
                previousTotal = total;
            }

            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (GlobalMemoryStatusEx(ref memory))
            {
                sample.TotalMemory = memory.Total;
                sample.UsedMemory = memory.Total - memory.Available;
                sample.CommitLimit = memory.CommitLimit;
                sample.Committed = memory.CommitLimit - memory.AvailableCommit;
            }

            var processes = new List<ProcessSample>();
            var currentCpu = new Dictionary<int, long>();
            foreach (var process in Process.GetProcesses())
                using (process)
                {
                    try
                    {
                        var row = new ProcessSample { Id = process.Id, Name = process.ProcessName, Memory = process.WorkingSet64,
                            Threads = process.Threads.Count };
                        try
                        {
                            long ticks = process.TotalProcessorTime.Ticks;
                            long previous;
                            currentCpu[row.Id] = ticks;
                            if (elapsed > 0 && previousCpu.TryGetValue(row.Id, out previous) && ticks >= previous)
                                row.Cpu = 100 * (ticks - previous) / (TimeSpan.TicksPerSecond * elapsed);
                            row.CpuTime = TimeSpan.FromTicks(ticks);
                        }
                        catch (Win32Exception) { }
                        processes.Add(row);
                    }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                }
            previousCpu.Clear();
            foreach (var row in currentCpu) previousCpu.Add(row.Key, row.Value);
            sample.Processes = processes.OrderByDescending(row => row.Cpu).ThenByDescending(row => row.Memory).ToArray();
            sample.Threads = processes.Sum(row => row.Threads);

            sample.Network = ReadNetwork(elapsed);
            var route = sample.Network.FirstOrDefault(net => net.HasGateway) ?? sample.Network.FirstOrDefault();
            if (route != null) history.Add(route);
            if (history.Count > 96) history.RemoveAt(0);
            sample.History = history.ToArray();
            return sample;
        }

        private NetSample[] ReadNetwork(double elapsed)
        {
            var list = new List<NetSample>();
            try
            {
                foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up) continue;
                    try
                    {
                        var stats = network.GetIPv4Statistics();
                        var row = new NetSample { Name = network.Name, Rx = stats.BytesReceived, Tx = stats.BytesSent,
                            HasGateway = network.GetIPProperties().GatewayAddresses.Any() };
                        NetSample last;
                        if (elapsed > 0 && previousNet.TryGetValue(network.Id, out last))
                        {
                            row.RxRate = Math.Max(0, row.Rx - last.Rx) / elapsed;
                            row.TxRate = Math.Max(0, row.Tx - last.Tx) / elapsed;
                        }
                        previousNet[network.Id] = row;
                        list.Add(row);
                    }
                    catch (NetworkInformationException) { }
                    catch (NotSupportedException) { }
                }
            }
            catch (NetworkInformationException) { }
            return list.OrderByDescending(item => item.HasGateway).ThenBy(item => item.Name).ToArray();
        }

        private static LogLine[] ReadLogs()
        {
            var messages = new List<LogLine>();
            foreach (string channel in new[] { "System", "Application" })
            {
                try
                {
                    var query = new EventLogQuery(channel, PathType.LogName) { ReverseDirection = true };
                    using (var reader = new EventLogReader(query))
                    for (int i = 0; i < 24; i++)
                    {
                        using (var record = reader.ReadEvent(TimeSpan.FromMilliseconds(120)))
                        {
                            if (record == null) break;
                            string message = "";
                            try { message = record.FormatDescription() ?? ""; }
                            catch (EventLogException) { }
                            string firstLine = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                            messages.Add(new LogLine { Time = record.TimeCreated ?? DateTime.MinValue,
                                Level = record.Level ?? 0, Provider = record.ProviderName ?? channel, Id = record.Id,
                                Text = firstLine.Length > 240 ? firstLine.Substring(0, 240) : firstLine });
                        }
                    }
                }
                catch (EventLogException) { }
                catch (UnauthorizedAccessException) { }
                catch (InvalidOperationException) { }
            }
            return messages.OrderBy(item => item.Time).ToArray();
        }

        private static string[][] ReadManuals()
        {
            string executable = Path.Combine(Environment.SystemDirectory, "help.exe");
            var result = new List<string[]>();
            foreach (string verb in new[] { "DIR", "FINDSTR", "COPY", "TREE", "SORT", "ATTRIB", "FC", "TYPE" })
            {
                try
                {
                    string[] lines = ReadCommand(executable, verb, Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage));
                    if (lines.Length > 1)
                        result.Add(new[] { verb + "(1)                 Windows Command Help" }.Concat(lines).ToArray());
                }
                catch (Win32Exception) { }
                catch (IOException) { }
                catch (InvalidOperationException) { }
            }
            return result.ToArray();
        }

        private static string[] ReadSshArt()
        {
            string temp = Path.GetTempPath();
            string key = Path.Combine(temp, "hollywood-key-" + Guid.NewGuid().ToString("N"));
            try
            {
                return ReadCommand(SshKeygen, "-t ed25519 -N \"\" -C hollywood -f \"" + key + "\"", Encoding.UTF8)
                    .Select(line => line.Replace(key, "%TEMP%\\hollywood-key")).ToArray();
            }
            catch (Win32Exception) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
            finally
            {
                foreach (string path in new[] { key, key + ".pub" })
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
            return new string[0];
        }

        private static string[] ReadCommand(string executable, string arguments, Encoding encoding)
        {
            var info = new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardOutputEncoding = encoding };
            using (var process = Process.Start(info))
            {
                if (process == null) return new string[0];
                var lines = new List<string>();
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                { if (args.Data != null && lines.Count < 64) lines.Add(args.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.StandardInput.Close();
                if (!process.WaitForExit(1100))
                {
                    process.Kill();
                    process.WaitForExit(300);
                    return new string[0];
                }
                process.WaitForExit();
                return lines.ToArray();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            internal uint Length, Load;
            internal ulong Total, Available, CommitLimit, AvailableCommit, Virtual, AvailableVirtual, ExtendedVirtual;
        }

        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    }

    internal sealed class SystemSample
    {
        internal double Cpu;
        internal ulong TotalMemory, UsedMemory, CommitLimit, Committed;
        internal int Threads;
        internal ProcessSample[] Processes = new ProcessSample[0];
        internal NetSample[] Network = new NetSample[0], History = new NetSample[0];
    }

    internal sealed class ProcessSample
    {
        internal int Id, Threads;
        internal long Memory;
        internal double Cpu;
        internal string Name;
        internal TimeSpan CpuTime;
    }

    internal sealed class NetSample
    {
        internal string Name;
        internal long Rx, Tx;
        internal double RxRate, TxRate;
        internal bool HasGateway;
    }

    internal sealed class LogLine
    {
        internal DateTime Time;
        internal int Level, Id;
        internal string Provider, Text;
    }
}
