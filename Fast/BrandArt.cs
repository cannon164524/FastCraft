using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastCraft.Fast
{
    /// <summary>
    /// The images of the FastCraft look, drawn with the same palette as the launcher: near
    /// black plates, the amber accent, the two speed chevrons of the mark. Everything here
    /// returns PNG bytes, so the resource pack is built from code and can never drift away
    /// from the launcher's colours.
    /// </summary>
    public static class BrandArt
    {
        public static readonly Color Background = Color.FromRgb(0x0A, 0x0D, 0x13);
        public static readonly Color BackgroundDeep = Color.FromRgb(0x07, 0x09, 0x0D);
        public static readonly Color Plate = Color.FromRgb(0x14, 0x1A, 0x22);
        public static readonly Color PlateRaised = Color.FromRgb(0x1F, 0x26, 0x2F);
        public static readonly Color Line = Color.FromRgb(0x27, 0x30, 0x3D);
        public static readonly Color Accent = Color.FromRgb(0xD9, 0xA5, 0x5B);
        public static readonly Color AccentLight = Color.FromRgb(0xFF, 0xD7, 0x9A);
        public static readonly Color AccentDeep = Color.FromRgb(0xD9, 0x8B, 0x3F);
        public static readonly Color Glow = Color.FromRgb(0xFF, 0xC0, 0x77);

        public enum ButtonKind
        {
            Normal,
            Highlighted,
            Disabled
        }

        /// <summary>
        /// The wordmark: the launcher's amber gradient on "FASTCRAFT", a soft halo around the
        /// glyphs, and the two speed chevrons of the mark on the left.
        /// </summary>
        public static byte[] Wordmark(int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // A tenth of the height on each side is the safe area: the title screen draws
                // this texture inside a frame a little smaller than itself, so glyphs must not
                // reach the edge or they lose their last pixels.
                var margin = height * 0.10;
                var chevrons = height * 0.54;
                var chevronsWidth = chevrons * 0.62;
                var gap = height * 0.18;

                DrawChevrons(dc, new Rect(margin, (height - chevrons) / 2, chevronsWidth, chevrons),
                    Accent, chevrons * 0.17);

                var left = margin + chevronsWidth + gap;
                var targetWidth = width - left - margin;
                var targetHeight = height * 0.60;
                if (targetWidth <= 0) return Encode(new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32));

                DrawFittedText(dc, "FASTCRAFT", left, (height - targetHeight) / 2, targetWidth, targetHeight,
                    AccentLight, AccentDeep, targetHeight * 0.16);
            }
            return Render(visual, width, height);
        }

        /// <summary>The "Java Edition" line of the title screen, in FastCraft's voice.</summary>
        public static byte[] EditionLabel(int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                DrawFittedTrackedText(dc, "FASTCRAFT EDITION", 0, 0, width, height, AccentLight, AccentDeep,
                    height * 0.26);
            }
            return Render(visual, width, height);
        }

        /// <summary>A button plate: dark, hairline border, amber on hover, quieter when disabled.</summary>
        public static byte[] Button(int width, int height, ButtonKind kind)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var radius = Math.Max(2, height * 0.16);
                var fill = kind == ButtonKind.Highlighted ? PlateRaised : kind == ButtonKind.Disabled ? Color.FromRgb(0x10, 0x14, 0x19) : Plate;
                var border = kind == ButtonKind.Highlighted ? Accent : kind == ButtonKind.Disabled ? Color.FromRgb(0x1E, 0x25, 0x30) : Line;
                var alpha = kind == ButtonKind.Disabled ? (byte)150 : (byte)235;

                var rect = new Rect(0.5, 0.5, width - 1, height - 1);
                dc.DrawRoundedRectangle(Fill(fill, alpha), new Pen(Fill(border, alpha), Math.Max(1, height * 0.05)), rect, radius, radius);

                // A thin lighter strip along the top edge: the same lifted edge the launcher's
                // buttons get, so a button reads as a plate instead of a flat rectangle.
                var top = new Rect(1, 1, width - 2, Math.Max(1, height * 0.12));
                dc.DrawRoundedRectangle(Fill(Color.FromArgb(18, 255, 255, 255), 255), null, top, radius * 0.6, radius * 0.6);
            }
            return Render(visual, width, height);
        }

        /// <summary>A tiled menu backdrop: near black, with a faint grid so it is not a flat void.</summary>
        public static byte[] Tile(int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Fill(Background, 255), null, new Rect(0, 0, width, height));
                var line = new Pen(Fill(Color.FromRgb(0x0D, 0x12, 0x1C), 255), Math.Max(1, width * 0.03));
                dc.DrawLine(line, new Point(0, 0), new Point(width, 0));
                dc.DrawLine(line, new Point(0, 0), new Point(0, height));
            }
            return Render(visual, width, height);
        }

        /// <summary>
        /// One face of the title screen panorama: a dark gradient with a little warm light,
        /// which is what the launcher's background does behind its pages.
        /// </summary>
        public static byte[] PanoramaFace(int size, bool warm)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var backdrop = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, size),
                    MappingMode = BrushMappingMode.Absolute
                };
                backdrop.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x0F, 0x16), 0));
                backdrop.GradientStops.Add(new GradientStop(BackgroundDeep, 1));
                dc.DrawRectangle(backdrop, null, new Rect(0, 0, size, size));

                if (warm)
                {
                    var glow = new RadialGradientBrush
                    {
                        Center = new Point(size * 0.5, size * 0.86),
                        GradientOrigin = new Point(size * 0.5, size * 0.86),
                        RadiusX = size * 0.62,
                        RadiusY = size * 0.62,
                        MappingMode = BrushMappingMode.Absolute
                    };
                    glow.GradientStops.Add(new GradientStop(Color.FromArgb(40, Glow.R, Glow.G, Glow.B), 0));
                    glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, Glow.R, Glow.G, Glow.B), 1));
                    dc.DrawRectangle(glow, null, new Rect(0, 0, size, size));
                }
                else
                {
                    var glow = new RadialGradientBrush
                    {
                        Center = new Point(size * 0.3, size * 0.2),
                        GradientOrigin = new Point(size * 0.3, size * 0.2),
                        RadiusX = size * 0.5,
                        RadiusY = size * 0.5,
                        MappingMode = BrushMappingMode.Absolute
                    };
                    var violet = Color.FromRgb(0x7A, 0x5C, 0xFF);
                    glow.GradientStops.Add(new GradientStop(Color.FromArgb(26, violet.R, violet.G, violet.B), 0));
                    glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, violet.R, violet.G, violet.B), 1));
                    dc.DrawRectangle(glow, null, new Rect(0, 0, size, size));
                }
            }
            return Render(visual, size, size);
        }

        /// <summary>The pack thumbnail: the mark, exactly as it looks in the launcher.</summary>
        public static byte[] PackIcon(int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var tile = new Rect(size * 0.04, size * 0.04, size * 0.92, size * 0.92);
                dc.DrawRoundedRectangle(Fill(Background, 255), null, new Rect(0, 0, size, size), 0, 0);
                dc.DrawRoundedRectangle(Fill(Plate, 255), new Pen(Fill(Line, 255), Math.Max(1, size * 0.012)),
                    tile, size * 0.2, size * 0.2);

                var chevronHeight = size * 0.46;
                var chevronWidth = chevronHeight * 0.62;
                DrawChevrons(dc, new Rect(size * 0.32, (size - chevronHeight) / 2, chevronWidth, chevronHeight),
                    Accent, chevronHeight * 0.18);
            }
            return Render(visual, size, size);
        }

        // ---------------------------------------------------------------- amber

        private static readonly Color AmberDark = Color.FromRgb(0xB8, 0x77, 0x2E);
        private static readonly Color AmberMid = Color.FromRgb(0xE0, 0x9B, 0x3E);
        private static readonly Color Stick = Color.FromRgb(0x7A, 0x5A, 0x3A);

        /// <summary>The amber ore: grey stone with amber blobs in it, not copper's green.</summary>
        public static byte[] AmberOre(int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Fill(Color.FromRgb(0x6E, 0x6E, 0x6E), 255), null, new Rect(0, 0, width, height));

                var random = new Random(26);
                var stone = Fill(Color.FromRgb(0x62, 0x62, 0x62), 255);
                var lighter = Fill(Color.FromRgb(0x7B, 0x7B, 0x7B), 255);
                var grain = Math.Max(1, width / 8);
                for (int i = 0; i < 40; i++)
                {
                    dc.DrawRectangle(random.Next(2) == 0 ? stone : lighter, null,
                        new Rect(random.Next(width), random.Next(height), grain, grain));
                }

                var dark = Fill(AmberDark, 255);
                var amber = Fill(Accent, 255);
                var light = Fill(AccentLight, 255);
                for (int i = 0; i < 7; i++)
                {
                    var size = Math.Max(2, width / 5);
                    var x = random.Next(Math.Max(1, width - size));
                    var y = random.Next(Math.Max(1, height - size));
                    dc.DrawRectangle(dark, null, new Rect(x, y, size, size));
                    dc.DrawRectangle(amber, null, new Rect(x + 1, y + 1, Math.Max(1, size - 2), Math.Max(1, size - 2)));
                    dc.DrawRectangle(light, null, new Rect(x + 1, y + 1, Math.Max(1, size / 3), Math.Max(1, size / 3)));
                }
            }
            return Render(visual, width, height);
        }

        /// <summary>Raw amber: a few rough amber lumps, no stone around them.</summary>
        public static byte[] RawAmber(int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var random = new Random(11);
                for (int i = 0; i < 5; i++)
                {
                    var size = Math.Max(3, width / 4);
                    var x = width / 10 + random.Next(Math.Max(1, width / 2));
                    var y = height / 10 + random.Next(Math.Max(1, height / 2));
                    dc.DrawRoundedRectangle(Fill(AmberDark, 255), null, new Rect(x, y, size, size), size / 3, size / 3);
                    dc.DrawRoundedRectangle(Fill(AmberMid, 255), null,
                        new Rect(x + 1, y + 1, Math.Max(1, size - 2), Math.Max(1, size - 2)), size / 4, size / 4);
                    dc.DrawRoundedRectangle(Fill(AccentLight, 255), null,
                        new Rect(x + 2, y + 2, Math.Max(1, size / 3), Math.Max(1, size / 3)), size / 6, size / 6);
                }
            }
            return Render(visual, width, height);
        }

        /// <summary>An amber ingot: one bar with a lit top edge.</summary>
        public static byte[] AmberIngot(int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bar = new Rect(width * 0.13, height * 0.31, width * 0.74, height * 0.38);
                dc.DrawRoundedRectangle(Fill(AmberDark, 255), null, bar, height * 0.12, height * 0.12);
                dc.DrawRoundedRectangle(Fill(Accent, 255), null,
                    new Rect(width * 0.18, height * 0.35, width * 0.64, height * 0.22), height * 0.09, height * 0.09);
                dc.DrawRectangle(Fill(AccentLight, 255), null,
                    new Rect(width * 0.24, height * 0.38, width * 0.5, Math.Max(1, height * 0.06)));
            }
            return Render(visual, width, height);
        }

        /// <summary>One amber tool: a wooden handle, and a head whose shape says which tool it is.</summary>
        public static byte[] AmberTool(int width, int height, string kind)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var handle = new Pen(Fill(Stick, 255), Math.Max(1, width * 0.13))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                dc.DrawLine(handle, new Point(width * 0.26, height * 0.88), new Point(width * 0.66, height * 0.4));

                var dark = Fill(AmberDark, 255);
                var amber = Fill(Accent, 255);
                var light = Fill(AccentLight, 255);

                if (kind == "sword")
                {
                    dc.DrawLine(new Pen(dark, Math.Max(2, width * 0.18)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                        new Point(width * 0.3, height * 0.82), new Point(width * 0.82, height * 0.18));
                    dc.DrawLine(new Pen(light, Math.Max(1, width * 0.06)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                        new Point(width * 0.34, height * 0.76), new Point(width * 0.8, height * 0.22));
                }
                else if (kind == "pickaxe")
                {
                    dc.DrawLine(new Pen(dark, Math.Max(2, width * 0.16)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                        new Point(width * 0.24, height * 0.34), new Point(width * 0.8, height * 0.28));
                    dc.DrawLine(new Pen(amber, Math.Max(1, width * 0.1)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                        new Point(width * 0.3, height * 0.34), new Point(width * 0.76, height * 0.3));
                }
                else if (kind == "axe")
                {
                    dc.DrawRoundedRectangle(dark, null, new Rect(width * 0.5, height * 0.12, width * 0.38, height * 0.32), width * 0.08, width * 0.08);
                    dc.DrawRoundedRectangle(amber, null, new Rect(width * 0.54, height * 0.16, width * 0.28, height * 0.22), width * 0.06, width * 0.06);
                    dc.DrawRectangle(light, null, new Rect(width * 0.58, height * 0.18, width * 0.12, Math.Max(1, height * 0.05)));
                }
                else if (kind == "shovel")
                {
                    dc.DrawRoundedRectangle(dark, null, new Rect(width * 0.58, height * 0.1, width * 0.3, height * 0.26), width * 0.1, width * 0.1);
                    dc.DrawRoundedRectangle(amber, null, new Rect(width * 0.61, height * 0.13, width * 0.24, height * 0.18), width * 0.08, width * 0.08);
                }
                else
                {
                    dc.DrawLine(new Pen(dark, Math.Max(2, width * 0.14)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                        new Point(width * 0.6, height * 0.3), new Point(width * 0.84, height * 0.28));
                    dc.DrawLine(new Pen(dark, Math.Max(2, width * 0.14)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                        new Point(width * 0.8, height * 0.3), new Point(width * 0.82, height * 0.12));
                    dc.DrawLine(new Pen(amber, Math.Max(1, width * 0.06)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                        new Point(width * 0.63, height * 0.3), new Point(width * 0.8, height * 0.28));
                }
            }
            return Render(visual, width, height);
        }

        // ---------------------------------------------------------------- drawing helpers

        private static void DrawChevrons(DrawingContext dc, Rect box, Color color, double thickness)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var middle = box.Top + box.Height / 2;
                context.BeginFigure(new Point(box.Left, box.Top), false, false);
                context.LineTo(new Point(box.Left + box.Width, middle), true, false);
                context.LineTo(new Point(box.Left, box.Bottom), true, false);
            }
            geometry.Freeze();

            var pen = new Pen(Fill(color, 255), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            // One wider, faint pass under the crisp one turns the line into a lit edge.
            var glowPen = new Pen(Fill(Color.FromArgb(70, color.R, color.G, color.B), 255), thickness * 2.2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            dc.DrawGeometry(null, glowPen, geometry);
            dc.DrawGeometry(null, pen, geometry);
        }

        private static void DrawFittedText(DrawingContext dc, string text, double left, double top, double width,
            double height, Color from, Color to, double glow)
        {
            var geometry = BuildTextGeometry(text);
            var bounds = geometry.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            var scale = Math.Min(width / bounds.Width, height / bounds.Height);
            var x = left + (width - bounds.Width * scale) / 2 - bounds.X * scale;
            var y = top + (height - bounds.Height * scale) / 2 - bounds.Y * scale;

            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
            if (glow > 0)
            {
                var glowPen = new Pen(Fill(Color.FromArgb(80, Glow.R, Glow.G, Glow.B), 255), glow / scale)
                {
                    LineJoin = PenLineJoin.Round
                };
                dc.DrawGeometry(null, glowPen, geometry);
            }
            dc.DrawGeometry(new LinearGradientBrush
            {
                StartPoint = new Point(0, top),
                EndPoint = new Point(0, top + height),
                MappingMode = BrushMappingMode.Absolute,
                GradientStops =
                {
                    new GradientStop(from, 0),
                    new GradientStop(to, 1)
                }
            }, null, geometry);
            dc.Pop();
        }

        /// <summary>Draws text letter by letter, so the label gets the spacing the launcher uses.</summary>
        private static void DrawFittedTrackedText(DrawingContext dc, string text, double left, double top,
            double width, double height, Color from, Color to, double tracking)
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold,
                FontStretches.Normal);

            var glyphs = new Geometry[text.Length];
            var advances = new double[text.Length];
            double total = 0;
            double glyphHeight = 0;

            for (int i = 0; i < text.Length; i++)
            {
                var formatted = new FormattedText(text[i].ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 100, Brushes.White, 96);
                glyphs[i] = formatted.BuildGeometry(new Point(0, 0));
                advances[i] = formatted.WidthIncludingTrailingWhitespace;
                total += advances[i];
                if (i < text.Length - 1) total += tracking;

                var bounds = glyphs[i].Bounds;
                if (!bounds.IsEmpty && bounds.Height > glyphHeight) glyphHeight = bounds.Height;
            }

            if (total <= 0 || glyphHeight <= 0) return;

            var scale = Math.Min(width / total, height / glyphHeight);
            var cursor = left + (width - total * scale) / 2;
            var topEdge = top + (height - glyphHeight * scale) / 2;

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, top),
                EndPoint = new Point(0, top + height),
                MappingMode = BrushMappingMode.Absolute
            };
            brush.GradientStops.Add(new GradientStop(from, 0));
            brush.GradientStops.Add(new GradientStop(to, 1));

            for (int i = 0; i < glyphs.Length; i++)
            {
                var geometry = glyphs[i];
                var bounds = geometry.Bounds;

                if (!bounds.IsEmpty)
                {
                    dc.PushTransform(new MatrixTransform(scale, 0, 0, scale,
                        cursor - bounds.X * scale, topEdge - bounds.Y * scale));
                    dc.DrawGeometry(null,
                        new Pen(Fill(Color.FromArgb(70, Glow.R, Glow.G, Glow.B), 255), 2 / scale), geometry);
                    dc.DrawGeometry(brush, null, geometry);
                    dc.Pop();
                }

                cursor += (advances[i] + tracking) * scale;
            }
        }

        private static Geometry BuildTextGeometry(string text)
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold,
                FontStretches.Normal);
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 100, Brushes.White, 96);
            return formatted.BuildGeometry(new Point(0, 0));
        }

        private static SolidColorBrush Fill(Color color, byte alpha)
        {
            var brush = new SolidColorBrush(color);
            if (alpha != 255)
            {
                brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            }
            brush.Freeze();
            return brush;
        }

        private static byte[] Render(DrawingVisual visual, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return Encode(bitmap);
        }

        private static byte[] Encode(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }
    }
}
