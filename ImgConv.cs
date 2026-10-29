// ImgConv — a tiny tray image converter (Windows, .NET Framework 4).
// Launch modes:
//   ImgConv.exe              — tray + window
//   ImgConv.exe /tray        — tray only (used by autostart)
//   ImgConv.exe /to jpg f..  — convert files and exit (Explorer menu)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] a)
    {
        SetProcessDPIAware();
        if (a.Length == 2 && a[0] == "/mkico") { Art.WriteIco(a[1]); return; }
        if (a.Length == 3 && a[0] == "/mkpng") // package logo, used at build time
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(a[1])));
            using (var b = Art.Draw(int.Parse(a[2]))) b.Save(a[1], ImageFormat.Png);
            return;
        }

        Cfg.Load();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (a.Length >= 3 && a[0] == "/to") { RunCli(a); return; }
        if (a.Length == 3 && a[0] == "/setup")  // called by the installer
        {
            Cfg.Save(); // marks settings as initialized, so first-run defaults don't override the installer's choice
            if (a[1] == "1") Shell.On(); else Shell.Off();
            Shell.AutoOn = a[2] == "1";
            return;
        }
        if (a.Length >= 1 && a[0] == "/uninstall") { Uninstall(Array.IndexOf(a, "/quiet") >= 0); return; }

        bool first;
        using (var mx = new Mutex(true, "ImgConv.Tray.7f3c1a", out first))
        using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "ImgConv.Show.7f3c1a"))
        {
            if (!first) { ev.Set(); return; }   // already running — just show its window
            if (Cfg.FirstRun) { Shell.On(); Cfg.Save(); } // Explorer menu is on by default
            bool silent = Array.IndexOf(a, "/tray") >= 0;
            Application.Run(new TrayCtx(ev, !silent));
        }
    }

    static void Uninstall(bool quiet)
    {
        if (!quiet && MessageBox.Show("Remove ImgConv from this computer?", "ImgConv",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        int me = System.Diagnostics.Process.GetCurrentProcess().Id;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("ImgConv"))
            if (p.Id != me) try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }

        Shell.Off();
        Shell.AutoOn = false;
        Win11Menu.Unregister();
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\ImgConv", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ImgConv", false);
        foreach (var sf in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
        {
            string lnk = Path.Combine(Environment.GetFolderPath(sf), "ImgConv.lnk");
            if (File.Exists(lnk)) File.Delete(lnk);
        }

        if (!quiet) MessageBox.Show("ImgConv has been removed.", "ImgConv", MessageBoxButtons.OK, MessageBoxIcon.Information);

        // Delete the exe folder after this process exits.
        string dir = Path.GetDirectoryName(Application.ExecutablePath);
        Win11Menu.MoveAwayDll(dir); // Explorer may still have it loaded
        if (string.Equals(Path.GetFileName(dir), "ImgConv", StringComparison.OrdinalIgnoreCase))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }

    const string BatchName = "ImgConv.Batch.7f3c1a";

    static void RunCli(string[] a)
    {
        string fmt = a[1].ToLowerInvariant();
        var files = new List<string>();
        for (int i = 2; i < a.Length; i++) files.Add(a[i]);

        // Explorer starts one process per selected file. The first one collects the rest,
        // so the user gets a single dialog for the whole selection.
        bool leader;
        using (var mx = new Mutex(true, BatchName, out leader))
        {
            if (!leader) { if (SendToLeader(files)) return; }
            else CollectFollowers(files);
        }

        if (Cfg.Ask)
            using (var f = new AskForm(fmt, files.Count))
                if (f.ShowDialog() != DialogResult.OK) return;

        var errs = new List<string>();
        foreach (var file in files)
        {
            try { Conv.Run(file, fmt, Cfg.Quality, Cfg.TargetKB, Cfg.MaxSide); }
            catch (Exception ex) { errs.Add(Path.GetFileName(file) + ": " + ex.Message); }
        }
        if (errs.Count > 0)
            MessageBox.Show(string.Join("\n", errs.Count > 15 ? errs.GetRange(0, 15) : errs), "ImgConv — error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    static bool SendToLeader(List<string> files)
    {
        try
        {
            using (var c = new NamedPipeClientStream(".", BatchName, PipeDirection.Out))
            {
                c.Connect(2000);
                byte[] b = Encoding.UTF8.GetBytes(string.Join("\n", files) + "\n");
                c.Write(b, 0, b.Length);
                c.Flush();
            }
            return true;
        }
        catch (Exception) { return false; } // leader is gone — handle these files ourselves
    }

    static void CollectFollowers(List<string> files)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 15000)
        {
            var s = new NamedPipeServerStream(BatchName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                var ar = s.BeginWaitForConnection(null, null);
                if (!ar.AsyncWaitHandle.WaitOne(500)) return; // nobody else for 0.5 s — the batch is complete
                s.EndWaitForConnection(ar);
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    string l;
                    while ((l = r.ReadLine()) != null)
                        if (l.Length > 0 && !files.Contains(l)) files.Add(l);
                }
            }
            catch (Exception) { return; }
            finally { s.Dispose(); }
        }
    }
}

// ---------- Settings (HKCU\Software\ImgConv) ----------
static class Cfg
{
    const string Key = @"Software\ImgConv";
    public static readonly string[] Formats = { "jpg", "png", "bmp", "gif", "tiff" };
    public static string Format = "jpg";
    public static int Quality = 100;
    public static int TargetKB = 490; // 0 = no limit
    public static int MaxSide = 0;    // 0 = original size
    public static bool Ask = true;    // show the size/quality dialog for Explorer conversions
    public static bool Jpegli = true; // use jpegli for JPG when ImgConvCodecs.dll is present
    public static bool FirstRun;

    public static void Load()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Key))
        {
            if (k == null) { FirstRun = true; return; }
            var f = k.GetValue("Format") as string;
            if (f != null && Array.IndexOf(Formats, f) >= 0) Format = f;
            Quality = Get(k, "Quality", 1, 100, Quality);
            TargetKB = Get(k, "TargetKB", 0, 10000000, TargetKB);
            MaxSide = Get(k, "MaxSide", 0, 100000, 0);
            Ask = Get(k, "Ask", 0, 1, 1) == 1;
            Jpegli = Get(k, "Jpegli", 0, 1, 1) == 1;
        }
    }

    static int Get(RegistryKey k, string n, int lo, int hi, int d)
    {
        object o = k.GetValue(n);
        return o is int ? Math.Max(lo, Math.Min(hi, (int)o)) : d;
    }

    public static void Save()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            k.SetValue("Format", Format);
            k.SetValue("Quality", Quality, RegistryValueKind.DWord);
            k.SetValue("TargetKB", TargetKB, RegistryValueKind.DWord);
            k.SetValue("MaxSide", MaxSide, RegistryValueKind.DWord);
            k.SetValue("Ask", Ask ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("Jpegli", Jpegli ? 1 : 0, RegistryValueKind.DWord);
        }
        FirstRun = false;
        if (Shell.IsOn) Shell.On(); // refresh labels in the Explorer menu
    }

    public static string Hint()
    {
        if (Ask) return "…"; // a dialog follows
        var p = new List<string>();
        if (TargetKB > 0) p.Add("≤ " + TargetKB + " KB");
        if (MaxSide > 0) p.Add("≤ " + MaxSide + " px");
        return p.Count == 0 ? "" : "  (" + string.Join(", ", p) + ")";
    }
}

// ---------- Conversion ----------
static class Conv
{
    public static string Run(string src, string fmt, int quality, int targetKB, int maxSide)
    {
        if (Array.IndexOf(Cfg.Formats, fmt) < 0) throw new Exception("unknown format " + fmt);
        Bitmap img = Load(src);
        try
        {
            int big = Math.Max(img.Width, img.Height);
            if (maxSide > 0 && big > maxSide)
                Swap(ref img, Resize(img, (int)Math.Round(img.Width * (double)maxSide / big), (int)Math.Round(img.Height * (double)maxSide / big)));
            if (fmt == "jpg") Swap(ref img, Flatten(img)); // JPG has no transparency — put a white background under it

            byte[] data = Encode(img, fmt, quality, targetKB * 1024L);
            string dst = OutPath(src, fmt);
            File.WriteAllBytes(dst, data);
            return dst;
        }
        finally { img.Dispose(); }
    }

    static void Swap(ref Bitmap a, Bitmap b) { a.Dispose(); a = b; }

    // Picks JPG quality and, if needed, downscales so the result fits in `target` bytes.
    static byte[] Encode(Bitmap src, string fmt, int q, long target)
    {
        byte[] d = Save(src, fmt, q);
        if (target <= 0 || d.Length <= target) return d;

        bool lossy = fmt == "jpg";
        const int QMIN = 30; // below this artifacts get ugly; downscaling looks better
        double scale = 1;
        Bitmap cur = src;
        try
        {
            for (int pass = 0; pass < 15; pass++)
            {
                if (lossy)
                {
                    int lo = Math.Min(QMIN, q), hi = q;
                    byte[] ok = null;
                    while (lo <= hi)
                    {
                        int m = (lo + hi) / 2;
                        byte[] t = Save(cur, fmt, m);
                        if (t.Length <= target) { ok = t; lo = m + 1; } else { d = t; hi = m - 1; }
                    }
                    if (ok != null) return ok;
                }
                scale *= Math.Sqrt((double)target / d.Length) * 0.95;
                int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
                if (w < 8 || h < 8) return d; // can't go any smaller
                if (cur != src) cur.Dispose();
                cur = Resize(src, w, h);
                if (!lossy) { d = Save(cur, fmt, q); if (d.Length <= target) return d; }
            }
            return d;
        }
        finally { if (cur != src) cur.Dispose(); }
    }

    static ImageCodecInfo jpegCodec;

    static byte[] Save(Image img, string fmt, int q)
    {
        using (var ms = new MemoryStream())
        {
            if (fmt == "jpg" && Cfg.Jpegli && Codecs.Available)
            {
                byte[] j = Codecs.Jpegli(img, q);
                if (j != null) return j; // otherwise fall back to the Windows encoder below
            }
            if (fmt == "jpg")
            {
                if (jpegCodec == null)
                    foreach (var c in ImageCodecInfo.GetImageEncoders())
                        if (c.MimeType == "image/jpeg") jpegCodec = c;
                using (var ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)q);
                    img.Save(ms, jpegCodec, ep);
                }
            }
            else
            {
                ImageFormat f = fmt == "png" ? ImageFormat.Png : fmt == "bmp" ? ImageFormat.Bmp
                              : fmt == "gif" ? ImageFormat.Gif : ImageFormat.Tiff;
                img.Save(ms, f);
            }
            return ms.ToArray();
        }
    }

    static Bitmap Resize(Image src, int w, int h)
    {
        var b = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        using (var ia = new ImageAttributes())
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            ia.SetWrapMode(WrapMode.TileFlipXY); // avoids dark fringes at the edges
            g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        }
        return b;
    }

    static Bitmap Flatten(Image src)
    {
        var b = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.White);
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
        }
        return b;
    }

    static string OutPath(string src, string fmt)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(src)), name = Path.GetFileNameWithoutExtension(src);
        string p = Path.Combine(dir, name + "." + fmt);
        for (int n = 1; File.Exists(p); n++) p = Path.Combine(dir, name + " (" + n + ")." + fmt);
        return p;
    }

    static Bitmap Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path); // read fully so the file isn't locked
        Bitmap bmp;
        if (Codecs.IsJxl(bytes) && Codecs.Available)
        {
            bmp = Codecs.DecodeJxl(bytes);
            if (bmp != null) return bmp; // otherwise try a Windows codec (JPEG XL extension from the Store)
        }
        try
        {
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms, true, false))
            {
                bmp = new Bitmap(img);
                if (Array.IndexOf(img.PropertyIdList, 0x0112) >= 0)
                {
                    byte[] v = img.GetPropertyItem(0x0112).Value;
                    if (v != null && v.Length >= 2) Orient(bmp, BitConverter.ToUInt16(v, 0));
                }
            }
        }
        catch (Exception)
        {
            // WebP / HEIC / AVIF — via Windows' built-in codecs (WIC)
            try { bmp = LoadWic(bytes); }
            catch (Exception) { throw new Exception("unsupported format"); }
        }
        return bmp;
    }

    static void Orient(Bitmap b, int o)
    {
        RotateFlipType t;
        switch (o)
        {
            case 2: t = RotateFlipType.RotateNoneFlipX; break;
            case 3: t = RotateFlipType.Rotate180FlipNone; break;
            case 4: t = RotateFlipType.Rotate180FlipX; break;
            case 5: t = RotateFlipType.Rotate90FlipX; break;
            case 6: t = RotateFlipType.Rotate90FlipNone; break;
            case 7: t = RotateFlipType.Rotate270FlipX; break;
            case 8: t = RotateFlipType.Rotate270FlipNone; break;
            default: return;
        }
        b.RotateFlip(t);
    }

    // Separate method so WPF assemblies are loaded only when actually needed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Bitmap LoadWic(byte[] bytes)
    {
        using (var ms = new MemoryStream(bytes))
        {
            var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(ms,
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var src = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                dec.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { src.CopyPixels(System.Windows.Int32Rect.Empty, bd.Scan0, bd.Stride * h, bd.Stride); }
            finally { b.UnlockBits(bd); }
            return b;
        }
    }
}

// ---------- Optional codecs: ImgConvCodecs.dll (jpegli + JPEG XL, built from libjxl) ----------
static class Codecs
{
    const string Dll = "ImgConvCodecs.dll";
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int ic_jpegli_encode(IntPtr bgr, int w, int h, int stride, int quality, out IntPtr outBuf, out uint outSize);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int ic_jxl_decode(byte[] data, UIntPtr size, out IntPtr bgra, out int w, out int h);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern void ic_free(IntPtr p);

    static int state; // 0 = unknown, 1 = usable, -1 = missing or broken
    public static bool Available
    {
        get
        {
            if (state == 0)
                state = Environment.Is64BitProcess &&
                        File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), Dll)) ? 1 : -1;
            return state > 0;
        }
    }

    public static bool IsJxl(byte[] b)
    {
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0x0A) return true; // bare codestream
        return b.Length >= 12 && b[0] == 0 && b[1] == 0 && b[2] == 0 && b[3] == 0x0C &&
               b[4] == (byte)'J' && b[5] == (byte)'X' && b[6] == (byte)'L' && b[7] == (byte)' '; // container
    }

    // Returns null if jpegli isn't usable, so the caller can fall back.
    public static byte[] Jpegli(Image img, int quality)
    {
        var bmp = img as Bitmap;
        if (bmp == null) return null;
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            IntPtr buf; uint size;
            if (ic_jpegli_encode(bd.Scan0, bmp.Width, bmp.Height, bd.Stride, quality, out buf, out size) != 0) return null;
            try { var r = new byte[size]; Marshal.Copy(buf, r, 0, (int)size); return r; }
            finally { ic_free(buf); }
        }
        catch (Exception) { state = -1; return null; } // DLL failed to load — stop trying
        finally { bmp.UnlockBits(bd); }
    }

    public static Bitmap DecodeJxl(byte[] data)
    {
        try
        {
            IntPtr px; int w, h;
            if (ic_jxl_decode(data, (UIntPtr)data.Length, out px, out w, out h) != 0) return null;
            try
            {
                var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                var bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(new IntPtr(px.ToInt64() + (long)y * w * 4), row, 0, row.Length);
                        Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row.Length);
                    }
                }
                finally { b.UnlockBits(bd); }
                return b;
            }
            finally { ic_free(px); }
        }
        catch (Exception) { state = -1; return null; }
    }
}

// ---------- Explorer menu and autostart ----------
static class Shell
{
    const string Base = @"Software\Classes\SystemFileAssociations\image\shell\ImgConv";
    // .jxl isn't a registered "image" type on most PCs, so it gets its own entry.
    const string JxlBase = @"Software\Classes\SystemFileAssociations\.jxl\shell\ImgConv";
    const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // The "Menu" flag drives both menus: the classic one (registry verbs below)
    // and the Windows 11 one (ImgConvMenu.dll reads the flag and hides itself).
    public static bool IsOn
    {
        get
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\ImgConv"))
            {
                object v = k == null ? null : k.GetValue("Menu");
                if (v is int) return (int)v != 0;
            }
            using (var k = Registry.CurrentUser.OpenSubKey(Base)) return k != null;
        }
    }

    static void SetFlag(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\ImgConv"))
            k.SetValue("Menu", on ? 1 : 0, RegistryValueKind.DWord);
    }

    public static void On()
    {
        string exe = Application.ExecutablePath;
        Registry.CurrentUser.DeleteSubKeyTree(Base, false);
        Registry.CurrentUser.DeleteSubKeyTree(JxlBase, false);
        SetFlag(true);
        Write(Base, exe);
        if (Codecs.Available) Write(JxlBase, exe);
    }

    static void Write(string key, string exe)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(key))
        {
            k.SetValue("MUIVerb", "Convert to");
            k.SetValue("SubCommands", "");
            k.SetValue("Icon", "\"" + exe + "\",0");
            string hint = Cfg.Hint();
            for (int i = 0; i < Cfg.Formats.Length; i++)
            {
                string f = Cfg.Formats[i];
                using (var c = k.CreateSubKey(@"shell\" + i + f))
                {
                    c.SetValue("MUIVerb", f.ToUpperInvariant() + hint);
                    using (var cmd = c.CreateSubKey("command"))
                        cmd.SetValue("", "\"" + exe + "\" /to " + f + " \"%1\"");
                }
            }
        }
    }

    public static void Off()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Base, false);
        Registry.CurrentUser.DeleteSubKeyTree(JxlBase, false);
        SetFlag(false);
    }

    public static bool AutoOn
    {
        get { using (var k = Registry.CurrentUser.OpenSubKey(Run)) return k != null && k.GetValue("ImgConv") != null; }
        set
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Run))
            {
                if (value) k.SetValue("ImgConv", "\"" + Application.ExecutablePath + "\" /tray");
                else k.DeleteValue("ImgConv", false);
            }
        }
    }
}

// ---------- Windows 11 context menu (signed sparse MSIX + ImgConvMenu.dll) ----------
static class Win11Menu
{
    public const string Package = "ImgConv.ContextMenu";

    public static bool Supported
    {
        get
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                int build;
                return k != null && int.TryParse(k.GetValue("CurrentBuildNumber") as string, out build) && build >= 22000;
            }
        }
    }

    // Returns null on success, otherwise a short reason.
    public static string Register(string dir)
    {
        string msix = Path.Combine(dir, "ImgConv.msix"), cer = Path.Combine(dir, "ImgConv.cer");
        if (!File.Exists(msix) || !File.Exists(cer)) return "package files are missing";
        if (!CertTrusted(cer) && !TrustCert(cer)) return "administrator permission was not given";
        string err = PowerShell("Get-AppxPackage " + Package + " | Remove-AppxPackage; " +
                                "Add-AppxPackage -Path " + Quote(msix) + " -ExternalLocation " + Quote(dir));
        return err.Length == 0 ? null : err;
    }

    public static void Unregister() { PowerShell("Get-AppxPackage " + Package + " | Remove-AppxPackage"); }

    // Explorer keeps the DLL loaded, so it can't be overwritten or deleted — but it can be renamed.
    public static void MoveAwayDll(string dir)
    {
        string dll = Path.Combine(dir, "ImgConvMenu.dll");
        foreach (var old in Directory.GetFiles(dir, "ImgConvMenu.dll.old*"))
            try { File.Delete(old); } catch (Exception) { }
        if (!File.Exists(dll)) return;
        try { File.Move(dll, Path.Combine(Path.GetTempPath(), "ImgConvMenu-" + Guid.NewGuid().ToString("N") + ".dll")); }
        catch (Exception) { File.Move(dll, dll + ".old" + DateTime.Now.Ticks); }
    }

    static bool CertTrusted(string cer)
    {
        var c = new System.Security.Cryptography.X509Certificates.X509Certificate2(cer);
        var store = new System.Security.Cryptography.X509Certificates.X509Store(
            System.Security.Cryptography.X509Certificates.StoreName.TrustedPeople,
            System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
        store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
        try { return store.Certificates.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint, c.Thumbprint, false).Count > 0; }
        finally { store.Close(); }
    }

    // Adds the public certificate to "Trusted People" — the one step that needs admin (UAC prompt).
    static bool TrustCert(string cer)
    {
        try
        {
            using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("certutil.exe",
                "-f -addstore TrustedPeople \"" + cer + "\"")
                { Verb = "runas", UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden }))
            {
                p.WaitForExit();
                return p.ExitCode == 0;
            }
        }
        catch (Exception) { return false; } // UAC declined
    }

    static string Quote(string s) { return "'" + s.Replace("'", "''") + "'"; }

    static string PowerShell(string script)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='Stop'; " + script.Replace("\"", "\\\"") + "\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        try
        {
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd().Trim();
                p.WaitForExit();
                return p.ExitCode == 0 ? "" : (err.Length > 0 ? err : "PowerShell exit code " + p.ExitCode);
            }
        }
        catch (Exception ex) { return ex.Message; }
    }
}

// ---------- Memory ----------
static class Mem
{
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);

    public static void Trim()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        SetProcessWorkingSetSize(new IntPtr(-1), new IntPtr(-1), new IntPtr(-1));
    }
}

// ---------- Tray ----------
class TrayCtx : ApplicationContext
{
    readonly NotifyIcon tray;
    readonly SynchronizationContext ui;
    MainForm form;

    public TrayCtx(EventWaitHandle showEvent, bool showNow)
    {
        ui = new WindowsFormsSynchronizationContext();

        var menu = new ContextMenu(); // native Win32 menu — lighter than ContextMenuStrip
        var open = new MenuItem("Open ImgConv", (s, e) => ShowForm()) { DefaultItem = true };
        menu.MenuItems.AddRange(new[] { open, new MenuItem("-"),
            new MenuItem("Exit", (s, e) => { tray.Visible = false; Application.Exit(); }) });

        tray = new NotifyIcon { Icon = Art.SmallIcon, Text = "ImgConv — image converter", ContextMenu = menu, Visible = true };
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowForm(); };

        // A second launch of the exe signals us — show the window.
        ThreadPool.RegisterWaitForSingleObject(showEvent, (o, t) => ui.Post(_ => ShowForm(), null), null, -1, false);

        if (showNow) ShowForm(); else Mem.Trim();
    }

    void ShowForm()
    {
        if (form == null)
        {
            form = new MainForm();
            form.FormClosed += (s, e) => { form.Dispose(); form = null; Mem.Trim(); };
        }
        form.Show();
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tray.Dispose();
        base.Dispose(disposing);
    }
}

// ---------- Window ----------
class MainForm : Form
{
    readonly Label drop, status;
    readonly ComboBox fmt;
    readonly NumericUpDown q, kb, side;
    readonly CheckBox useKb, useSide;
    float k = 1;
    volatile bool busy;

    int S(int v) { return (int)Math.Round(v * k); }

    public MainForm()
    {
        using (var g = CreateGraphics()) k = g.DpiX / 96f;
        Text = "ImgConv";
        Icon = Art.SmallIcon;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(340), S(396));
        AllowDrop = true;

        drop = new Label
        {
            Text = "Drop images here\nor click to choose files",
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(240, 245, 255),
            Cursor = Cursors.Hand,
            Bounds = new Rectangle(S(12), S(12), S(316), S(110)),
            AllowDrop = true,
        };
        drop.Click += (s, e) => Pick();
        Controls.Add(drop);

        AddLabel("Format", 137);
        fmt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(S(190), S(134), S(138), S(24)) };
        foreach (var f in Cfg.Formats) fmt.Items.Add(f.ToUpperInvariant());
        fmt.SelectedIndex = Array.IndexOf(Cfg.Formats, Cfg.Format);
        Controls.Add(fmt);

        AddLabel("JPG quality, %", 167);
        q = Num(164, 1, 100, Cfg.Quality);

        useKb = Check("Max file size, KB", 194, Cfg.TargetKB > 0);
        kb = Num(194, 1, 10000000, Cfg.TargetKB > 0 ? Cfg.TargetKB : 490);

        useSide = Check("Max long side, px", 224, Cfg.MaxSide > 0);
        side = Num(224, 16, 100000, Cfg.MaxSide > 0 ? Cfg.MaxSide : 1920);

        status = new Label { Bounds = new Rectangle(S(12), S(256), S(316), S(20)), ForeColor = Color.DimGray,
                             Text = "Results are saved next to the originals." };
        Controls.Add(status);

        // ---- Settings ----
        Controls.Add(new Label { Bounds = new Rectangle(S(12), S(284), S(316), 1), BorderStyle = BorderStyle.Fixed3D });
        Controls.Add(new Label { Text = "Settings", AutoSize = true, Location = new Point(S(12), S(292)),
                                 Font = new Font("Segoe UI Semibold", 9f) });
        var cShell = Check("Add \"Convert to\" to the Explorer right-click menu", 312, Shell.IsOn);
        var cAsk = Check("Ask size and quality when converting from Explorer", 336, Cfg.Ask);
        var cAuto = Check("Start with Windows (in the tray)", 360, Shell.AutoOn);
        cShell.CheckedChanged += (s, e) => { if (cShell.Checked) Shell.On(); else Shell.Off(); };
        cAsk.CheckedChanged += (s, e) => { Cfg.Ask = cAsk.Checked; Cfg.Save(); };
        cAuto.CheckedChanged += (s, e) => Shell.AutoOn = cAuto.Checked;
        if (Codecs.Available)
        {
            var cJpegli = Check("Use jpegli for JPG (smaller files, same quality)", 384, Cfg.Jpegli);
            cJpegli.CheckedChanged += (s, e) => { Cfg.Jpegli = cJpegli.Checked; Cfg.Save(); };
            ClientSize = new Size(ClientSize.Width, S(420));
        }

        EventHandler sync = (s, e) => { q.Enabled = fmt.SelectedIndex == 0; kb.Enabled = useKb.Checked; side.Enabled = useSide.Checked; };
        fmt.SelectedIndexChanged += sync; useKb.CheckedChanged += sync; useSide.CheckedChanged += sync;
        sync(null, null);

        DragEventHandler enter = (s, e) => e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DragEventHandler dropped = (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null) Start(f); };
        DragEnter += enter; DragDrop += dropped;
        drop.DragEnter += enter; drop.DragDrop += dropped;

        FormClosing += (s, e) => Apply();
    }

    void AddLabel(string t, int y)
    {
        Controls.Add(new Label { Text = t, AutoSize = true, Location = new Point(S(12), S(y)) });
    }

    CheckBox Check(string t, int y, bool v)
    {
        var c = new CheckBox { Text = t, Checked = v, AutoSize = true, Location = new Point(S(12), S(y + 2)) };
        Controls.Add(c);
        return c;
    }

    NumericUpDown Num(int y, int min, int max, int v)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, v)),
                                    Bounds = new Rectangle(S(230), S(y), S(98), S(24)), TextAlign = HorizontalAlignment.Right };
        Controls.Add(n);
        return n;
    }

    void Apply()
    {
        Cfg.Format = Cfg.Formats[fmt.SelectedIndex];
        Cfg.Quality = (int)q.Value;
        Cfg.TargetKB = useKb.Checked ? (int)kb.Value : 0;
        Cfg.MaxSide = useSide.Checked ? (int)side.Value : 0;
        Cfg.Save();
    }

    void Pick()
    {
        if (busy) return;
        using (var d = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Images|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.avif;*.jxl;*.ico|All files|*.*",
        })
            if (d.ShowDialog(this) == DialogResult.OK) Start(d.FileNames);
    }

    void Start(string[] input)
    {
        if (busy) return;
        var files = new List<string>();
        foreach (var p in input)
        {
            if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p)); // folder — take all files in it
            else files.Add(p);
        }
        if (files.Count == 0) return;

        Apply();
        busy = true;
        drop.Enabled = false;
        string f = Cfg.Format; int qq = Cfg.Quality, t = Cfg.TargetKB, m = Cfg.MaxSide;

        var th = new Thread(() =>
        {
            int ok = 0;
            var errs = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                int n = i + 1;
                Ui(() => status.Text = "Converting " + n + " of " + files.Count + "…");
                try { Conv.Run(files[i], f, qq, t, m); ok++; }
                catch (Exception ex) { errs.Add(Path.GetFileName(files[i]) + ": " + ex.Message); }
            }
            Ui(() =>
            {
                busy = false;
                drop.Enabled = true;
                status.Text = "Done: " + ok + " of " + files.Count + (errs.Count > 0 ? ", failed: " + errs.Count : "");
                if (errs.Count > 0)
                    MessageBox.Show(this, string.Join("\n", errs.Count > 15 ? errs.GetRange(0, 15) : errs), "Some files failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Mem.Trim();
            });
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        th.Start();
    }

    void Ui(Action a)
    {
        try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch (InvalidOperationException) { }
    }
}

// ---------- Small "size and quality" dialog for Explorer conversions ----------
class AskForm : Form
{
    public AskForm(string fmt, int count)
    {
        float k;
        using (var g = CreateGraphics()) k = g.DpiX / 96f;
        Func<int, int> S = v => (int)Math.Round(v * k);
        bool jpg = fmt == "jpg";

        Text = "ImgConv";
        Icon = Art.SmallIcon;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(S(300), S(176));

        Controls.Add(new Label
        {
            Text = "Convert " + (count == 1 ? "1 image" : count + " images") + " to " + fmt.ToUpperInvariant(),
            AutoSize = true, Location = new Point(S(12), S(12)), Font = new Font("Segoe UI Semibold", 10f),
        });

        var useKb = new CheckBox { Text = "Max file size, KB", Checked = Cfg.TargetKB > 0, AutoSize = true, Location = new Point(S(12), S(48)) };
        var kb = new NumericUpDown { Minimum = 1, Maximum = 10000000, Value = Cfg.TargetKB > 0 ? Cfg.TargetKB : 490,
            Bounds = new Rectangle(S(190), S(46), S(98), S(24)), TextAlign = HorizontalAlignment.Right };
        useKb.CheckedChanged += (s, e) => kb.Enabled = useKb.Checked;
        kb.Enabled = useKb.Checked;

        var qLabel = new Label { Text = jpg ? "Quality, %" : "Quality (JPG only)", AutoSize = true, Location = new Point(S(12), S(81)) };
        var q = new NumericUpDown { Minimum = 1, Maximum = 100, Value = Cfg.Quality, Enabled = jpg,
            Bounds = new Rectangle(S(190), S(78), S(98), S(24)), TextAlign = HorizontalAlignment.Right };

        var noAsk = new CheckBox { Text = "Don't ask again", AutoSize = true, Location = new Point(S(12), S(146)) };

        var ok = new Button { Text = "Convert", Bounds = new Rectangle(S(196), S(110), S(92), S(28)), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Bounds = new Rectangle(S(96), S(110), S(92), S(28)), DialogResult = DialogResult.Cancel };
        Controls.AddRange(new Control[] { useKb, kb, qLabel, q, noAsk, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        ActiveControl = ok;

        FormClosing += (s, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            Cfg.TargetKB = useKb.Checked ? (int)kb.Value : 0;
            if (jpg) Cfg.Quality = (int)q.Value;
            if (noAsk.Checked) Cfg.Ask = false;
            Cfg.Save(); // remember for next time
        };
        Shown += (s, e) => Activate();
    }
}

// ---------- Icon (drawn in code, no files) ----------
static class Art
{
    static Icon small;
    public static Icon SmallIcon
    {
        get
        {
            if (small == null)
                using (var b = Draw(SystemInformation.SmallIconSize.Width)) small = Icon.FromHandle(b.GetHicon());
            return small;
        }
    }

    public static Bitmap Draw(int s)
    {
        var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc = new RectangleF(0, 0, s, s);
            float r = s * 0.22f;
            using (var p = new GraphicsPath())
            {
                p.AddArc(0, 0, 2 * r, 2 * r, 180, 90);
                p.AddArc(s - 2 * r, 0, 2 * r, 2 * r, 270, 90);
                p.AddArc(s - 2 * r, s - 2 * r, 2 * r, 2 * r, 0, 90);
                p.AddArc(0, s - 2 * r, 2 * r, 2 * r, 90, 90);
                p.CloseFigure();
                using (var br = new LinearGradientBrush(rc, Color.FromArgb(59, 130, 246), Color.FromArgb(139, 92, 246), 45f))
                    g.FillPath(br, p);
            }
            using (var w = new SolidBrush(Color.White))
            {
                g.FillPolygon(w, new[] {
                    new PointF(s * .14f, s * .80f), new PointF(s * .40f, s * .40f), new PointF(s * .58f, s * .64f),
                    new PointF(s * .69f, s * .50f), new PointF(s * .86f, s * .80f) });
                g.FillEllipse(w, s * .62f, s * .17f, s * .17f, s * .17f);
            }
        }
        return b;
    }

    // Writes an .ico (32-bit DIB frames) — used at build time for the exe icon.
    public static void WriteIco(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64 };
        var frames = new List<byte[]>();
        foreach (int s in sizes)
            using (var bmp = Draw(s))
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int mask = ((s + 31) / 32) * 4 * s;
                w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
                w.Write(0); w.Write(s * s * 4 + mask); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                for (int y = s - 1; y >= 0; y--)
                    for (int x = 0; x < s; x++) { var c = bmp.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
                w.Write(new byte[mask]);
                w.Flush();
                frames.Add(ms.ToArray());
            }
        using (var f = new BinaryWriter(File.Create(path)))
        {
            f.Write((short)0); f.Write((short)1); f.Write((short)sizes.Length);
            int off = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                f.Write((byte)sizes[i]); f.Write((byte)sizes[i]); f.Write((byte)0); f.Write((byte)0);
                f.Write((short)1); f.Write((short)32); f.Write(frames[i].Length); f.Write(off);
                off += frames[i].Length;
            }
            foreach (var fr in frames) f.Write(fr);
        }
    }
}
