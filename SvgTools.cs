// SvgTools - يحوّل الصور الثابتة إلى SVG، ويرسم ملفات SVG بأي حجم بدون ما تخرب الدقة
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public static class SvgTracer
{
    const int MaxTraceSide = 400;   // الصور الكبيرة تتصغر قبل التتبع حتى يبقى الملف معقول
    const int PhotoColors = 16;     // عدد الألوان للصور العادية (غير البكسل آرت)

    // يرجع "pixel" إذا الصورة بكسل آرت (تتحول بالضبط)، أو "traced" إذا انرسمت بألوان مبسطة
    public static string ConvertFile(string input, string output, bool removeBackground)
    {
        Bitmap src;
        using (var img = GifBox.LoadAny(input))
        {
            src = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(src)) g.DrawImage(img, 0, 0, img.Width, img.Height);
        }
        int origW = src.Width, origH = src.Height;
        if (removeBackground) GifBox.RemoveBackground(new Bitmap[] { src });

        int gw, gh;
        int[] labels;
        List<int> palette;
        bool pixelArt;
        int[] px = GifBox.ReadPixels(src);
        double cell, offX, offY;

        if (IsNativePixelArt(px, src.Width, src.Height))
        {
            pixelArt = true; gw = src.Width; gh = src.Height;
            labels = Palettize(px, out palette, 0);
        }
        else if (DetectGrid(px, src.Width, src.Height, out cell, out offX, out offY))
        {
            pixelArt = true;
            int[] cells = SampleGrid(px, src.Width, src.Height, cell, offX, offY, out gw, out gh);
            labels = Palettize(cells, out palette, 40);
        }
        else
        {
            pixelArt = false;
            Bitmap small = Shrink(src, MaxTraceSide);
            gw = small.Width; gh = small.Height;
            int[] spx = GifBox.ReadPixels(small);
            if (small != src) small.Dispose();
            labels = Quantize(spx, PhotoColors, out palette);
            ModeFilter(labels, gw, gh);
        }
        src.Dispose();

        string svg = BuildSvg(labels, gw, gh, palette, pixelArt, origW, origH);
        File.WriteAllText(output, svg, new UTF8Encoding(false));
        return pixelArt ? "pixel" : "traced";
    }

    // ---------- كشف البكسل آرت ----------
    // صورة صغيرة بألوان قليلة = كل بكسل مربع لحاله
    static bool IsNativePixelArt(int[] px, int w, int h)
    {
        if (w > 512 || h > 512) return false;
        var colors = new HashSet<int>();
        foreach (int c in px)
        {
            if (((c >> 24) & 0xFF) < 128) continue;
            colors.Add(c | unchecked((int)0xFF000000));
            if (colors.Count > 64) return false;
        }
        return colors.Count > 0;
    }

    // بكسل آرت مكبّر: يدور على حجم المربع اللي كل تغيّرات اللون تصير على حدوده
    static bool DetectGrid(int[] px, int w, int h, out double cell, out double offX, out double offY)
    {
        cell = 0; offX = 0; offY = 0;
        int[] hx = ChangeHistogram(px, w, h, true);
        int[] hy = ChangeHistogram(px, w, h, false);
        int tx = 0, ty = 0;
        foreach (int c in hx) tx += c;
        foreach (int c in hy) ty += c;
        if (tx < 20 || ty < 20) return false;

        for (double s = 64; s >= 3; s -= 0.25)
        {
            if (s * 6 > Math.Min(w, h)) continue;   // لازم على الأقل 6 مربعات بكل اتجاه
            double ox, oy;
            if (Fits(hx, tx, s, out ox) && Fits(hy, ty, s, out oy))
            {
                cell = s; offX = ox; offY = oy;
                return true;
            }
        }
        return false;
    }

    // كم مرة يتغير اللون بشكل واضح عند كل عمود (أو صف)
    static int[] ChangeHistogram(int[] px, int w, int h, bool horizontal)
    {
        int len = horizontal ? w : h, other = horizontal ? h : w;
        var hist = new int[len + 1];
        for (int o = 0; o < other; o += 2)
            for (int i = 1; i < len; i++)
            {
                int a = horizontal ? px[o * w + i - 1] : px[(i - 1) * w + o];
                int b = horizontal ? px[o * w + i] : px[i * w + o];
                if (Diff(a, b) > 60) hist[i]++;
            }
        return hist;
    }

    static bool Fits(int[] hist, int total, double s, out double offset)
    {
        const int Res = 4;   // دقة ربع بكسل
        int bins = (int)Math.Ceiling(s * Res);
        var fold = new int[bins];
        for (int p = 0; p < hist.Length; p++)
            if (hist[p] > 0) fold[Math.Min(bins - 1, (int)((p % s) * Res))] += hist[p];

        int win = (int)(Math.Min(1.5, s * 0.15) * Res);
        int best = 0, bestBin = 0;
        for (int b = 0; b < bins; b++)
        {
            int sum = 0;
            for (int k = -win; k <= win; k++) sum += fold[((b + k) % bins + bins) % bins];
            if (sum > best) { best = sum; bestBin = b; }
        }
        offset = (double)bestBin / Res;
        return best >= total * 0.9;
    }

    // ياخذ لون وسط كل مربع
    static int[] SampleGrid(int[] px, int w, int h, double cell, double offX, double offY, out int gw, out int gh)
    {
        double sx = offX - Math.Ceiling(offX / cell - 1e-9) * cell;
        double sy = offY - Math.Ceiling(offY / cell - 1e-9) * cell;
        gw = (int)Math.Ceiling((w - sx) / cell - 1e-9);
        gh = (int)Math.Ceiling((h - sy) / cell - 1e-9);
        var cells = new int[gw * gh];
        for (int j = 0; j < gh; j++)
            for (int i = 0; i < gw; i++)
            {
                int x0 = Clamp((int)(sx + (i + 0.25) * cell), 0, w - 1), x1 = Clamp((int)(sx + (i + 0.75) * cell), 0, w - 1);
                int y0 = Clamp((int)(sy + (j + 0.25) * cell), 0, h - 1), y1 = Clamp((int)(sy + (j + 0.75) * cell), 0, h - 1);
                long r = 0, g = 0, b = 0; int n = 0, all = 0;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int c = px[y * w + x]; all++;
                        if (((c >> 24) & 0xFF) < 128) continue;
                        r += (c >> 16) & 0xFF; g += (c >> 8) & 0xFF; b += c & 0xFF; n++;
                    }
                cells[j * gw + i] = n * 2 < all ? 0
                    : unchecked((int)0xFF000000) | (int)(r / n) << 16 | (int)(g / n) << 8 | (int)(b / n);
            }
        return cells;
    }

    // يحوّل كل لون لرقم بالـ palette، والألوان القريبة (أقل من tol) تندمج. -1 = شفاف
    static int[] Palettize(int[] px, out List<int> palette, int tol)
    {
        palette = new List<int>();
        var labels = new int[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            int c = px[i];
            if (((c >> 24) & 0xFF) < 128) { labels[i] = -1; continue; }
            int found = -1;
            for (int k = 0; k < palette.Count && found < 0; k++)
                if (Diff(palette[k], c) <= tol) found = k;
            if (found < 0) { palette.Add(c | unchecked((int)0xFF000000)); found = palette.Count - 1; }
            labels[i] = found;
        }
        return labels;
    }

    // ---------- الصور العادية ----------
    static Bitmap Shrink(Bitmap src, int maxSide)
    {
        int m = Math.Max(src.Width, src.Height);
        if (m <= maxSide) return src;
        int w = Math.Max(1, src.Width * maxSide / m), h = Math.Max(1, src.Height * maxSide / m);
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, w, h);
        }
        return b;
    }

    // k-means بسيط: يختار أهم k ألوان بالصورة
    static int[] Quantize(int[] px, int k, out List<int> palette)
    {
        var opaque = new List<int>();
        int step = Math.Max(1, px.Length / 6000);
        for (int i = 0; i < px.Length; i += step)
            if (((px[i] >> 24) & 0xFF) >= 128) opaque.Add(px[i]);
        palette = new List<int>();
        var labels = new int[px.Length];
        if (opaque.Count == 0) { for (int i = 0; i < labels.Length; i++) labels[i] = -1; return labels; }

        k = Math.Min(k, opaque.Count);
        var cr = new double[k]; var cg = new double[k]; var cb = new double[k];
        for (int j = 0; j < k; j++)
        {
            int c = opaque[j * opaque.Count / k];
            cr[j] = (c >> 16) & 0xFF; cg[j] = (c >> 8) & 0xFF; cb[j] = c & 0xFF;
        }
        for (int iter = 0; iter < 12; iter++)
        {
            var sr = new double[k]; var sg = new double[k]; var sb = new double[k]; var n = new int[k];
            foreach (int c in opaque)
            {
                int j = Nearest(c, cr, cg, cb);
                sr[j] += (c >> 16) & 0xFF; sg[j] += (c >> 8) & 0xFF; sb[j] += c & 0xFF; n[j]++;
            }
            for (int j = 0; j < k; j++)
                if (n[j] > 0) { cr[j] = sr[j] / n[j]; cg[j] = sg[j] / n[j]; cb[j] = sb[j] / n[j]; }
        }
        for (int j = 0; j < k; j++)
            palette.Add(unchecked((int)0xFF000000) | (int)cr[j] << 16 | (int)cg[j] << 8 | (int)cb[j]);
        for (int i = 0; i < px.Length; i++)
            labels[i] = ((px[i] >> 24) & 0xFF) < 128 ? -1 : Nearest(px[i], cr, cg, cb);
        return labels;
    }

    static int Nearest(int c, double[] cr, double[] cg, double[] cb)
    {
        int r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF, best = 0;
        double bd = double.MaxValue;
        for (int j = 0; j < cr.Length; j++)
        {
            double d = (r - cr[j]) * (r - cr[j]) + (g - cg[j]) * (g - cg[j]) + (b - cb[j]) * (b - cb[j]);
            if (d < bd) { bd = d; best = j; }
        }
        return best;
    }

    // يشيل النقاط الوحيدة (نويز) ويخليها بلون جيرانها
    static void ModeFilter(int[] labels, int w, int h)
    {
        var copy = (int[])labels.Clone();
        var count = new Dictionary<int, int>();
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int me = copy[y * w + x];
                if (me < 0) continue;
                count.Clear();
                int same = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int l = copy[(y + dy) * w + x + dx];
                        if (l < 0) continue;
                        if (l == me) same++;
                        int v; count.TryGetValue(l, out v); count[l] = v + 1;
                    }
                if (same > 0) continue;
                int bestL = me, bestN = 0;
                foreach (var kv in count) if (kv.Value > bestN) { bestN = kv.Value; bestL = kv.Key; }
                labels[y * w + x] = bestL;
            }
    }

    // ---------- تتبع الحدود وكتابة الـ SVG ----------
    static string BuildSvg(int[] labels, int w, int h, List<int> palette, bool pixelArt, int outW, int outH)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendFormat(ci, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {2} {3}\"{4}>\n",
            outW, outH, w, h, pixelArt ? " shape-rendering=\"crispEdges\"" : "");

        // الألوان الأكبر مساحة أول، والصغيرة فوقها
        var area = new int[palette.Count];
        foreach (int l in labels) if (l >= 0) area[l]++;
        var order = new List<int>();
        for (int i = 0; i < palette.Count; i++) if (area[i] > 0) order.Add(i);
        order.Sort(delegate(int a, int b) { return area[b].CompareTo(area[a]); });

        foreach (int c in order)
        {
            string d = TraceColor(labels, w, h, c, !pixelArt);
            string hex = (palette[c] & 0xFFFFFF).ToString("x6");
            if (pixelArt)
                sb.AppendFormat("<path fill=\"#{0}\" fill-rule=\"evenodd\" d=\"{1}\"/>\n", hex, d);
            else   // خط رفيع بنفس اللون يسد الفراغات الصغيرة بين الألوان
                sb.AppendFormat("<path fill=\"#{0}\" fill-rule=\"evenodd\" stroke=\"#{0}\" stroke-width=\"0.6\" stroke-linejoin=\"round\" d=\"{1}\"/>\n", hex, d);
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    // يرسم حدود كل البكسلات من لون واحد كخطوط مغلقة
    static string TraceColor(int[] labels, int w, int h, int c, bool smooth)
    {
        int vw = w + 1;
        var outgoing = new Dictionary<int, List<int>>();
        Action<int, int, int, int> edge = delegate(int x0, int y0, int x1, int y1)
        {
            int a = y0 * vw + x0;
            List<int> list;
            if (!outgoing.TryGetValue(a, out list)) { list = new List<int>(2); outgoing[a] = list; }
            list.Add(y1 * vw + x1);
        };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (labels[y * w + x] != c) continue;
                if (y == 0 || labels[(y - 1) * w + x] != c) edge(x, y, x + 1, y);
                if (x == w - 1 || labels[y * w + x + 1] != c) edge(x + 1, y, x + 1, y + 1);
                if (y == h - 1 || labels[(y + 1) * w + x] != c) edge(x + 1, y + 1, x, y + 1);
                if (x == 0 || labels[y * w + x - 1] != c) edge(x, y + 1, x, y);
            }

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var keys = new List<int>(outgoing.Keys);
        foreach (int start in keys)
        {
            List<int> first;
            while (outgoing.TryGetValue(start, out first) && first.Count > 0)
            {
                var loop = new List<PointF>();
                int v = start;
                do
                {
                    loop.Add(new PointF(v % vw, v / vw));
                    List<int> outs = outgoing[v];
                    int next = outs[outs.Count - 1];
                    outs.RemoveAt(outs.Count - 1);
                    v = next;
                } while (v != start);

                if (smooth) loop = Midpoints(loop);
                loop = RemoveCollinear(loop);
                if (loop.Count < 3) continue;
                sb.Append('M');
                for (int i = 0; i < loop.Count; i++)
                {
                    if (i == 1) sb.Append('L');
                    else if (i > 1) sb.Append(' ');
                    sb.Append(loop[i].X.ToString("0.##", ci)).Append(' ').Append(loop[i].Y.ToString("0.##", ci));
                }
                sb.Append('Z');
            }
        }
        return sb.ToString();
    }

    // يقص زوايا الدرج: كل نقطة تصير بنص الضلع، فالحواف تطلع مايلة بدل مسننة
    static List<PointF> Midpoints(List<PointF> p)
    {
        var r = new List<PointF>(p.Count);
        for (int i = 0; i < p.Count; i++)
        {
            PointF a = p[i], b = p[(i + 1) % p.Count];
            r.Add(new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2));
        }
        return r;
    }

    static List<PointF> RemoveCollinear(List<PointF> p)
    {
        var r = new List<PointF>(p.Count);
        for (int i = 0; i < p.Count; i++)
        {
            PointF a = p[(i + p.Count - 1) % p.Count], b = p[i], c = p[(i + 1) % p.Count];
            double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (Math.Abs(cross) > 1e-6) r.Add(b);
        }
        return r;
    }

    static int Diff(int a, int b)
    {
        bool ta = ((a >> 24) & 0xFF) < 128, tb = ((b >> 24) & 0xFF) < 128;
        if (ta || tb) return ta == tb ? 0 : 255;
        return Math.Max(Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)),
               Math.Max(Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)), Math.Abs((a & 0xFF) - (b & 0xFF))));
    }

    static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
}

// يقرأ ملفات SVG البسيطة (مسارات بألوان، مثل اللي يطلعها SvgTracer) ويرسمها بأي حجم
public class SimpleSvg
{
    class Shape { public Color Fill; public Color Stroke; public float StrokeWidth; public GraphicsPath Path; }

    readonly List<Shape> shapes = new List<Shape>();
    RectangleF view;
    bool crisp;
    public Size Size { get; private set; }

    public static SimpleSvg Load(string path)
    {
        string text = File.ReadAllText(path);
        var svg = new SimpleSvg();
        string root = Regex.Match(text, "<svg\\b[^>]*>", RegexOptions.IgnoreCase).Value;
        if (root.Length == 0) throw new InvalidDataException("Not an SVG file.");

        float[] vb = Numbers(Attr(root, "viewBox"));
        float w = Number(Attr(root, "width")), h = Number(Attr(root, "height"));
        if (vb.Length == 4) svg.view = new RectangleF(vb[0], vb[1], vb[2], vb[3]);
        else svg.view = new RectangleF(0, 0, w > 0 ? w : 100, h > 0 ? h : 100);
        if (w <= 0) w = svg.view.Width;
        if (h <= 0) h = svg.view.Height;
        svg.Size = new Size(Math.Max(1, (int)w), Math.Max(1, (int)h));
        svg.crisp = Attr(root, "shape-rendering") == "crispEdges";

        foreach (Match m in Regex.Matches(text, "<path\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var s = new Shape();
            s.Fill = ParseColor(Attr(m.Value, "fill"), Color.Black);
            s.Stroke = ParseColor(Attr(m.Value, "stroke"), Color.Empty);
            s.StrokeWidth = Number(Attr(m.Value, "stroke-width"));
            if (s.StrokeWidth <= 0 && !s.Stroke.IsEmpty) s.StrokeWidth = 1;
            s.Path = ParsePath(Attr(m.Value, "d"));
            s.Path.FillMode = Attr(m.Value, "fill-rule") == "evenodd" ? FillMode.Alternate : FillMode.Winding;
            svg.shapes.Add(s);
        }
        if (svg.shapes.Count == 0) throw new InvalidDataException("This SVG has no paths this program can draw.");
        return svg;
    }

    public void Render(Graphics g, int w, int h)
    {
        g.SmoothingMode = crisp ? SmoothingMode.None : SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.ScaleTransform(w / view.Width, h / view.Height);
        g.TranslateTransform(-view.X, -view.Y);
        foreach (Shape s in shapes)
        {
            if (!s.Fill.IsEmpty) using (var b = new SolidBrush(s.Fill)) g.FillPath(b, s.Path);
            if (!s.Stroke.IsEmpty)
                using (var p = new Pen(s.Stroke, s.StrokeWidth)) { p.LineJoin = LineJoin.Round; g.DrawPath(p, s.Path); }
        }
        g.ResetTransform();
    }

    // يدعم أوامر M L H V Z (كبيرة وصغيرة)
    static GraphicsPath ParsePath(string d)
    {
        var path = new GraphicsPath();
        var pts = new List<PointF>();
        float x = 0, y = 0, sx = 0, sy = 0;
        char cmd = 'M';
        var tokens = Regex.Matches(d ?? "", "[MLHVZmlhvz]|-?(?:\\d+\\.?\\d*|\\.\\d+)(?:[eE]-?\\d+)?");
        int i = 0;
        Func<float> next = delegate { return float.Parse(tokens[i++].Value, CultureInfo.InvariantCulture); };
        Action flush = delegate { if (pts.Count > 1) path.AddPolygon(pts.ToArray()); pts.Clear(); };
        while (i < tokens.Count)
        {
            string t = tokens[i].Value;
            if (char.IsLetter(t[0])) { cmd = t[0]; i++; if (cmd == 'Z' || cmd == 'z') { flush(); x = sx; y = sy; } continue; }
            bool rel = char.IsLower(cmd);
            switch (char.ToUpper(cmd))
            {
                case 'M':
                    flush();
                    x = (rel ? x : 0) + next(); y = (rel ? y : 0) + next();
                    sx = x; sy = y; pts.Add(new PointF(x, y));
                    cmd = rel ? 'l' : 'L';   // الأرقام بعد M تعتبر L
                    break;
                case 'L': x = (rel ? x : 0) + next(); y = (rel ? y : 0) + next(); pts.Add(new PointF(x, y)); break;
                case 'H': x = (rel ? x : 0) + next(); pts.Add(new PointF(x, y)); break;
                case 'V': y = (rel ? y : 0) + next(); pts.Add(new PointF(x, y)); break;
                default: i++; break;
            }
        }
        flush();
        return path;
    }

    static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, "\\s" + Regex.Escape(name) + "\\s*=\\s*[\"']([^\"']*)[\"']");
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    static float Number(string s)
    {
        var m = Regex.Match(s ?? "", "-?[\\d.]+");
        float v;
        return m.Success && float.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
    }

    static float[] Numbers(string s)
    {
        var list = new List<float>();
        foreach (Match m in Regex.Matches(s ?? "", "-?[\\d.]+"))
            list.Add(float.Parse(m.Value, CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    static Color ParseColor(string s, Color fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        if (s == "none") return Color.Empty;
        try { return ColorTranslator.FromHtml(s); } catch { return fallback; }
    }
}
