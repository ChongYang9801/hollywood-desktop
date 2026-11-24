using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

[assembly: AssemblyTitle("Hollywood Desktop")]
[assembly: AssemblyDescription("Animated terminal wallpaper for Windows")]
[assembly: AssemblyProduct("Hollywood Desktop")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace HollywoodDesktop
{
    internal static class Program
    {
        private const string InstanceName = @"Local\HollywoodDesktop.e3839085";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--stop")
            {
                EventWaitHandle existing;
                if (EventWaitHandle.TryOpenExisting(InstanceName + ".Stop", out existing))
                    using (existing) existing.Set();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == "--license") { ShowLicense(); return; }
            HollywoodOptions options;
            if (args.Any(item => item == "-h" || item == "--help") || !HollywoodOptions.TryParse(args, out options))
            {
                MessageBox.Show(HollywoodOptions.Help, "Hollywood Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (options.Terminal)
            {
                if (!TerminalHost.Run(options))
                    MessageBox.Show("无法打开支持虚拟终端序列的 Windows 控制台。", "Hollywood Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool firstInstance;
            using (var instance = new Mutex(true, InstanceName, out firstInstance))
            {
                if (!firstInstance) return;
                try
                {
                    using (var stop = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Stop"))
                    using (var wallpaper = new WallpaperApplication(stop, options))
                        Application.Run(wallpaper);
                }
                finally
                {
                    instance.ReleaseMutex();
                }
            }
        }

        internal static void ShowLicense()
        {
            using (var window = new Form { Text = "Hollywood Desktop — License and acknowledgments", Size = new Size(740, 540),
                StartPosition = FormStartPosition.CenterScreen, ShowIcon = false })
            using (var text = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical, WordWrap = true })
            {
                text.Text = (HollywoodAssets.ReadText("THIRD-PARTY-NOTICES.txt") + "\n\n" + HollywoodAssets.ReadText("LICENSE"))
                    .Replace("\r", "").Replace("\n", "\r\n");
                window.Controls.Add(text);
                window.ShowDialog();
            }
        }
    }

    internal sealed class WallpaperApplication : ApplicationContext
    {
        private readonly EventWaitHandle stop;
        private readonly HollywoodOptions options;
        private readonly WindowsData data = new WindowsData();
        private readonly Timer frames = new Timer { Interval = 125 };
        private readonly Timer desktopCheck = new Timer { Interval = 500 };
        private readonly Stopwatch animation = new Stopwatch();
        private readonly Stopwatch missingDesktop = Stopwatch.StartNew();
        private readonly List<WallpaperWindow> windows = new List<WallpaperWindow>();
        private readonly Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem pause = new ToolStripMenuItem("暂停动画");
        private readonly ToolStripMenuItem shuffle = new ToolStripMenuItem("重排终端面板");
        private readonly NotifyIcon tray;
        private DesktopLayer layer;
        private DisplayArea[] attachedDisplays = new DisplayArea[0];
        private int scene;
        private double lastShuffle;
        private bool paused;
        private bool hasAttached;

        internal WallpaperApplication(EventWaitHandle stop, HollywoodOptions options)
        {
            this.stop = stop;
            this.options = options;
            paused = !NativeMethods.ClientAnimationsEnabled();
            pause.Text = paused ? "继续动画" : "暂停动画";
            menu.Items.AddRange(new ToolStripItem[] { pause, shuffle, new ToolStripSeparator(),
                new ToolStripMenuItem("许可与致谢", null, delegate { Program.ShowLicense(); }),
                new ToolStripMenuItem("退出并恢复壁纸", null, delegate { ExitThread(); }) });
            tray = new NotifyIcon { Icon = icon ?? SystemIcons.Application, Text = "Hollywood Desktop",
                ContextMenuStrip = menu, Visible = true };
            pause.Click += delegate { TogglePause(); };
            shuffle.Click += delegate { Shuffle(); UpdateFrames(); };
            frames.Tick += delegate { UpdateFrames(); };
            desktopCheck.Tick += delegate { CheckDesktop(); };
            desktopCheck.Start();
        }

        private void CheckDesktop()
        {
            if (stop.WaitOne(0))
            {
                ExitThread();
                return;
            }

            var displays = DisplayArea.GetAll();
            if (layer != null && displays.SequenceEqual(attachedDisplays) &&
                windows.All(window => window.IsAttached(layer))) return;

            ReleaseWindows();
            try
            {
                layer = DesktopLayer.Find();
                if (layer != null && displays.Length != 0 && displays.All(display => layer.Covers(display.Bounds)))
                {
                    for (int i = 0; i < displays.Length; i++)
                        windows.Add(new WallpaperWindow(layer, displays[i], i + 1, options, data));
                    attachedDisplays = displays;
                    hasAttached = true;
                    missingDesktop.Reset();
                    if (!paused)
                    {
                        animation.Start();
                        frames.Start();
                    }
                    UpdateFrames();
                    return;
                }
            }
            catch (Win32Exception)
            {
                // Explorer may be rebuilding its windows. Retry without showing a foreground window.
            }

            ReleaseWindows();
            missingDesktop.Start();
            if (!hasAttached && missingDesktop.Elapsed.TotalSeconds > 8)
            {
                MessageBox.Show("无法找到可以安全放置动画的 Windows 桌面背景层。请确认 Windows 资源管理器正在运行。",
                    "Hollywood Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ExitThread();
            }
        }

        private void TogglePause()
        {
            paused = !paused;
            pause.Text = paused ? "继续动画" : "暂停动画";
            if (paused)
            {
                frames.Stop();
                animation.Stop();
            }
            else if (windows.Count > 0)
            {
                animation.Start();
                frames.Start();
            }
            UpdateFrames();
        }

        private void Shuffle()
        {
            scene++;
            lastShuffle = animation.Elapsed.TotalSeconds;
        }

        private void UpdateFrames()
        {
            double elapsed = animation.Elapsed.TotalSeconds;
            if (!paused && elapsed - lastShuffle >= options.Delay) Shuffle();
            foreach (var window in windows) window.UpdateFrame(scene, elapsed, paused);
        }

        private void ReleaseWindows()
        {
            frames.Stop();
            animation.Stop();
            foreach (var window in windows) window.Dispose();
            windows.Clear();
            layer = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseWindows();
                frames.Dispose();
                desktopCheck.Dispose();
                tray.Visible = false;
                tray.Dispose();
                menu.Dispose();
                if (icon != null) icon.Dispose();
                data.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
