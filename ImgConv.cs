// ImgConv — лёгкий конвертер изображений в трее (Windows, .NET Framework 4).
// Режимы запуска:
//   ImgConv.exe              — трей + окно
//   ImgConv.exe /tray        — только трей (для автозапуска)
//   ImgConv.exe /to jpg f..  — конвертировать файлы и выйти (из меню Проводника)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
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

        Cfg.Load();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (a.Length >= 3 && a[0] == "/to") { RunCli(a); return; }
        if (a.Length == 3 && a[0] == "/setup")  // вызывается установщиком
        {
            if (a[1] == "1") Shell.On(); else Shell.Off();
            Shell.AutoOn = a[2] == "1";
            return;
        }
        if (a.Length >= 1 && a[0] == "/uninstall") { Uninstall(Array.IndexOf(a, "/quiet") >= 0); return; }

        bool first;
        using (var mx = new Mutex(true, "ImgConv.Tray.7f3c1a", out first))
        using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, "ImgConv.Show.7f3c1a"))
        {
            if (!first) { ev.Set(); return; }   // уже запущена — просто показать окно
            bool silent = Array.IndexOf(a, "/tray") >= 0;
            Application.Run(new TrayCtx(ev, !silent));
        }
    }

    static void Uninstall(bool quiet)
    {
        if (!quiet && MessageBox.Show("Удалить ImgConv с компьютера?", "ImgConv",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        int me = System.Diagnostics.Process.GetCurrentProcess().Id;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("ImgConv"))
            if (p.Id != me) try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }

        Shell.Off();
        Shell.AutoOn = false;
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\ImgConv", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ImgConv", false);
        foreach (var sf in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
        {
            string lnk = Path.Combine(Environment.GetFolderPath(sf), "ImgConv.lnk");
            if (File.Exists(lnk)) File.Delete(lnk);
        }

        if (!quiet) MessageBox.Show("ImgConv удалён.", "ImgConv", MessageBoxButtons.OK, MessageBoxIcon.Information);

        // Папку с самим exe удаляем после выхода процесса.
        string dir = Path.GetDirectoryName(Application.ExecutablePath);
        if (string.Equals(Path.GetFileName(dir), "ImgConv", StringComparison.OrdinalIgnoreCase))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }

    static void RunCli(string[] a)
    {
        string fmt = a[1].ToLowerInvariant();
        var errs = new List<string>();
        for (int i = 2; i < a.Length; i++)
        {
            try { Conv.Run(a[i], fmt, Cfg.Quality, Cfg.TargetKB, Cfg.MaxSide); }
            catch (Exception ex) { errs.Add(Path.GetFileName(a[i]) + ": " + ex.Message); }
        }
        if (errs.Count > 0)
            MessageBox.Show(string.Join("\n", errs), "ImgConv — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

// ---------- Настройки (HKCU\Software\ImgConv) ----------
static class Cfg
{
    const string Key = @"Software\ImgConv";
    public static readonly string[] Formats = { "jpg", "png", "bmp", "gif", "tiff" };
    public static string Format = "jpg";
    public static int Quality = 90;
    public static int TargetKB = 0;  // 0 = без ограничения
    public static int MaxSide = 0;   // 0 = исходный размер

    public static void Load()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Key))
        {
            if (k == null) return;
            var f = k.GetValue("Format") as string;
            if (f != null && Array.IndexOf(Formats, f) >= 0) Format = f;
            Quality = Get(k, "Quality", 1, 100, Quality);
            TargetKB = Get(k, "TargetKB", 0, 10000000, 0);
            MaxSide = Get(k, "MaxSide", 0, 100000, 0);
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
        }
        if (Shell.IsOn) Shell.On(); // обновить подписи в меню Проводника
    }

    public static string Hint()
    {
        var p = new List<string>();
        if (TargetKB > 0) p.Add("≤ " + TargetKB + " КБ");
        if (MaxSide > 0) p.Add("≤ " + MaxSide + " px");
        return p.Count == 0 ? "" : "  (" + string.Join(", ", p) + ")";
    }
}

// ---------- Конвертация ----------
static class Conv
{
    public static string Run(string src, string fmt, int quality, int targetKB, int maxSide)
    {
        if (Array.IndexOf(Cfg.Formats, fmt) < 0) throw new Exception("неизвестный формат " + fmt);
        Bitmap img = Load(src);
        try
        {
            int big = Math.Max(img.Width, img.Height);
            if (maxSide > 0 && big > maxSide)
                Swap(ref img, Resize(img, (int)Math.Round(img.Width * (double)maxSide / big), (int)Math.Round(img.Height * (double)maxSide / big)));
            if (fmt == "jpg") Swap(ref img, Flatten(img)); // JPG без прозрачности — подложить белый фон

            byte[] data = Encode(img, fmt, quality, targetKB * 1024L);
            string dst = OutPath(src, fmt);
            File.WriteAllBytes(dst, data);
            return dst;
        }
        finally { img.Dispose(); }
    }

    static void Swap(ref Bitmap a, Bitmap b) { a.Dispose(); a = b; }

    // Подбирает качество (JPG) и при необходимости уменьшает картинку, чтобы влезть в target байт.
    static byte[] Encode(Bitmap src, string fmt, int q, long target)
    {
        byte[] d = Save(src, fmt, q);
        if (target <= 0 || d.Length <= target) return d;

        bool lossy = fmt == "jpg";
        const int QMIN = 30; // ниже — заметные артефакты, лучше уменьшить размер
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
                if (w < 8 || h < 8) return d; // меньше уже некуда
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
            ia.SetWrapMode(WrapMode.TileFlipXY); // без тёмной каймы по краям
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
        byte[] bytes = File.ReadAllBytes(path); // читаем целиком — файл не блокируется
        Bitmap bmp;
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
            // WebP / HEIC / AVIF — через системные кодеки Windows (WIC)
            try { bmp = LoadWic(bytes); }
            catch (Exception) { throw new Exception("формат не поддерживается"); }
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

    // Отдельный метод, чтобы библиотеки WPF грузились в память только когда реально нужны.
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

// ---------- Меню Проводника и автозапуск ----------
static class Shell
{
    const string Base = @"Software\Classes\SystemFileAssociations\image\shell\ImgConv";
    const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsOn { get { using (var k = Registry.CurrentUser.OpenSubKey(Base)) return k != null; } }

    public static void On()
    {
        string exe = Application.ExecutablePath;
        Off();
        using (var k = Registry.CurrentUser.CreateSubKey(Base))
        {
            k.SetValue("MUIVerb", "Конвертировать в");
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

    public static void Off() { Registry.CurrentUser.DeleteSubKeyTree(Base, false); }

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

// ---------- Память ----------
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

// ---------- Трей ----------
class TrayCtx : ApplicationContext
{
    readonly NotifyIcon tray;
    readonly SynchronizationContext ui;
    MainForm form;

    public TrayCtx(EventWaitHandle showEvent, bool showNow)
    {
        ui = new WindowsFormsSynchronizationContext();

        var menu = new ContextMenu(); // нативное меню Win32 — легче ContextMenuStrip
        var open = new MenuItem("Открыть конвертер", (s, e) => ShowForm()) { DefaultItem = true };
        var shell = new MenuItem("Пункт в меню Проводника");
        shell.Click += (s, e) => { if (Shell.IsOn) Shell.Off(); else Shell.On(); };
        var auto = new MenuItem("Запускать с Windows");
        auto.Click += (s, e) => Shell.AutoOn = !Shell.AutoOn;
        menu.Popup += (s, e) => { shell.Checked = Shell.IsOn; auto.Checked = Shell.AutoOn; };
        menu.MenuItems.AddRange(new[] { open, new MenuItem("-"), shell, auto, new MenuItem("-"),
            new MenuItem("Выход", (s, e) => { tray.Visible = false; Application.Exit(); }) });

        tray = new NotifyIcon { Icon = Art.SmallIcon, Text = "ImgConv — конвертер фото", ContextMenu = menu, Visible = true };
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowForm(); };

        // Второй запуск exe посылает сигнал — показываем окно.
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

// ---------- Окно ----------
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
        ClientSize = new Size(S(340), S(300));
        AllowDrop = true;

        drop = new Label
        {
            Text = "Перетащите сюда фото\nили нажмите, чтобы выбрать",
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(240, 245, 255),
            Cursor = Cursors.Hand,
            Bounds = new Rectangle(S(12), S(12), S(316), S(110)),
            AllowDrop = true,
        };
        drop.Click += (s, e) => Pick();
        Controls.Add(drop);

        AddLabel("Формат", 137);
        fmt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(S(190), S(134), S(138), S(24)) };
        foreach (var f in Cfg.Formats) fmt.Items.Add(f.ToUpperInvariant());
        fmt.SelectedIndex = Array.IndexOf(Cfg.Formats, Cfg.Format);
        Controls.Add(fmt);

        AddLabel("Качество JPG, %", 167);
        q = Num(164, 1, 100, Cfg.Quality);

        useKb = Check("Размер файла не больше, КБ", 194, Cfg.TargetKB > 0);
        kb = Num(194, 1, 10000000, Cfg.TargetKB > 0 ? Cfg.TargetKB : 500);

        useSide = Check("Длинная сторона не больше, px", 224, Cfg.MaxSide > 0);
        side = Num(224, 16, 100000, Cfg.MaxSide > 0 ? Cfg.MaxSide : 1920);

        status = new Label { Bounds = new Rectangle(S(12), S(256), S(316), S(36)), ForeColor = Color.DimGray,
                             Text = "Файлы сохраняются рядом с исходными." };
        Controls.Add(status);

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
            Filter = "Изображения|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.avif;*.ico|Все файлы|*.*",
        })
            if (d.ShowDialog(this) == DialogResult.OK) Start(d.FileNames);
    }

    void Start(string[] input)
    {
        if (busy) return;
        var files = new List<string>();
        foreach (var p in input)
        {
            if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p)); // папка — все файлы из неё
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
                Ui(() => status.Text = "Обработка " + n + " из " + files.Count + "…");
                try { Conv.Run(files[i], f, qq, t, m); ok++; }
                catch (Exception ex) { errs.Add(Path.GetFileName(files[i]) + ": " + ex.Message); }
            }
            Ui(() =>
            {
                busy = false;
                drop.Enabled = true;
                status.Text = "Готово: " + ok + " из " + files.Count + (errs.Count > 0 ? ", ошибок: " + errs.Count : "");
                if (errs.Count > 0)
                    MessageBox.Show(this, string.Join("\n", errs.Count > 15 ? errs.GetRange(0, 15) : errs), "Не удалось", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

// ---------- Иконка (рисуется кодом, без файлов) ----------
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

    // Пишет .ico (32-битные DIB-кадры) — используется при сборке для иконки exe.
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
