// Draws the app's artwork from one design, so the icon, installer pictures and GitHub preview always match:
//   assets\mdu.ico (16-256 px), assets\mdu-256.png, assets\mdu-64.png, assets\logo.svg,
//   installer\wizard.bmp (164x314), installer\header.bmp (150x57), docs\social-preview.png (1280x640).
// The mark: a blue tile with a white chip (the hardware) carrying an up arrow (the update). Small sizes drop the pins.
// Run tools\art.cmd from anywhere; it builds this with the C# compiler inside Windows and runs it in the repo root.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

static class ArtGen
{
    const string Top = "#2F80FF", Bottom = "#1458D9", ArrowColor = "#1A66E8";
    const string Font = "Segoe UI Variable Display, Segoe UI";

    static Brush Solid(string hex) { Brush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    static Brush TileBrush()
    {
        LinearGradientBrush g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Top), (Color)ColorConverter.ConvertFromString(Bottom), 90);
        g.Freeze();
        return g;
    }

    // The chip: white body, pins on all four sides, blue up arrow. b = body size, (cx, cy) = center.
    static void DrawChip(DrawingContext dc, double cx, double cy, double b, int pins, Brush body, Brush arrow)
    {
        if (pins > 0)
        {
            double len = b * 0.2, wid = b * (pins == 2 ? 0.16 : 0.11), step = b * (pins == 2 ? 0.36 : 0.3);
            for (int i = 0; i < pins; i++)
            {
                double off = (i - (pins - 1) / 2.0) * step;
                double r = wid * 0.3;
                dc.DrawRoundedRectangle(body, null, new Rect(cx - b / 2 - len, cy + off - wid / 2, len + r, wid), r, r);   // left
                dc.DrawRoundedRectangle(body, null, new Rect(cx + b / 2 - r, cy + off - wid / 2, len + r, wid), r, r);     // right
                dc.DrawRoundedRectangle(body, null, new Rect(cx + off - wid / 2, cy - b / 2 - len, wid, len + r), r, r);   // top
                dc.DrawRoundedRectangle(body, null, new Rect(cx + off - wid / 2, cy + b / 2 - r, wid, len + r), r, r);     // bottom
            }
        }
        dc.DrawRoundedRectangle(body, null, new Rect(cx - b / 2, cy - b / 2, b, b), b * 0.16, b * 0.16);
        Geometry g = Arrow(cx, cy, b);
        Pen soft = new Pen(arrow, b * 0.035);
        soft.LineJoin = PenLineJoin.Round;
        dc.DrawGeometry(arrow, soft, g);
    }

    static double[] ArrowPoints(double cx, double cy, double b)
    {
        return new double[] {
            cx, cy - 0.31 * b,
            cx + 0.27 * b, cy - 0.03 * b,
            cx + 0.095 * b, cy - 0.03 * b,
            cx + 0.095 * b, cy + 0.31 * b,
            cx - 0.095 * b, cy + 0.31 * b,
            cx - 0.095 * b, cy - 0.03 * b,
            cx - 0.27 * b, cy - 0.03 * b };
    }

    static Geometry Arrow(double cx, double cy, double b)
    {
        double[] p = ArrowPoints(cx, cy, b);
        StreamGeometry g = new StreamGeometry();
        using (StreamGeometryContext c = g.Open())
        {
            c.BeginFigure(new Point(p[0], p[1]), true, true);
            for (int i = 2; i < p.Length; i += 2) c.LineTo(new Point(p[i], p[i + 1]), true, true);
        }
        g.Freeze();
        return g;
    }

    // The full icon: tile + chip. size = edge in px; the drawing is designed on a 256 grid.
    static void DrawIcon(DrawingContext dc, double x, double y, double size)
    {
        double s = size / 256.0;
        int pins = size >= 32 ? 3 : size >= 20 ? 2 : 0;
        double margin = (size <= 24 ? 6 : 12) * s;
        dc.DrawRoundedRectangle(TileBrush(), null, new Rect(x + margin, y + margin, size - 2 * margin, size - 2 * margin), 56 * s, 56 * s);
        double body = (pins == 3 ? 104 : pins == 2 ? 124 : 150) * s;
        DrawChip(dc, x + size / 2, y + size / 2, body, pins, Brushes.White, Solid(ArrowColor));
    }

    static RenderTargetBitmap Render(int w, int h, Action<DrawingContext> draw)
    {
        DrawingVisual v = new DrawingVisual();
        RenderOptions.SetEdgeMode(v, EdgeMode.Unspecified);
        using (DrawingContext dc = v.RenderOpen()) draw(dc);
        RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        return rtb;
    }

    static FormattedText Text(string s, double size, FontWeight weight, Brush brush, double maxWidth = 0, TextAlignment align = TextAlignment.Left)
    {
        FormattedText t = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(Font), FontStyles.Normal, weight, FontStretches.Normal), size, brush, 1.0);
        if (maxWidth > 0) t.MaxTextWidth = maxWidth;
        t.TextAlignment = align;
        return t;
    }

    static void SavePng(BitmapSource b, string path)
    {
        PngBitmapEncoder e = new PngBitmapEncoder();
        e.Frames.Add(BitmapFrame.Create(b));
        using (FileStream f = File.Create(path)) e.Save(f);
        Console.WriteLine("  " + path);
    }

    // NSIS wants plain 24-bit bitmaps.
    static void SaveBmp24(BitmapSource b, string path)
    {
        BmpBitmapEncoder e = new BmpBitmapEncoder();
        e.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(b, PixelFormats.Bgr24, null, 0)));
        using (FileStream f = File.Create(path)) e.Save(f);
        Console.WriteLine("  " + path);
    }

    static byte[] PngBytes(BitmapSource b)
    {
        PngBitmapEncoder e = new PngBitmapEncoder();
        e.Frames.Add(BitmapFrame.Create(b));
        using (MemoryStream m = new MemoryStream()) { e.Save(m); return m.ToArray(); }
    }

    // A 32-bit icon image the classic way (BITMAPINFOHEADER, bottom-up BGRA, empty AND mask): every Windows tool reads it.
    static byte[] DibBytes(BitmapSource src)
    {
        BitmapSource b = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = b.PixelWidth, h = b.PixelHeight, stride = w * 4;
        byte[] px = new byte[h * stride];
        b.CopyPixels(px, stride, 0);
        int maskStride = ((w + 31) / 32) * 4;
        using (MemoryStream m = new MemoryStream())
        using (BinaryWriter wr = new BinaryWriter(m))
        {
            wr.Write(40); wr.Write(w); wr.Write(h * 2); wr.Write((short)1); wr.Write((short)32);
            wr.Write(0); wr.Write(h * stride + h * maskStride); wr.Write(0); wr.Write(0); wr.Write(0); wr.Write(0);
            for (int row = h - 1; row >= 0; row--) wr.Write(px, row * stride, stride);
            wr.Write(new byte[h * maskStride]);
            return m.ToArray();
        }
    }

    static void SaveIco(string path, int[] sizes)
    {
        List<byte[]> images = new List<byte[]>();
        foreach (int s in sizes)
        {
            int size = s;
            RenderTargetBitmap b = Render(size, size, dc => DrawIcon(dc, 0, 0, size));
            images.Add(size >= 256 ? PngBytes(b) : DibBytes(b));
        }
        using (FileStream f = File.Create(path))
        using (BinaryWriter w = new BinaryWriter(f))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] img in images) w.Write(img);
        }
        Console.WriteLine("  " + path);
    }

    static string N(double d) { return d.ToString("0.##", CultureInfo.InvariantCulture); }

    // The same 256-px icon as SVG, for the README and anyone who needs it larger.
    static void SaveSvg(string path)
    {
        double b = 104, cx = 128, cy = 128, len = b * 0.2, wid = b * 0.11, step = b * 0.3, r = wid * 0.3;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" width=\"256\" height=\"256\">");
        sb.AppendLine("  <title>Miller Driver Updater</title>");
        sb.AppendLine("  <defs>");
        sb.AppendLine("    <linearGradient id=\"tile\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"><stop offset=\"0\" stop-color=\"" + Top + "\"/><stop offset=\"1\" stop-color=\"" + Bottom + "\"/></linearGradient>");
        sb.AppendLine("  </defs>");
        sb.AppendLine("  <rect x=\"12\" y=\"12\" width=\"232\" height=\"232\" rx=\"56\" fill=\"url(#tile)\"/>");
        sb.AppendLine("  <g fill=\"#FFFFFF\">");
        for (int i = 0; i < 3; i++)
        {
            double off = (i - 1) * step;
            sb.AppendFormat("    <rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" rx=\"{4}\"/>\n", N(cx - b / 2 - len), N(cy + off - wid / 2), N(len + r), N(wid), N(r));
            sb.AppendFormat("    <rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" rx=\"{4}\"/>\n", N(cx + b / 2 - r), N(cy + off - wid / 2), N(len + r), N(wid), N(r));
            sb.AppendFormat("    <rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" rx=\"{4}\"/>\n", N(cx + off - wid / 2), N(cy - b / 2 - len), N(wid), N(len + r), N(r));
            sb.AppendFormat("    <rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" rx=\"{4}\"/>\n", N(cx + off - wid / 2), N(cy + b / 2 - r), N(wid), N(len + r), N(r));
        }
        sb.AppendFormat("    <rect x=\"{0}\" y=\"{0}\" width=\"{1}\" height=\"{1}\" rx=\"{2}\"/>\n", N(cx - b / 2), N(b), N(b * 0.16));
        sb.AppendLine("  </g>");
        double[] p = ArrowPoints(cx, cy, b);
        StringBuilder pts = new StringBuilder();
        for (int i = 0; i < p.Length; i += 2) pts.Append((i > 0 ? " " : "") + N(p[i]) + "," + N(p[i + 1]));
        sb.AppendFormat("  <polygon points=\"{0}\" fill=\"{1}\" stroke=\"{1}\" stroke-width=\"{2}\" stroke-linejoin=\"round\"/>\n", pts, ArrowColor, N(b * 0.035));
        sb.AppendLine("</svg>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("  " + path);
    }

    // Installer welcome/finish picture: the chip in white on the blue of the tile, name and sources underneath.
    static BitmapSource Wizard()
    {
        return Render(164, 314, dc =>
        {
            dc.DrawRectangle(TileBrush(), null, new Rect(0, 0, 164, 314));
            DrawChip(dc, 82, 104, 58, 3, Brushes.White, Solid(ArrowColor));
            Brush white = Brushes.White, soft = Solid("#D6E6FF");
            FormattedText name = Text("Miller Driver\nUpdater", 20, FontWeights.SemiBold, white, 150, TextAlignment.Center);
            name.LineHeight = 25;
            dc.DrawText(name, new Point(7, 170));
            FormattedText tag = Text("Drivers and apps from\nWindows Update, NVIDIA\nand winget", 11.5, FontWeights.Normal, soft, 150, TextAlignment.Center);
            tag.LineHeight = 16;
            dc.DrawText(tag, new Point(7, 233));
        });
    }

    // Installer page header (white strip, picture on the right).
    static BitmapSource Header()
    {
        return Render(150, 57, dc =>
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 150, 57));
            DrawIcon(dc, 150 - 48 - 6, 4.5, 48);
        });
    }

    // GitHub's link preview (Settings -> Social preview): what people see when the repo is shared in a chat.
    static BitmapSource Social()
    {
        return Render(1280, 640, dc =>
        {
            dc.DrawRectangle(Solid("#F5F7FB"), null, new Rect(0, 0, 1280, 640));
            DrawIcon(dc, 104, 196, 248);
            dc.DrawText(Text("Miller Driver Updater", 68, FontWeights.SemiBold, Solid("#1D1D1F")), new Point(400, 204));
            dc.DrawText(Text("Driver, NVIDIA and app updates for Windows,\nstraight from Windows Update, nvidia.com and winget.", 30, FontWeights.Normal, Solid("#4A4A50"), 800), new Point(404, 302));
            dc.DrawText(Text("Free   ·   Open source (MIT)   ·   No ads, no tracking", 26, FontWeights.SemiBold, Solid(ArrowColor)), new Point(404, 410));
        });
    }

    [STAThread]
    static int Main()
    {
        Console.WriteLine("Drawing the artwork:");
        Directory.CreateDirectory("docs");
        SaveIco(@"assets\mdu.ico", new[] { 16, 20, 24, 32, 40, 48, 64, 256 });
        SavePng(Render(256, 256, dc => DrawIcon(dc, 0, 0, 256)), @"assets\mdu-256.png");
        SavePng(Render(64, 64, dc => DrawIcon(dc, 0, 0, 64)), @"assets\mdu-64.png");
        SaveSvg(@"assets\logo.svg");
        SaveBmp24(Wizard(), @"installer\wizard.bmp");
        SaveBmp24(Header(), @"installer\header.bmp");
        SavePng(Social(), @"docs\social-preview.png");
        return 0;
    }
}
