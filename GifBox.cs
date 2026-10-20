// GifBox - يشغّل GIF (أو صورة عادية) ويشيل الخلفية تلقائياً إذا كانت لون واحد
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class GifBox : Control
{
    // لون الشفافية للنافذة (TransparencyKey): لازم يطابق بالضبط
    public static readonly Color Key = Color.FromArgb(1, 2, 3);
    const int Tolerance = 14;          // قد إيش اللون يقدر يختلف عن لون الخلفية
    const double MinBorderShare = 0.5; // نسبة حواف الصورة اللي لازم تكون بلون الخلفية

    Bitmap[] frames = new Bitmap[0];
    Bitmap[] scaled = new Bitmap[0];
    int[] delays = new int[0];
    int current;
    SimpleSvg vector;   // إذا الملف SVG ينرسم من جديد بكل حجم بدل تكبير صورة
    readonly Timer timer = new Timer();

    // يفتح ستيكرات تيليكرام (.tgs و .webm). يتعبى من TelegramStickers إذا ملفات WebView2 موجودة
    public static Func<string, Tuple<Bitmap[], int[]>> AnimatedLoader;

    public Size ImageSize { get; private set; }
    public bool BackgroundRemoved { get; private set; }

    public GifBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Key;
        timer.Tick += delegate { NextFrame(); };
    }

    public void LoadImage(string path, bool removeBackground)
    {
        timer.Stop();
        DisposeFrames();
        vector = null;
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            vector = SimpleSvg.Load(path);
            frames = new Bitmap[] { new Bitmap(1, 1) };
            delays = new int[] { 100 };
            ImageSize = vector.Size;
            BackgroundRemoved = false;
            scaled = new Bitmap[1];
            current = 0;
            Invalidate();
            return;
        }
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".tgs" || ext == ".webm")
        {
            if (AnimatedLoader == null)
                throw new NotSupportedException("Telegram stickers need the 'lib' folder (WebView2) next to the program.");
            Tuple<Bitmap[], int[]> anim = AnimatedLoader(path);
            frames = anim.Item1;
            delays = anim.Item2;
            ImageSize = frames[0].Size;
            BackgroundRemoved = removeBackground && RemoveBackground(frames);
            scaled = new Bitmap[frames.Length];
            current = 0;
            Invalidate();
            if (frames.Length > 1) { timer.Interval = delays[0]; timer.Start(); }
            return;
        }
        using (var img = LoadAny(path))
        {
            int n = 1;
            try { n = Math.Max(1, img.GetFrameCount(FrameDimension.Time)); } catch { }
            PropertyItem delayProp = null;
            try { delayProp = img.GetPropertyItem(0x5100); } catch { }

            frames = new Bitmap[n];
            delays = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (n > 1) img.SelectActiveFrame(FrameDimension.Time, i);
                var b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(b)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                frames[i] = b;
                int d = (delayProp != null && delayProp.Value.Length >= (i + 1) * 4)
                    ? BitConverter.ToInt32(delayProp.Value, i * 4) * 10 : 100;
                delays[i] = d < 20 ? 100 : d;   // مثل المتصفحات: التأخير الصغير جداً = 100ms
            }
            ImageSize = img.Size;
        }
        BackgroundRemoved = removeBackground && RemoveBackground(frames);
        scaled = new Bitmap[frames.Length];
        current = 0;
        Invalidate();
        if (frames.Length > 1) { timer.Interval = delays[0]; timer.Start(); }
    }

    // GIF/PNG/JPG يقرأها ويندوز مباشرة، و WebP (ستيكرات واتساب وتيليكرام) عن طريق WIC
    internal static Image LoadAny(string path)
    {
        try { return Image.FromFile(path); }
        catch (OutOfMemoryException) { }   // هكذا يرد GDI+ على صيغة ما يعرفها
        catch (ArgumentException) { }

        using (var fs = System.IO.File.OpenRead(path))
        {
            var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(fs,
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dec.Frames[0]));
            using (var ms = new System.IO.MemoryStream())
            {
                enc.Save(ms);
                ms.Position = 0;
                using (var tmp = new Bitmap(ms)) return new Bitmap(tmp);
            }
        }
    }

    // ---------- إزالة الخلفية ----------
    internal static bool RemoveBackground(Bitmap[] fr)
    {
        int w = fr[0].Width, h = fr[0].Height;
        int[] px = ReadPixels(fr[0]);
        List<int> border = BorderIndices(w, h);

        // إذا الحواف أصلاً شفافة، الصورة ما تحتاج شي
        int clear = 0;
        foreach (int i in border) if (Alpha(px[i]) < 128) clear++;
        if (clear > border.Count * 0.3) return false;

        // لون الخلفية = لون الزاوية اللي أكثر الحواف تشبهه
        int[] corners = { px[0], px[w - 1], px[(h - 1) * w], px[h * w - 1] };
        int bg = 0, best = 0;
        foreach (int c in corners)
        {
            int count = 0;
            foreach (int i in border) if (Near(px[i], c)) count++;
            if (count > best) { best = count; bg = c; }
        }
        if (best < border.Count * MinBorderShare) return false;

        foreach (Bitmap b in fr)
        {
            int[] p = ReadPixels(b);
            FloodClear(p, w, h, border, bg);
            WritePixels(b, p);
        }
        return true;
    }

    // يمسح كل البكسلات المتصلة بالحواف واللي لونها قريب من الخلفية
    static void FloodClear(int[] px, int w, int h, List<int> border, int bg)
    {
        var seen = new bool[px.Length];
        var stack = new Stack<int>(border);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (seen[i]) continue;
            seen[i] = true;
            if (!Near(px[i], bg)) continue;
            px[i] &= 0x00FFFFFF;   // شفاف، ونخلي اللون حتى الحواف ما تطلع سودة عند التكبير
            int x = i % w, y = i / w;
            if (x > 0) stack.Push(i - 1);
            if (x < w - 1) stack.Push(i + 1);
            if (y > 0) stack.Push(i - w);
            if (y < h - 1) stack.Push(i + w);
        }
    }

    static List<int> BorderIndices(int w, int h)
    {
        var list = new List<int>();
        for (int x = 0; x < w; x++) { list.Add(x); list.Add((h - 1) * w + x); }
        for (int y = 1; y < h - 1; y++) { list.Add(y * w); list.Add(y * w + w - 1); }
        return list;
    }

    static int Alpha(int c) { return (c >> 24) & 0xFF; }

    static bool Near(int a, int b)
    {
        if (Alpha(a) < 128 || Alpha(b) < 128) return Alpha(a) < 128 && Alpha(b) < 128;
        return Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) <= Tolerance
            && Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) <= Tolerance
            && Math.Abs((a & 0xFF) - (b & 0xFF)) <= Tolerance;
    }

    internal static int[] ReadPixels(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[b.Width * b.Height];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        b.UnlockBits(d);
        return px;
    }

    internal static void WritePixels(Bitmap b, int[] px)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, d.Scan0, px.Length);
        b.UnlockBits(d);
    }

    // ---------- الرسم ----------
    // كل إطار ينرسم بحجم النافذة مرة وحدة، والشفاف يصير لون Key بالضبط
    Bitmap ScaledFrame(int i)
    {
        int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
        Bitmap s = scaled[i];
        if (s != null && s.Width == w && s.Height == h) return s;
        if (s != null) s.Dispose();

        s = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(s))
        using (var ia = new ImageAttributes())
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            ia.SetWrapMode(WrapMode.TileFlipXY);
            Bitmap f = frames[i];
            if (vector != null) vector.Render(g, w, h);
            else g.DrawImage(f, new Rectangle(0, 0, w, h), 0, 0, f.Width, f.Height, GraphicsUnit.Pixel, ia);
        }
        int[] px = ReadPixels(s);
        int key = Key.ToArgb();
        for (int j = 0; j < px.Length; j++)
            px[j] = Alpha(px[j]) < 128 ? key : (px[j] | unchecked((int)0xFF000000));
        WritePixels(s, px);
        scaled[i] = s;
        return s;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (frames.Length == 0) return;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.CompositingMode = CompositingMode.SourceCopy;
        Bitmap s = ScaledFrame(current);
        e.Graphics.DrawImage(s, new Rectangle(0, 0, s.Width, s.Height));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        foreach (Bitmap s in scaled) if (s != null) s.Dispose();
        scaled = new Bitmap[frames.Length];
        Invalidate();
    }

    void NextFrame()
    {
        current = (current + 1) % frames.Length;
        timer.Interval = delays[current];
        Invalidate();
    }

    void DisposeFrames()
    {
        foreach (Bitmap b in frames) b.Dispose();
        foreach (Bitmap s in scaled) if (s != null) s.Dispose();
        frames = new Bitmap[0];
        scaled = new Bitmap[0];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); DisposeFrames(); }
        base.Dispose(disposing);
    }
}

// ألوان قائمة الزر الأيمن: خلفية فاتحة وتحديد أزرق هادي بدل الشكل القديم
public class MenuColors : ProfessionalColorTable
{
    static readonly Color Bg = Color.FromArgb(250, 250, 250);
    static readonly Color Hover = Color.FromArgb(229, 239, 255);
    static readonly Color HoverBorder = Color.FromArgb(170, 198, 245);
    static readonly Color Line = Color.FromArgb(222, 222, 222);

    public override Color ToolStripDropDownBackground { get { return Bg; } }
    public override Color ImageMarginGradientBegin { get { return Bg; } }
    public override Color ImageMarginGradientMiddle { get { return Bg; } }
    public override Color ImageMarginGradientEnd { get { return Bg; } }
    public override Color MenuBorder { get { return Line; } }
    public override Color MenuItemBorder { get { return HoverBorder; } }
    public override Color MenuItemSelected { get { return Hover; } }
    public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
    public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
    public override Color SeparatorDark { get { return Line; } }
    public override Color SeparatorLight { get { return Bg; } }
    public override Color CheckBackground { get { return Hover; } }
    public override Color CheckSelectedBackground { get { return Hover; } }
    public override Color CheckPressedBackground { get { return Hover; } }
    public override Color ButtonSelectedBorder { get { return HoverBorder; } }
}
