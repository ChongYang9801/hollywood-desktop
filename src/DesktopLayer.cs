using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HollywoodDesktop
{
    internal sealed class DesktopLayer
    {
        internal readonly IntPtr Parent;
        internal readonly IntPtr Icons;
        internal readonly IntPtr Backdrop;

        private DesktopLayer(IntPtr parent, IntPtr icons, IntPtr backdrop)
        {
            Parent = parent;
            Icons = icons;
            Backdrop = backdrop;
        }

        internal bool Raised { get { return Backdrop != IntPtr.Zero; } }

        internal static DesktopLayer Find()
        {
            IntPtr progman = NativeMethods.GetShellWindow();
            if (progman == IntPtr.Zero || progman != NativeMethods.FindWindow("Progman", null)) return null;

            DesktopLayer layer = FindExisting(progman);
            if (layer != null) return layer;

            // Explorer's private wallpaper-layer request. Some shell versions need the older parameters.
            NativeMethods.RequestDesktopLayer(progman, 0xD, 1);
            layer = FindExisting(progman);
            if (layer != null) return layer;
            NativeMethods.RequestDesktopLayer(progman, 0, 0);
            return FindExisting(progman);
        }

        private static DesktopLayer FindExisting(IntPtr progman)
        {
            IntPtr iconView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if ((NativeMethods.GetWindowLong(progman, -20) & 0x00200000) != 0)
            {
                if (iconView == IntPtr.Zero) return null;
                IntPtr backdrop = NativeMethods.FindWindowEx(progman, iconView, "WorkerW", null);
                return backdrop == IntPtr.Zero ? null : new DesktopLayer(progman, iconView, backdrop);
            }

            uint shellProcess = NativeMethods.ProcessForWindow(progman);
            DesktopLayer layer = null;
            NativeMethods.EnumWindows(delegate(IntPtr root, IntPtr data)
            {
                if (NativeMethods.ProcessForWindow(root) != shellProcess ||
                    NativeMethods.FindWindowEx(root, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;

                IntPtr worker = root;
                while ((worker = NativeMethods.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
                {
                    if (NativeMethods.ProcessForWindow(worker) == shellProcess && NativeMethods.IsWindowVisible(worker) &&
                        NativeMethods.FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                    {
                        layer = new DesktopLayer(worker, root, IntPtr.Zero);
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return layer;
        }

        internal bool Covers(Rectangle screen)
        {
            NativeMethods.Rect rect;
            if (!NativeMethods.GetClientRect(Parent, out rect)) return false;
            var origin = new NativeMethods.Point();
            NativeMethods.MapWindowPoints(Parent, IntPtr.Zero, ref origin, 1);
            return new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom).Contains(screen);
        }

        internal bool IsBehindIcons(IntPtr window)
        {
            return NativeMethods.GetAncestor(window, 1) == Parent &&
                (Raised
                    ? NativeMethods.Precedes(Icons, window) && NativeMethods.Precedes(window, Backdrop)
                    : NativeMethods.Precedes(Icons, Parent));
        }
    }

    internal struct DisplayArea : IEquatable<DisplayArea>
    {
        internal readonly Rectangle Bounds;
        internal readonly Rectangle WorkArea;

        private DisplayArea(Rectangle bounds, Rectangle workArea)
        {
            Bounds = bounds;
            WorkArea = Rectangle.Intersect(bounds, workArea);
        }

        internal static DisplayArea[] GetAll()
        {
            var displays = new List<DisplayArea>();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr monitor, IntPtr device, ref NativeMethods.Rect area, IntPtr data)
                {
                    var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf(typeof(NativeMethods.MonitorInfo)) };
                    if (NativeMethods.GetMonitorInfo(monitor, ref info))
                        displays.Add(new DisplayArea(info.Monitor.ToRectangle(), info.Work.ToRectangle()));
                    return true;
                }, IntPtr.Zero);
            return displays.OrderBy(display => display.Bounds.Left).ThenBy(display => display.Bounds.Top).ToArray();
        }

        public bool Equals(DisplayArea other)
        {
            return Bounds == other.Bounds && WorkArea == other.WorkArea;
        }
    }

    internal sealed class WallpaperWindow : NativeWindow, IDisposable
    {
        private readonly TerminalScene renderer;
        private Bitmap frame;
        private bool dirty = true;
        private int scene = -1;
        private double time = -1;
        private bool paused;

        internal WallpaperWindow(DesktopLayer layer, DisplayArea display, int displayNumber)
        {
            Rectangle bounds = display.Bounds;
            var area = display.WorkArea.IsEmpty ? bounds : display.WorkArea;
            area.Offset(-bounds.X, -bounds.Y);
            renderer = new TerminalScene(area, displayNumber);
            try
            {
                CreateHandle(new CreateParams {
                    Parent = layer.Parent,
                    Caption = "Hollywood Desktop Wallpaper",
                    Style = 0x40000000 | 0x08000000, // WS_CHILD | WS_DISABLED
                    ExStyle = 0x08000000 | 0x00000020 | (layer.Raised ? 0x00080000 : 0)
                    // WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED (raised desktop only)
                });

                if (layer.Raised && !NativeMethods.SetLayeredWindowAttributes(Handle, 0, 255, 2))
                    throw new Win32Exception();

                var origin = new NativeMethods.Point { X = bounds.X, Y = bounds.Y };
                NativeMethods.MapWindowPoints(IntPtr.Zero, layer.Parent, ref origin, 1);
                IntPtr after = layer.Raised ? layer.Icons : IntPtr.Zero;
                const uint noSize = 0x0001, noMove = 0x0002, noZOrder = 0x0004;
                const uint noActivate = 0x0010;
                const uint showWindow = 0x0040;
                if (!NativeMethods.SetWindowPos(Handle, after, origin.X, origin.Y, bounds.Width, bounds.Height, noActivate) ||
                    !layer.IsBehindIcons(Handle) ||
                    !NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, noActivate | noSize | noMove | noZOrder | showWindow))
                    throw new Win32Exception();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal bool IsAttached(DesktopLayer layer)
        {
            return Handle != IntPtr.Zero && NativeMethods.IsWindowVisible(Handle) && layer.IsBehindIcons(Handle);
        }

        internal void UpdateFrame(int newScene, double elapsed, bool isPaused)
        {
            if (scene == newScene && time == elapsed && paused == isPaused) return;
            scene = newScene;
            time = elapsed;
            paused = isPaused;
            dirty = true;
            if (Handle != IntPtr.Zero) NativeMethods.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case 0x000F: // WM_PAINT
                    Paint(message.HWnd);
                    return;
                case 0x0014: // WM_ERASEBKGND
                    message.Result = new IntPtr(1);
                    return;
                case 0x0084: // WM_NCHITTEST
                    message.Result = new IntPtr(-1); // HTTRANSPARENT
                    return;
                case 0x0021: // WM_MOUSEACTIVATE
                    message.Result = new IntPtr(3); // MA_NOACTIVATE
                    return;
            }
            base.WndProc(ref message);
        }

        private void Paint(IntPtr window)
        {
            NativeMethods.PaintStruct paint;
            IntPtr device = NativeMethods.BeginPaint(window, out paint);
            if (device == IntPtr.Zero) return;
            try
            {
                NativeMethods.Rect rect;
                if (!NativeMethods.GetClientRect(window, out rect) || rect.Right < 1 || rect.Bottom < 1) return;
                if (frame == null || frame.Width != rect.Right || frame.Height != rect.Bottom)
                {
                    if (frame != null) frame.Dispose();
                    frame = new Bitmap(rect.Right, rect.Bottom, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                    dirty = true;
                }
                if (dirty)
                {
                    using (var graphics = Graphics.FromImage(frame))
                        renderer.Draw(graphics, frame.Size, Math.Max(0, scene), Math.Max(0, time), paused);
                    dirty = false;
                }
                using (var target = Graphics.FromHdc(device)) target.DrawImageUnscaled(frame, 0, 0);
            }
            finally
            {
                NativeMethods.EndPaint(window, ref paint);
            }
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
            if (frame != null) frame.Dispose();
            renderer.Dispose();
        }
    }

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point { internal int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left, Top, Right, Bottom;
            internal Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MonitorInfo
        {
            internal int Size;
            internal Rect Monitor, Work;
            internal uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PaintStruct
        {
            internal IntPtr Device;
            internal int Erase;
            internal Rect Paint;
            internal int Restore, Update;
            private int reserved1, reserved2, reserved3, reserved4, reserved5, reserved6, reserved7, reserved8;
        }

        internal delegate bool WindowCallback(IntPtr window, IntPtr data);
        internal delegate bool MonitorCallback(IntPtr monitor, IntPtr device, ref Rect bounds, IntPtr data);

        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowCallback callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr device, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint command);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern int MapWindowPoints(IntPtr source, IntPtr target, ref Point point, uint count);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);
        [DllImport("user32.dll")] internal static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);
        [DllImport("user32.dll")] internal static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);
        [DllImport("user32.dll")] internal static extern bool EndPaint(IntPtr window, ref PaintStruct paint);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);

        internal static bool ClientAnimationsEnabled()
        {
            int enabled;
            return !SystemParametersInfo(0x1042, 0, out enabled, 0) || enabled != 0; // SPI_GETCLIENTAREAANIMATION
        }

        internal static void RequestDesktopLayer(IntPtr progman, int wParam, int lParam)
        {
            IntPtr ignored;
            SendMessageTimeout(progman, 0x052C, new IntPtr(wParam), new IntPtr(lParam), 2, 250, out ignored);
        }

        internal static uint ProcessForWindow(IntPtr window)
        {
            uint process;
            GetWindowThreadProcessId(window, out process);
            return process;
        }

        internal static bool Precedes(IntPtr first, IntPtr second)
        {
            if (first == IntPtr.Zero || second == IntPtr.Zero || first == second) return false;
            for (var current = GetWindow(first, 2); current != IntPtr.Zero; current = GetWindow(current, 2))
                if (current == second) return true;
            return false;
        }
    }
}
