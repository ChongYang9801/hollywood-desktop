using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Text;

namespace HollywoodDesktop
{
    internal sealed class TerminalScene : IDisposable
    {
        private static readonly Color Ink = Color.FromArgb(5, 10, 14);
        private static readonly Color PanelInk = Color.FromArgb(10, 20, 25);
        private static readonly Color Line = Color.FromArgb(43, 66, 68);
        private static readonly Color Paper = Color.FromArgb(221, 241, 234);
        private static readonly Color Muted = Color.FromArgb(147, 178, 174);
        private static readonly Color Mint = Color.FromArgb(111, 246, 184);
        private static readonly Color Blue = Color.FromArgb(117, 216, 236);
        private static readonly Color Amber = Color.FromArgb(255, 201, 115);
        private static readonly Color[] RainShades = { Color.FromArgb(30, 86, 68), Color.FromArgb(45, 138, 102), Mint, Paper };
        private static readonly string[] Titles = { "SYS.LOG", "HEX VIEW", "SIGNAL", "PROCESS TABLE", "MATRIX", "DATA STREAM" };
        private static readonly string[] Commands = { "tail -f", "hexdump", "scope", "top", "cmatrix", "sample" };
        private static readonly Color[] Accents = { Mint, Amber, Blue, Mint, Mint, Amber };
        private static readonly string[] Services = { "renderer", "frameclock", "cache", "router", "telemetry", "dispatch", "sync", "indexer" };
        private static readonly string[] Events = { "frame batch committed", "buffer pages recycled", "channel heartbeat acknowledged",
            "paint queue synchronized", "sample window aligned", "worker returned to idle", "display frame queued", "sequence verified" };
        private static readonly string[] Processes = { "render.host", "stream.pipe", "data.cache", "event.loop", "frame.sync", "display.out", "trace.buffer" };
        private const string Glyphs = "0123456789ABCDEFXYZ+-*<>=:./";

        private readonly Rectangle workArea;
        private readonly int display;
        private readonly StringFormat textFormat = new StringFormat(StringFormat.GenericTypographic) {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
            Trimming = StringTrimming.None
        };
        private readonly List<Pane> panes = new List<Pane>();
        private Bitmap background;
        private Size lastSize;
        private int lastScene = -1;
        private Rectangle content;
        private Font hero, body, label, strong;
        private int lineHeight, padding, paneHeader, footerHeight;

        internal TerminalScene(Rectangle workArea, int display)
        {
            this.workArea = workArea;
            this.display = display;
        }

        internal void Draw(Graphics graphics, Size size, int scene, double time, bool paused)
        {
            if (background == null || lastSize != size || lastScene != scene) BuildScene(size, scene);
            graphics.DrawImageUnscaled(background, 0, 0);
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            foreach (Pane pane in panes)
            {
                if (pane.Body.Width < 1 || pane.Body.Height < 1) continue;
                var state = graphics.Save();
                graphics.SetClip(pane.Body);
                switch (pane.Kind)
                {
                    case 0: DrawLog(graphics, pane.Body, time); break;
                    case 1: DrawHex(graphics, pane.Body, time); break;
                    case 2: DrawSignal(graphics, pane.Body, time); break;
                    case 3: DrawProcesses(graphics, pane.Body, time); break;
                    case 4: DrawRain(graphics, pane.Body, time); break;
                    case 5: DrawStream(graphics, pane.Body, time); break;
                }
                graphics.Restore(state);
            }

            if (content.Width > 0)
            {
                int footer = content.Bottom - footerHeight + padding / 2;
                Text(graphics, paused ? "PAUSED  /  SIMULATED DATA" : "RUNNING  /  SIMULATED DATA", label,
                    paused ? Amber : Mint, content.Left, footer, content.Width);
                if (content.Width > 440)
                    Right(graphics, "DISPLAY " + display.ToString("D2") + "  /  " + TimeSpan.FromSeconds(time).ToString(@"hh\:mm\:ss"),
                        label, Muted, content.Right, footer, content.Width);
            }
        }

        private void BuildScene(Size size, int scene)
        {
            if (background != null) background.Dispose();
            if (lastSize != size) SetFonts(size);
            lastSize = size;
            lastScene = scene;
            background = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
            panes.Clear();

            using (var graphics = Graphics.FromImage(background))
            {
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                graphics.Clear(Ink);
                var area = Rectangle.Intersect(workArea, new Rectangle(Point.Empty, size));
                int lane = Math.Min(250, (int)(area.Width * 0.145));
                var field = Rectangle.FromLTRB(area.Left + lane, area.Top, area.Right, area.Bottom);
                DrawBackdrop(graphics, field);
                content = Rectangle.FromLTRB(field.Left + padding * 2, field.Top + padding,
                    field.Right - padding * 2, field.Bottom - padding);
                if (content.Width < 72 || content.Height < 112) return;

                int titleHeight = (int)(hero.Size * 1.28f);
                Text(graphics, "DESKTOP TERMINAL", strong, Mint, content.Left, content.Top + padding / 2, content.Width);
                Text(graphics, "HOLLYWOOD", hero, Paper, content.Left, content.Top + paneHeader, content.Width);
                int top = content.Top + paneHeader + titleHeight + padding;
                int bottom = content.Bottom - footerHeight - padding / 2;
                if (bottom - top < 80) return;
                var grid = Rectangle.FromLTRB(content.Left, top, content.Right, bottom);
                Layout(grid, scene);

                foreach (Pane pane in panes) DrawPanel(graphics, pane);
                using (var pen = new Pen(Line))
                    graphics.DrawLine(pen, content.Left, content.Bottom - footerHeight, content.Right - 1, content.Bottom - footerHeight);
            }
        }

        private void SetFonts(Size size)
        {
            ReleaseFonts();
            float scale = Math.Max(0.62f, Math.Min(1.85f, Math.Min(size.Width / 1510f, size.Height / 880f)));
            hero = new Font("Consolas", 60 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            body = new Font("Consolas", 14 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            label = new Font("Consolas", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            strong = new Font("Consolas", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            padding = Math.Max(9, (int)(15 * scale));
            lineHeight = Math.Max(11, (int)(22 * scale));
            paneHeader = Math.Max(22, (int)(32 * scale));
            footerHeight = Math.Max(18, (int)(28 * scale));
        }

        private void DrawBackdrop(Graphics graphics, Rectangle field)
        {
            if (field.Width < 1 || field.Height < 1) return;
            using (var gradient = new LinearGradientBrush(field, Ink, Color.FromArgb(13, 30, 32), 0f))
            using (var dot = new SolidBrush(Color.FromArgb(39, 69, 70)))
            {
                graphics.FillRectangle(gradient, field);
                int step = padding * 2;
                for (int y = field.Top + padding; y < field.Bottom; y += step)
                    for (int x = field.Left + padding; x < field.Right; x += step)
                        graphics.FillRectangle(dot, x, y, 1, 1);
            }
        }

        private void Layout(Rectangle grid, int scene)
        {
            int gap = padding;
            int leftTop = scene % 2 == 0 ? 0 : 1;
            int middleTop = scene % 3 == 1 ? 3 : 2;
            if (grid.Width >= 680 && grid.Width * 1.05 > grid.Height)
            {
                int usable = grid.Width - gap * 2;
                int left = (int)(usable * (scene % 3 == 0 ? 0.45 : 0.40));
                int center = (int)(usable * 0.30);
                SplitColumn(new Rectangle(grid.Left, grid.Top, left, grid.Height), leftTop, 1 - leftTop, 0.58);
                SplitColumn(new Rectangle(grid.Left + left + gap, grid.Top, center, grid.Height), middleTop, 5 - middleTop, 0.48);
                SplitColumn(Rectangle.FromLTRB(grid.Left + left + center + gap * 2, grid.Top, grid.Right, grid.Bottom), 4, 5, 0.73);
            }
            else if (grid.Width >= 332 && grid.Height >= 330)
            {
                int half = (grid.Width - gap) / 2;
                int first = (grid.Height - gap * 2) / 3;
                AddPair(grid, half, grid.Top, first, leftTop, middleTop);
                AddPair(grid, half, grid.Top + first + gap, first, 1 - leftTop, 4);
                int last = grid.Top + (first + gap) * 2;
                AddPair(grid, half, last, grid.Bottom - last, 5 - middleTop, 5);
            }
            else
            {
                SplitColumn(grid, leftTop, 4, 0.56);
            }
        }

        private void AddPair(Rectangle grid, int half, int y, int height, int left, int right)
        {
            AddPane(new Rectangle(grid.Left, y, half, height), left);
            int x = grid.Left + half + padding;
            AddPane(new Rectangle(x, y, grid.Right - x, height), right);
        }

        private void SplitColumn(Rectangle column, int top, int bottom, double split)
        {
            int first = (int)((column.Height - padding) * split);
            AddPane(new Rectangle(column.Left, column.Top, column.Width, first), top);
            int y = column.Top + first + padding;
            AddPane(new Rectangle(column.Left, y, column.Width, column.Bottom - y), bottom);
        }

        private void AddPane(Rectangle bounds, int kind)
        {
            if (bounds.Width <= 2 || bounds.Height <= 2) return;
            panes.Add(new Pane { Bounds = bounds, Kind = kind,
                Body = Rectangle.FromLTRB(bounds.Left + padding, bounds.Top + paneHeader + padding / 2,
                    bounds.Right - padding, bounds.Bottom - padding / 2) });
        }

        private void DrawPanel(Graphics graphics, Pane pane)
        {
            Rectangle rect = pane.Bounds;
            using (var surface = new SolidBrush(PanelInk))
            using (var trim = new SolidBrush(Accents[pane.Kind]))
            using (var border = new Pen(Line))
            {
                graphics.FillRectangle(surface, rect);
                graphics.DrawRectangle(border, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
                if (rect.Height <= paneHeader) return;
                int mark = Math.Max(3, padding / 3);
                graphics.FillRectangle(trim, rect.Left + padding, rect.Top + (paneHeader - mark) / 2, mark, mark);
                graphics.DrawLine(border, rect.Left, rect.Top + paneHeader, rect.Right - 1, rect.Top + paneHeader);
                int left = rect.Left + padding + mark * 2;
                int y = rect.Top + (int)((paneHeader - strong.GetHeight(graphics)) / 2);
                Text(graphics, Titles[pane.Kind], strong, Paper, left, y, rect.Right - left - padding);
                if (rect.Width >= 175)
                    Right(graphics, Commands[pane.Kind], label, Muted, rect.Right - padding, y + 1, rect.Width / 2);
            }
        }

        private void DrawLog(Graphics graphics, Rectangle rect, double time)
        {
            int frame = (int)(time * 2.4) + 31;
            int count = Math.Min(18, rect.Height / lineHeight);
            int marker = (int)(body.Size * 6);
            if (rect.Width < 195) marker = 0;
            for (int row = 0; row < count; row++)
            {
                int line = frame - count + row + 1;
                int y = rect.Top + row * lineHeight;
                uint value = Mix(line);
                var color = line % 11 == 0 ? Amber : row == count - 1 ? Mint : Muted;
                if (marker > 0)
                    Text(graphics, TimeSpan.FromSeconds(line).ToString(@"hh\:mm\:ss"), body, color, rect.Left, y, marker);
                string message = String.Format(CultureInfo.InvariantCulture, "{0,-10} {1}",
                    Services[value % Services.Length], Events[(value >> 9) % Events.Length]);
                Text(graphics, message, body, color, rect.Left + marker, y, rect.Width - marker);
            }
        }

        private void DrawHex(Graphics graphics, Rectangle rect, double time)
        {
            int start = (int)(time * 1.7) * 16 + 0x1400;
            int count = Math.Min(14, rect.Height / lineHeight);
            float cell = graphics.MeasureString("0000000000", body, int.MaxValue, textFormat).Width / 10;
            int characters = (int)(rect.Width / Math.Max(1, cell));
            int maxBytes = Math.Max(0, Math.Min(16, (characters - 8) / 3));
            int asciiLength = Math.Min(16, characters - 10 - maxBytes * 3);
            for (int row = 0; row < count; row++)
            {
                int offset = start + row * 16;
                var text = new StringBuilder();
                text.Append(offset.ToString("X6")).Append("  ");
                for (int column = 0; column < maxBytes; column++)
                    text.Append((Mix(offset + column) & 0xFF).ToString("X2")).Append(' ');
                if (maxBytes == 16 && asciiLength > 0)
                {
                    text.Append("  ");
                    for (int column = 0; column < asciiLength; column++) text.Append(Glyphs[(int)(Mix(offset + column) % Glyphs.Length)]);
                }
                Text(graphics, text.ToString(), body, row % 4 == 0 ? Amber : Muted, rect.Left, rect.Top + row * lineHeight, rect.Width);
            }
        }

        private void DrawSignal(Graphics graphics, Rectangle rect, double time)
        {
            int level = (int)(55 + 17 * Math.Sin(time * 0.6));
            Text(graphics, "CH A   " + level.ToString("D2") + " mV", strong, Blue, rect.Left, rect.Top, rect.Width);
            int top = rect.Top + lineHeight * 2;
            int height = rect.Bottom - top - lineHeight;
            if (height < 18 || rect.Width < 6) return;
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var grid = new Pen(Color.FromArgb(37, 63, 69)))
            using (var trace = new Pen(Blue, 1.6f))
            using (var echo = new Pen(Color.FromArgb(70, 131, 143)))
            {
                for (int row = 0; row <= 4; row++)
                    graphics.DrawLine(grid, rect.Left, top + height * row / 4, rect.Right, top + height * row / 4);
                for (int col = 0; col <= 10; col++)
                    graphics.DrawLine(grid, rect.Left + rect.Width * col / 10, top, rect.Left + rect.Width * col / 10, top + height);
                var primary = new PointF[84];
                var secondary = new PointF[primary.Length];
                for (int i = 0; i < primary.Length; i++)
                {
                    float x = i / (primary.Length - 1f);
                    double phase = x * 24 - time * 1.7;
                    double carrier = Math.Sin(phase) * (0.13 + 0.18 * Math.Pow(Math.Sin(phase * 0.39), 2));
                    primary[i] = new PointF(rect.Left + x * rect.Width, top + (float)(0.44 + carrier + Math.Sin(phase * 3.1) * 0.075) * height);
                    secondary[i] = new PointF(primary[i].X, top + (float)(0.72 + Math.Sin(phase * 0.73 + 1) * 0.09) * height);
                }
                graphics.DrawLines(echo, secondary);
                graphics.DrawLines(trace, primary);
            }
            graphics.Restore(state);
            Text(graphics, "0 ms", label, Muted, rect.Left, top + height + 2, rect.Width);
            Right(graphics, "100 ms", label, Muted, rect.Right, top + height + 2, rect.Width);
        }

        private void DrawProcesses(Graphics graphics, Rectangle rect, double time)
        {
            Text(graphics, "PID   PROCESS", strong, Muted, rect.Left, rect.Top, rect.Width);
            bool showLoad = rect.Width > body.Size * 14;
            if (showLoad) Right(graphics, "CPU", strong, Muted, rect.Right, rect.Top, rect.Width / 3);
            int count = Math.Min(Processes.Length, Math.Max(0, rect.Height / lineHeight - 1));
            for (int row = 0; row < count; row++)
            {
                int y = rect.Top + (row + 1) * lineHeight;
                int cpu = (int)((1 + Math.Sin(time * 0.72 + row * 1.3)) * (13 - row));
                string process = (408 + row * 136).ToString("D4") + "  " + Processes[row];
                Text(graphics, process, body, row == 0 ? Mint : Paper, rect.Left, y, rect.Width);
                if (showLoad) Right(graphics, cpu + "%", body, row == 0 ? Mint : Muted, rect.Right, y, rect.Width / 3);
            }
        }

        private void DrawRain(Graphics graphics, Rectangle rect, double time)
        {
            float cell = graphics.MeasureString("0000000000", body, int.MaxValue, textFormat).Width / 10;
            int columns = Math.Min(72, (int)(rect.Width / Math.Max(1, cell)));
            int rows = Math.Min(60, rect.Height / lineHeight);
            if (columns == 0 || rows == 0) return;
            var bands = new char[RainShades.Length][];
            for (int band = 0; band < bands.Length; band++) bands[band] = new char[columns];
            var heads = new int[columns];
            var tails = new int[columns];
            for (int col = 0; col < columns; col++)
            {
                uint random = Mix(col + 403 * display);
                heads[col] = ((int)(time * (2 + random % 7)) + (int)(random % 2017)) % (rows + 14);
                tails[col] = 3 + (int)(random % 11);
            }
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int distance = heads[col] - row;
                    int shade = distance < 0 || distance > tails[col] ? -1 : distance == 0 ? 3 : distance < 3 ? 2 : distance < 6 ? 1 : 0;
                    for (int band = 0; band < bands.Length; band++) bands[band][col] = ' ';
                    if (shade >= 0) bands[shade][col] = Glyphs[(int)(Mix(row * 397 + col + (int)(time * 5)) % Glyphs.Length)];
                }
                for (int band = 0; band < bands.Length; band++)
                    Text(graphics, new string(bands[band]), body, RainShades[band], rect.Left, rect.Top + row * lineHeight, rect.Width);
            }
        }

        private void DrawStream(Graphics graphics, Rectangle rect, double time)
        {
            string[] names = { "RX BUFFER", "TX BUFFER", "QUEUE AGE", "SYNC" };
            for (int row = 0; row < names.Length && (row + 1) * lineHeight <= rect.Height; row++)
            {
                int y = rect.Top + row * lineHeight;
                int level = (int)(49 + 38 * Math.Sin(time * (0.31 + row * 0.08) + row));
                int threshold = (int)(body.Size * 18);
                Text(graphics, names[row], label, Muted, rect.Left, y, rect.Width);
                if (rect.Width > body.Size * 10)
                    Right(graphics, level.ToString("D2") + "%", label, Amber, rect.Right, y, rect.Width / 3);
                if (rect.Width < threshold) continue;
                int x = rect.Left + (int)(body.Size * 6.5f);
                int width = rect.Right - x - (int)(body.Size * 3);
                int barY = y + (int)(body.Size / 2);
                using (var track = new SolidBrush(Line))
                using (var fill = new SolidBrush(Amber))
                {
                    graphics.FillRectangle(track, x, barY, width, 2);
                    graphics.FillRectangle(fill, x, barY, width * level / 100, 2);
                }
            }
        }

        private void Text(Graphics graphics, string text, Font font, Color color, int x, int y, int width)
        {
            if (width < 1) return;
            using (var brush = new SolidBrush(color))
                graphics.DrawString(text, font, brush, new RectangleF(x, y, width, font.GetHeight(graphics) + 3), textFormat);
        }

        private void Right(Graphics graphics, string text, Font font, Color color, int right, int y, int maxWidth)
        {
            int width = (int)Math.Ceiling(graphics.MeasureString(text, font, int.MaxValue, textFormat).Width) + 2;
            if (width <= maxWidth) Text(graphics, text, font, color, right - width, y, width);
        }

        private static uint Mix(int value)
        {
            unchecked
            {
                uint n = (uint)value + 0x9E3779B9;
                n = (n ^ (n >> 16)) * 0x85EBCA6B;
                return (n ^ (n >> 13)) * 0xC2B2AE35;
            }
        }

        private void ReleaseFonts()
        {
            if (hero == null) return;
            hero.Dispose();
            body.Dispose();
            label.Dispose();
            strong.Dispose();
        }

        public void Dispose()
        {
            if (background != null) background.Dispose();
            ReleaseFonts();
            textFormat.Dispose();
        }

        private struct Pane
        {
            internal Rectangle Bounds, Body;
            internal int Kind;
        }
    }
}
