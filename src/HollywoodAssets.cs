using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace HollywoodDesktop
{
    internal static class HollywoodAssets
    {
        internal static readonly Lazy<string[]> Sources = new Lazy<string[]>(delegate
        {
            return new[] { "upstream-hollywood.sh", "HollywoodWidget.cs", "TerminalScene.cs", "WindowsData.cs", "DesktopLayer.cs" }
                .Where(name => ReadText(name).Length > 0).ToArray();
        });
        internal static readonly Lazy<string[]> Banners = new Lazy<string[]>(delegate
        {
            return ReadText("figlet.txt").Replace("\r", "").Split('\f').Where(item => item.Trim().Length > 0).ToArray();
        });
        internal static readonly Lazy<RgbImage> Map = new Lazy<RgbImage>(delegate
        {
            using (var stream = Open("map.jpg"))
            using (var image = Image.FromStream(stream)) return RgbImage.FromImage(image, 136, 64);
        });
        internal static readonly Lazy<RgbImage[]> Pictures = new Lazy<RgbImage[]>(ReadWindowsPictures);
        internal static readonly Lazy<VideoPack> Soundwave = new Lazy<VideoPack>(ReadVideo);
        internal static readonly Lazy<BinarySample[]> Binaries = new Lazy<BinarySample[]>(ReadBinaries);
        internal static readonly Lazy<string[]> SystemPaths = new Lazy<string[]>(ReadSystemPaths);
        private static readonly Dictionary<string, string> TextCache = new Dictionary<string, string>();

        internal static string ReadText(string name)
        {
            string text;
            if (TextCache.TryGetValue(name, out text)) return text;
            using (var stream = Open(name))
            using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();
            TextCache.Add(name, text);
            return text;
        }

        private static Stream Open(string name)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hollywood." + name);
            if (stream == null) throw new InvalidDataException("Missing Hollywood resource " + name);
            return stream;
        }

        private static RgbImage[] ReadWindowsPictures()
        {
            var pictures = new List<RgbImage>();
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web");
                foreach (string file in Directory.EnumerateFiles(root, "*.jpg", SearchOption.AllDirectories).Take(18))
                {
                    try { using (var image = Image.FromFile(file)) pictures.Add(RgbImage.FromImage(image, 110, 55)); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (ArgumentException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (pictures.Count == 0) pictures.Add(Map.Value);
            return pictures.ToArray();
        }

        private static VideoPack ReadVideo()
        {
            using (var stream = Open("soundwave.frames.gz"))
            using (var compressed = new GZipStream(stream, CompressionMode.Decompress))
            using (var reader = new BinaryReader(compressed))
            {
                int width = reader.ReadInt32(), height = reader.ReadInt32(), rate = reader.ReadInt32(), frames = reader.ReadInt32();
                if (width < 1 || width > 128 || height < 1 || height > 128 || rate < 1 || frames < 1 || frames > 10000)
                    throw new InvalidDataException("Invalid Hollywood video frame pack.");
                var images = new RgbImage[frames];
                for (int i = 0; i < frames; i++)
                {
                    int length = width * height * 3;
                    byte[] rgb = reader.ReadBytes(length);
                    if (rgb.Length != length) throw new InvalidDataException("Incomplete Hollywood video frame.");
                    images[i] = new RgbImage(width, height, rgb);
                }
                return new VideoPack { Frames = images, Rate = rate };
            }
        }

        private static BinarySample[] ReadBinaries()
        {
            var files = new List<BinarySample>();
            foreach (string name in new[] { "cmd.exe", "kernel32.dll", "netstat.exe", "notepad.exe", "sort.exe", "findstr.exe", "tree.com", "where.exe" })
            {
                string path = Path.Combine(Environment.SystemDirectory, name);
                try
                {
                    using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new BinaryReader(file))
                        files.Add(new BinarySample { Name = name, Bytes = reader.ReadBytes(4096) });
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return files.Where(file => file.Bytes.Length > 0).ToArray();
        }

        private static string[] ReadSystemPaths()
        {
            var paths = new List<string>();
            foreach (string name in new[] { "drivers", "en-US", "wbem", "WindowsPowerShell", "spool", "Tasks", "DriverStore" })
            {
                string root = Path.Combine(Environment.SystemDirectory, name);
                if (!Directory.Exists(root)) continue;
                paths.Add(root);
                try { paths.AddRange(Directory.EnumerateFileSystemEntries(root).Take(32)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return paths.ToArray();
        }
    }

    internal sealed class BinarySample { internal string Name; internal byte[] Bytes; }
    internal sealed class VideoPack { internal RgbImage[] Frames; internal int Rate; }

    internal sealed class RgbImage
    {
        private readonly int width, height;
        private readonly byte[] data;
        private const string Density = " .,:;ox%#@";

        internal RgbImage(int width, int height, byte[] data) { this.width = width; this.height = height; this.data = data; }

        internal static RgbImage FromImage(Image image, int maxWidth, int maxHeight)
        {
            using (var raster = new Bitmap(image, new Size(maxWidth, maxHeight)))
            {
                var bytes = new byte[maxWidth * maxHeight * 3];
                for (int y = 0; y < maxHeight; y++)
                for (int x = 0; x < maxWidth; x++)
                {
                    var color = raster.GetPixel(x, y);
                    int i = (y * maxWidth + x) * 3;
                    bytes[i] = color.R; bytes[i + 1] = color.G; bytes[i + 2] = color.B;
                }
                return new RgbImage(maxWidth, maxHeight, bytes);
            }
        }

        internal void Draw(PaneView view, bool video, bool fill)
        {
            int drawWidth = fill ? view.Width : Math.Min(view.Width, view.Height * width / height);
            int drawHeight = fill ? view.Height : Math.Min(view.Height, Math.Max(1, drawWidth * height / width));
            int marginX = (view.Width - drawWidth) / 2, marginY = (view.Height - drawHeight) / 2;
            for (int y = 0; y < drawHeight; y++)
            for (int x = 0; x < drawWidth; x++)
            {
                int i = ((y * height / drawHeight) * width + x * width / drawWidth) * 3;
                int red = data[i], green = data[i + 1], blue = data[i + 2];
                int value = (red * 299 + green * 587 + blue * 114) / 1000;
                if (video && value < 18) continue;
                char shade = Density[value * (Density.Length - 1) / 255];
                if (shade == ' ' && video) shade = '.';
                if (shade != ' ') view.Put(marginX + x, marginY + y, shade, TerminalGrid.ImageColor(red, green, blue));
            }
        }
    }
}
