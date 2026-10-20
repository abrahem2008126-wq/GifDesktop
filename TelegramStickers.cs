// TelegramStickers - يفتح ستيكرات تيليكرام المتحركة (.tgs و .webm) عن طريق محرك Edge (WebView2)
// الإطارات تنحفظ بمجلد cache حتى المرة الجاية تفتح فوراً
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

public static class TelegramStickers
{
    const int TimeoutSeconds = 60;

    static string libDir, cacheDir, dataDir;
    static bool loaderSet;

    // يربط هذا الكلاس بـ GifBox حتى يقدر يفتح .tgs و .webm
    public static void Register(string lib, string cache, string data)
    {
        libDir = lib; cacheDir = cache; dataDir = data;
        GifBox.AnimatedLoader = Load;
    }

    public static bool IsSupported(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".tgs" || ext == ".webm";
    }

    static Tuple<Bitmap[], int[]> Load(string path)
    {
        string dir = Path.Combine(cacheDir, CacheKey(path));
        string info = Path.Combine(dir, "info.txt");
        if (File.Exists(info)) return ReadCache(dir, info);

        int delay;
        List<Bitmap> frames = Render(path, out delay);
        WriteCache(dir, info, frames, delay);
        return Tuple.Create(frames.ToArray(), Repeat(delay, frames.Count));
    }

    // ---------- WebView2 ----------
    static List<Bitmap> Render(string path, out int delay)
    {
        if (!loaderSet)
        {
            string arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
            string sub = arch.Equals("ARM64", StringComparison.OrdinalIgnoreCase) ? "arm64"
                       : Environment.Is64BitProcess ? "x64" : "x86";
            CoreWebView2Environment.SetLoaderDllFolderPath(Path.Combine(libDir, sub));
            loaderSet = true;
        }

        string message;
        if (path.EndsWith(".tgs", StringComparison.OrdinalIgnoreCase))
        {
            // .tgs = JSON مضغوط بـ gzip
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var rd = new StreamReader(gz, Encoding.UTF8))
                message = "tgs:" + rd.ReadToEnd();
        }
        else
        {
            message = "webm:" + Convert.ToBase64String(File.ReadAllBytes(path));
        }

        var frames = new List<Bitmap>();
        int doneDelay = 0;
        string error = null;
        bool done = false;

        // نافذة مخفية برا الشاشة: WebView2 يحتاج نافذة حتى يشتغل
        var host = new Form();
        host.ShowInTaskbar = false;
        host.FormBorderStyle = FormBorderStyle.None;
        host.StartPosition = FormStartPosition.Manual;
        host.Location = new Point(-20000, -20000);
        host.Size = new Size(400, 400);
        var view = new WebView2();
        view.Dock = DockStyle.Fill;
        host.Controls.Add(view);
        try
        {
            host.Show();
            Task<CoreWebView2Environment> envTask = CoreWebView2Environment.CreateAsync(null, dataDir, null);
            Wait(envTask);
            Wait(view.EnsureCoreWebView2Async(envTask.Result));
            CoreWebView2 core = view.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("gifdesktop.local", libDir, CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                string m = e.TryGetWebMessageAsString();
                if (m.StartsWith("frame:")) frames.Add(FromDataUrl(m.Substring(6)));
                else if (m.StartsWith("done:")) { doneDelay = int.Parse(m.Substring(5)); done = true; }
                else if (m.StartsWith("error:")) { error = m.Substring(6); done = true; }
            };
            core.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (e.IsSuccess) core.PostWebMessageAsString(message);
                else { error = "Could not open the sticker decoder page."; done = true; }
            };
            core.Navigate("https://gifdesktop.local/decoder.html");

            DateTime until = DateTime.Now.AddSeconds(TimeoutSeconds);
            while (!done)
            {
                if (DateTime.Now > until) { error = "Timed out while reading the sticker."; break; }
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }
        finally
        {
            view.Dispose();
            host.Dispose();
        }

        if (error != null || frames.Count == 0)
        {
            foreach (Bitmap b in frames) b.Dispose();
            throw new InvalidDataException(error ?? "The sticker has no frames.");
        }
        delay = doneDelay < 20 ? 33 : doneDelay;
        return frames;
    }

    // ينتظر task بدون ما يوقف الرسائل (WebView2 يحتاجها حتى يكمل)
    static void Wait(Task t)
    {
        DateTime until = DateTime.Now.AddSeconds(TimeoutSeconds);
        while (!t.IsCompleted)
        {
            if (DateTime.Now > until) throw new TimeoutException("WebView2 did not start.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
        if (t.IsFaulted) throw t.Exception.GetBaseException();
    }

    static Bitmap FromDataUrl(string url)
    {
        byte[] bytes = Convert.FromBase64String(url.Substring(url.IndexOf(',') + 1));
        using (var ms = new MemoryStream(bytes))
        using (var tmp = new Bitmap(ms))
        {
            var b = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
            return b;
        }
    }

    // ---------- الكاش ----------
    static string CacheKey(string path)
    {
        var fi = new FileInfo(path);
        string id = fi.FullName.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
        using (var sha = SHA1.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(id));
            return BitConverter.ToString(h).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }
    }

    static void WriteCache(string dir, string info, List<Bitmap> frames, int delay)
    {
        try
        {
            Directory.CreateDirectory(dir);
            for (int i = 0; i < frames.Count; i++)
                frames[i].Save(Path.Combine(dir, i.ToString("D4") + ".png"), ImageFormat.Png);
            File.WriteAllText(info, frames.Count + " " + delay);   // آخر شي: يعني الكاش كامل
        }
        catch { }   // إذا ما انحفظ، يتحوّل من جديد المرة الجاية
    }

    static Tuple<Bitmap[], int[]> ReadCache(string dir, string info)
    {
        string[] parts = File.ReadAllText(info).Split(' ');
        int count = int.Parse(parts[0]), delay = int.Parse(parts[1]);
        var frames = new Bitmap[count];
        for (int i = 0; i < count; i++)
        {
            using (var tmp = new Bitmap(Path.Combine(dir, i.ToString("D4") + ".png")))
            {
                frames[i] = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(frames[i])) g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
            }
        }
        return Tuple.Create(frames, Repeat(delay, count));
    }

    static int[] Repeat(int v, int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = v;
        return a;
    }
}
