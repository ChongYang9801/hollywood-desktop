using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Text;

namespace HollywoodDesktop
{
    internal sealed class TerminalScene : IDisposable
    {
        private readonly Rectangle workArea;
        private readonly HollywoodSession session;
        private readonly Font font = new Font("Consolas", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly StringFormat format = new StringFormat(StringFormat.GenericTypographic)
        { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
        private readonly SolidBrush[] brushes = new SolidBrush[256];
        private readonly Dictionary<string, string> strings = new Dictionary<string, string>();

        internal TerminalScene(Rectangle workArea, int display, HollywoodOptions options, WindowsData data)
        {
            this.workArea = workArea;
            session = new HollywoodSession(options, data, unchecked(Environment.TickCount * 31 + display));
        }

        internal void Draw(Graphics graphics, Size size, int scene, double time, bool paused)
        {
            graphics.Clear(Color.Black);
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle area = Rectangle.Intersect(workArea, new Rectangle(Point.Empty, size));
            if (area.Width < 12 || area.Height < 22) return;
            float cellWidth = graphics.MeasureString("MMMMMMMMMM", font, int.MaxValue, format).Width / 10f;
            const int cellHeight = 17;
            int columns = Math.Max(1, (int)(area.Width / cellWidth));
            int rows = Math.Max(1, area.Height / cellHeight);
            TerminalGrid grid = session.Frame(columns, rows, scene, time, paused);
            DrawGrid(graphics, grid, area.Location, cellWidth, cellHeight);
        }

        internal void DrawGrid(Graphics graphics, TerminalGrid grid, Point origin, float cellWidth, int cellHeight)
        {
            int columns = grid.Columns;
            for (int y = 0; y < grid.Rows; y++)
            {
                int x = 0;
                while (x < columns)
                {
                    int index = y * columns + x;
                    byte background = grid.Background[index];
                    int end = x + 1;
                    while (end < columns && grid.Background[y * columns + end] == background) end++;
                    if (background != 0)
                        graphics.FillRectangle(Brush(background), origin.X + x * cellWidth, origin.Y + y * cellHeight,
                            (end - x) * cellWidth + 0.5f, cellHeight);
                    x = end;
                }
                x = 0;
                while (x < columns)
                {
                    int index = y * columns + x;
                    if (grid.Spans[index] == TerminalGrid.Continuation || (grid.Text[index] == ' ' && grid.Elements[index] == null))
                    { x++; continue; }
                    byte foreground = grid.Foreground[index];
                    float left = origin.X + x * cellWidth;
                    int top = origin.Y + y * cellHeight;
                    if (grid.Text[index] >= 0x80 || grid.Elements[index] != null)
                    {
                        int width = grid.Spans[index] == 2 ? 2 : 1;
                        if (grid.Elements[index] == null && grid.Text[index] >= 0x2500 && grid.Text[index] <= 0x257f)
                            DrawBorder(graphics, grid.Text[index], foreground, left, top, cellWidth, cellHeight);
                        else
                            graphics.DrawString(grid.Elements[index] ?? InternSmall(grid.Text[index].ToString()), font, Brush(foreground),
                                new RectangleF(left, top, width * cellWidth, cellHeight), format);
                        x += width;
                        continue;
                    }
                    int end = x + 1;
                    while (end < columns && grid.Foreground[y * columns + end] == foreground && grid.Text[y * columns + end] < 0x80 &&
                           grid.Elements[y * columns + end] == null && grid.Spans[y * columns + end] == 0) end++;
                    string text = new string(grid.Text, index, end - x).TrimEnd(' ');
                    graphics.DrawString(InternSmall(text), font, Brush(foreground), left, top, format);
                    x = end;
                }
            }
        }

        private void DrawBorder(Graphics graphics, char glyph, byte color, float x, int y, float w, int h)
        {
            int mask = glyph == '─' ? 3 : glyph == '│' ? 12 : TerminalGrid.Junctions.IndexOf(glyph);
            if (mask < 0) return;
            float middleX = (float)Math.Round(x + w / 2);
            int middleY = y + h / 2;
            var brush = Brush(color);
            if ((mask & 1) != 0) graphics.FillRectangle(brush, x, middleY, middleX - x + 1, 1);
            if ((mask & 2) != 0) graphics.FillRectangle(brush, middleX, middleY, x + w - middleX + 0.75f, 1);
            if ((mask & 4) != 0) graphics.FillRectangle(brush, middleX, y, 1, middleY - y + 1);
            if ((mask & 8) != 0) graphics.FillRectangle(brush, middleX, middleY, 1, y + h - middleY);
        }

        private string InternSmall(string text)
        {
            if (text.Length != 1) return text;
            string stored;
            if (!strings.TryGetValue(text, out stored)) strings.Add(text, stored = text);
            return stored;
        }

        private SolidBrush Brush(byte index)
        {
            return brushes[index] ?? (brushes[index] = new SolidBrush(TerminalGrid.ColorAt(index)));
        }

        public void Dispose()
        {
            font.Dispose();
            format.Dispose();
            foreach (var brush in brushes) if (brush != null) brush.Dispose();
        }
    }

    internal sealed class HollywoodSession
    {
        private readonly HollywoodOptions options;
        private readonly WindowsData data;
        private readonly Random random;
        private readonly List<Pane> panes = new List<Pane>();
        private TerminalGrid grid;
        private int currentScene = -1;

        internal HollywoodSession(HollywoodOptions options, WindowsData data, int seed)
        {
            this.options = options;
            this.data = data;
            random = new Random(seed);
        }

        internal TerminalGrid Frame(int columns, int rows, int scene, double time, bool paused)
        {
            if (grid == null || grid.Columns != columns || grid.Rows != rows)
            {
                grid = new TerminalGrid(columns, rows);
                currentScene = -1;
            }
            if (currentScene != scene)
            {
                Layout(Math.Max(1, rows - 1), columns, time);
                currentScene = scene;
            }

            grid.Clear();
            foreach (var pane in panes)
            {
                var view = new PaneView(grid, pane.Area);
                pane.Widget.Draw(view, data, Math.Max(0, time - pane.Created));
            }
            grid.DrawJunctions();
            if (rows > 1)
            {
                var status = new PaneView(grid, new Rectangle(0, rows - 1, columns, 1));
                status.Write(0, 0, new string(' ', columns), 0, 2);
                status.Write(0, 0, paused ? "[hollywood] 1:hollywood* (paused)" : "[hollywood] 1:hollywood*", 0, 2);
                string clock = DateTime.Now.ToString("HH:mm dd-MMM-yy", System.Globalization.CultureInfo.InvariantCulture);
                if (columns >= 46) status.Write(columns - clock.Length - 1, 0, clock, 0, 2);
            }
            return grid;
        }

        private void Layout(int height, int width, double time)
        {
            Pane retained = panes.OrderByDescending(p => p.Area.Bottom).ThenByDescending(p => p.Area.Right).FirstOrDefault();
            var widgets = Enumerable.Range(0, HollywoodWidget.Names.Length).ToArray();
            for (int i = widgets.Length - 1; i > 0; i--)
            {
                int chosen = random.Next(i + 1);
                int value = widgets[i]; widgets[i] = widgets[chosen]; widgets[chosen] = value;
            }
            panes.Clear();
            grid.ClearSeparators();
            var area = new Rectangle(0, 0, width, height);
            if (retained == null) retained = Create(area, widgets[0], time);
            retained.Area = area;
            panes.Add(retained);

            bool vertical = true;
            foreach (int kind in widgets)
            {
                if (panes.Count >= options.Splits) break;
                if (kind == retained.Widget.Kind) continue;
                Pane target = Choose(vertical);
                if (target == null)
                {
                    vertical = !vertical;
                    target = Choose(vertical);
                    if (target == null) break;
                }
                Rectangle a = target.Area;
                Rectangle b;
                if (vertical)
                {
                    int heightA = (a.Height - 1) / 2;
                    int y = a.Y + heightA;
                    grid.Separator(a.Left, y, a.Right, y + 1, 1);
                    b = Rectangle.FromLTRB(a.Left, y + 1, a.Right, a.Bottom);
                    a.Height = heightA;
                }
                else
                {
                    int widthA = (a.Width - 1) / 2;
                    int x = a.X + widthA;
                    grid.Separator(x, a.Top, x + 1, a.Bottom, 2);
                    b = Rectangle.FromLTRB(x + 1, a.Top, a.Right, a.Bottom);
                    a.Width = widthA;
                }
                target.Area = a;
                panes.Add(Create(b, kind, time));
                vertical = !vertical;
            }
        }

        private Pane Choose(bool vertical)
        {
            var candidates = panes.Where(p => vertical ? p.Area.Height >= 7 : p.Area.Width >= 23).ToArray();
            return candidates.Length == 0 ? null : candidates[random.Next(candidates.Length)];
        }

        private Pane Create(Rectangle area, int widget, double time)
        {
            return new Pane { Area = area, Created = time, Widget = new HollywoodWidget(widget, random.Next()) };
        }

        private sealed class Pane
        {
            internal Rectangle Area;
            internal HollywoodWidget Widget;
            internal double Created;
        }
    }

    internal sealed class TerminalGrid
    {
        private static readonly Color[] Basic = { Color.Black, Color.FromArgb(205, 49, 49), Color.FromArgb(13, 188, 121),
            Color.FromArgb(229, 229, 16), Color.FromArgb(36, 114, 200), Color.FromArgb(188, 63, 188), Color.FromArgb(17, 168, 205),
            Color.FromArgb(229, 229, 229), Color.FromArgb(102, 102, 102), Color.FromArgb(241, 76, 76), Color.FromArgb(35, 209, 139),
            Color.FromArgb(245, 245, 67), Color.FromArgb(59, 142, 234), Color.FromArgb(214, 112, 214), Color.FromArgb(41, 184, 219), Color.White };
        internal const string Junctions = " ───│┘└┴│┐┌┬│┤├┼";
        internal const byte Continuation = byte.MaxValue;
        internal readonly int Columns, Rows;
        internal readonly char[] Text;
        internal readonly string[] Elements;
        internal readonly byte[] Spans;
        internal readonly byte[] Foreground, Background;
        private readonly byte[] separators;
        private readonly char[] blank;

        internal TerminalGrid(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
            Text = new char[columns * rows];
            Elements = new string[Text.Length];
            Spans = new byte[Text.Length];
            Foreground = new byte[Text.Length];
            Background = new byte[Text.Length];
            separators = new byte[Text.Length];
            blank = new string(' ', Text.Length).ToCharArray();
        }

        internal void Clear()
        {
            Array.Copy(blank, Text, Text.Length);
            Array.Clear(Elements, 0, Elements.Length);
            Array.Clear(Spans, 0, Spans.Length);
            Array.Clear(Foreground, 0, Foreground.Length);
            Array.Clear(Background, 0, Background.Length);
        }

        internal void Put(int index, char character, string element, int width, byte color, byte background)
        {
            ClearWide(index);
            if (width == 2)
            {
                ClearWide(index + 1);
                Text[index + 1] = ' ';
                Elements[index + 1] = null;
                Foreground[index + 1] = color;
                Background[index + 1] = background;
                Spans[index + 1] = Continuation;
            }
            Text[index] = character;
            Elements[index] = element;
            Foreground[index] = color;
            Background[index] = background;
            Spans[index] = width == 2 ? (byte)2 : (byte)0;
        }

        private void ClearWide(int index)
        {
            if (Spans[index] == 0) return;
            int lead = Spans[index] == Continuation ? index - 1 : index;
            for (int i = lead; i < lead + 2; i++)
            {
                Text[i] = ' ';
                Spans[i] = 0;
                Elements[i] = null;
                Foreground[i] = 0;
                Background[i] = 0;
            }
        }

        internal void ClearSeparators() { Array.Clear(separators, 0, separators.Length); }

        internal void Separator(int left, int top, int right, int bottom, byte direction)
        {
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++) separators[y * Columns + x] = direction;
        }

        internal void DrawJunctions()
        {
            for (int y = 0; y < Rows - 1; y++)
            for (int x = 0; x < Columns; x++)
            {
                int i = y * Columns + x;
                byte direction = separators[i];
                if (direction == 0) continue;
                int junction = direction == 1 ? 3 : 12;
                if (x > 0 && separators[i - 1] == 1) junction |= 1;
                if (x + 1 < Columns && separators[i + 1] == 1) junction |= 2;
                if (y > 0 && separators[i - Columns] == 2) junction |= 4;
                if (separators[i + Columns] == 2) junction |= 8;
                Text[i] = Junctions[junction];
                Foreground[i] = 8;
            }
        }

        internal static Color ColorAt(byte index)
        {
            if (index < 16) return Basic[index];
            if (index >= 232) { int gray = (index - 232) * 10 + 8; return Color.FromArgb(gray, gray, gray); }
            int n = index - 16;
            return Color.FromArgb(Cube(n / 36), Cube(n / 6 % 6), Cube(n % 6));
        }

        private static int Cube(int value) { return value == 0 ? 0 : 55 + value * 40; }

        internal static byte ImageColor(int red, int green, int blue)
        {
            return (byte)(16 + ((red * 5 + 127) / 255) * 36 + ((green * 5 + 127) / 255) * 6 + (blue * 5 + 127) / 255);
        }
    }

    internal struct PaneView
    {
        private readonly TerminalGrid grid;
        private readonly Rectangle area;
        internal int Width { get { return area.Width; } }
        internal int Height { get { return area.Height; } }
        internal PaneView(TerminalGrid grid, Rectangle area) { this.grid = grid; this.area = area; }

        internal void Put(int x, int y, char value, byte color, byte background = 0)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            grid.Put((area.Y + y) * grid.Columns + area.X + x, value, null, 1, color, background);
        }

        internal void Write(int x, int y, string text, byte color = 7, byte background = 0)
        {
            if (y < 0 || y >= Height || x >= Width) return;
            int column = x;
            int row = (area.Y + y) * grid.Columns + area.X;
            for (int i = 0; i < text.Length; )
            {
                if (column >= Width) break;
                char character = text[i];
                int width = TerminalText.Advance(text, i, column);
                int length = TerminalText.ElementLength(text, i);
                if (column + width > Width) break;
                if (width > 0 && character != '\t' && column >= 0)
                {
                    string element = length > 1 ? text.Substring(i, length) : null;
                    if (length == 1 && char.IsSurrogate(character)) character = '\ufffd';
                    grid.Put(row + column, character, element, width, color, background);
                }
                i += length;
                column += width;
            }
        }
    }
}
