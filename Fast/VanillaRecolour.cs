using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastCraft.Fast
{
    /// <summary>Which pixels of a vanilla texture belong to the material.</summary>
    public enum Recolour
    {
        /// <summary>An iron tool: the desaturated pixels are the metal, the brown handle stays wood.</summary>
        Metal,
        /// <summary>An ingot or a lump: every pixel is the material.</summary>
        MetalAll,
        /// <summary>An ore: the coloured pixels are the gems, the grey stone stays.</summary>
        Gem
    }

    /// <summary>
    /// The ruby tier's textures are the game's own textures with their colours moved to dark
    /// red, so the items keep the exact pixels, silhouettes, outlines and shading of the vanilla
    /// art and only the colour changes. Nothing is drawn by hand: the file is decoded, each
    /// pixel is judged (metal, wood or stone), and the result is written back as a PNG.
    /// </summary>
    public static class VanillaRecolour
    {
        // The dark red of ruby, from the outline to the lit edge. Interpolated by brightness,
        // which is what keeps the original shading intact.
        private static readonly double[] Stops = { 0.0, 0.18, 0.38, 0.62, 0.84, 1.0 };

        private static readonly Color[] Ramp =
        {
            Color.FromRgb(0x1B, 0x02, 0x04),
            Color.FromRgb(0x3B, 0x06, 0x0A),
            Color.FromRgb(0x63, 0x0B, 0x11),
            Color.FromRgb(0x8B, 0x13, 0x19),
            Color.FromRgb(0xAD, 0x22, 0x28),
            Color.FromRgb(0xCD, 0x3B, 0x3F)
        };

        private const double RubyHue = 357.0;

        public static bool TryRecolour(byte[] source, Recolour mode, out byte[] result)
        {
            result = null;
            if (source == null || source.Length == 0) return false;

            try
            {
                BitmapSource frame;
                using (var stream = new MemoryStream(source))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) return false;
                    frame = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
                }

                var width = frame.PixelWidth;
                var height = frame.PixelHeight;
                if (width <= 0 || height <= 0) return false;

                var stride = width * 4;
                var pixels = new byte[stride * height];
                frame.CopyPixels(pixels, stride, 0);

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    var alpha = pixels[i + 3];
                    if (alpha == 0) continue;

                    var b = pixels[i] / 255.0;
                    var g = pixels[i + 1] / 255.0;
                    var r = pixels[i + 2] / 255.0;

                    double h;
                    double s;
                    double v;
                    ToHsv(r, g, b, out h, out s, out v);

                    Color colour;

                    if (mode == Recolour.Gem)
                    {
                        // Stone is grey (no saturation) and stays stone; the gem's own
                        // brightness and saturation are kept, only the hue becomes ruby.
                        if (s < 0.18 || v < 0.15) continue;
                        colour = FromHsv(RubyHue, Math.Min(1.0, Math.Max(0.70, s)), Math.Min(1.0, v * 0.96));
                    }
                    else
                    {
                        // A wooden handle is brown, so it carries saturation; the metal does not.
                        if (mode == Recolour.Metal && s > 0.30) continue;
                        colour = Sample(v);
                    }

                    pixels[i] = colour.B;
                    pixels[i + 1] = colour.G;
                    pixels[i + 2] = colour.R;
                }

                var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                bitmap.Freeze();

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = new MemoryStream())
                {
                    encoder.Save(output);
                    result = output.ToArray();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A colour from the dark red ramp, chosen by the original brightness.</summary>
        private static Color Sample(double value)
        {
            value = Math.Min(1.0, Math.Max(0.0, value));

            for (int i = 1; i < Stops.Length; i++)
            {
                if (value > Stops[i]) continue;

                var span = Stops[i] - Stops[i - 1];
                var t = span <= 0 ? 0 : (value - Stops[i - 1]) / span;
                return Mix(Ramp[i - 1], Ramp[i], t);
            }
            return Ramp[Ramp.Length - 1];
        }

        private static Color Mix(Color from, Color to, double t)
        {
            return Color.FromRgb(
                (byte) Math.Round(from.R + (to.R - from.R) * t),
                (byte) Math.Round(from.G + (to.G - from.G) * t),
                (byte) Math.Round(from.B + (to.B - from.B) * t));
        }

        private static void ToHsv(double r, double g, double b, out double h, out double s, out double v)
        {
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;

            v = max;
            s = max <= 0 ? 0 : delta / max;

            if (delta <= 0)
            {
                h = 0;
                return;
            }

            if (max == r) h = 60.0 * (((g - b) / delta) % 6.0);
            else if (max == g) h = 60.0 * ((b - r) / delta + 2.0);
            else h = 60.0 * ((r - g) / delta + 4.0);

            if (h < 0) h += 360.0;
        }

        private static Color FromHsv(double h, double s, double v)
        {
            var c = v * s;
            var x = c * (1 - Math.Abs(((h / 60.0) % 2.0) - 1));
            var m = v - c;

            double r;
            double g;
            double b;

            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromRgb(
                (byte) Math.Round(Math.Min(1.0, r + m) * 255),
                (byte) Math.Round(Math.Min(1.0, g + m) * 255),
                (byte) Math.Round(Math.Min(1.0, b + m) * 255));
        }
    }
}
