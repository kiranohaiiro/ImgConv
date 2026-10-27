// Установщик ImgConv. Внутри лежит ImgConv.exe (как ресурс).
// Ставит в %LOCALAPPDATA%\Programs\ImgConv — права администратора не нужны.
// Тихая установка: ImgConv-Setup.exe /S
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

static class SetupApp
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static int Main(string[] a)
    {
        SetProcessDPIAware();
        if (a.Length > 0 && a[0].ToUpperInvariant() == "/S")
        {
            try { Installer.Run(Installer.DefaultDir, true, false, true, true); return 0; }
            catch (Exception) { return 1; }
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }
}

static class Installer
{
    public const string Version = "1.0";
    const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ImgConv";

    public static string DefaultDir
    {
        get
        {
            string cur = null;
            using (var k = Registry.CurrentUser.OpenSubKey(UninstKey))
                if (k != null) cur = k.GetValue("InstallLocation") as string;
            return !string.IsNullOrEmpty(cur) ? cur
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ImgConv");
        }
    }

    public static bool Installed { get { using (var k = Registry.CurrentUser.OpenSubKey(UninstKey)) return k != null; } }

    public static string Run(string dir, bool startMenu, bool desktop, bool shellMenu, bool autorun)
    {
        dir = Path.GetFullPath(dir);
        if (!string.Equals(Path.GetFileName(dir), "ImgConv", StringComparison.OrdinalIgnoreCase))
            dir = Path.Combine(dir, "ImgConv");

        // Закрыть запущенную копию, чтобы можно было заменить файл.
        foreach (var p in Process.GetProcessesByName("ImgConv"))
            try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }

        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "ImgConv.exe");
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ImgConv.exe"))
        using (var f = File.Create(exe)) s.CopyTo(f);

        Shortcut(Environment.SpecialFolder.Programs, exe, startMenu);
        Shortcut(Environment.SpecialFolder.DesktopDirectory, exe, desktop);

        using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
        {
            k.SetValue("DisplayName", "ImgConv — конвертер фото");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "ImgConv");
            k.SetValue("DisplayIcon", "\"" + exe + "\",0");
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", "\"" + exe + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + exe + "\" /uninstall /quiet");
            k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024) + 1, RegistryValueKind.DWord);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        // Меню Проводника и автозапуск прописывает сама программа — с правильным путём.
        using (var p = Process.Start(new ProcessStartInfo(exe, "/setup " + (shellMenu ? "1" : "0") + " " + (autorun ? "1" : "0"))
               { UseShellExecute = false }))
            p.WaitForExit(15000);
        return exe;
    }

    static void Shortcut(Environment.SpecialFolder where, string exe, bool create)
    {
        string lnk = Path.Combine(Environment.GetFolderPath(where), "ImgConv.lnk");
        if (!create) { if (File.Exists(lnk)) File.Delete(lnk); return; }

        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object sh = Activator.CreateInstance(t), sc = null;
        try
        {
            sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Конвертер фото" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
        finally
        {
            if (sc != null) Marshal.FinalReleaseComObject(sc);
            Marshal.FinalReleaseComObject(sh);
        }
    }
}

class SetupForm : Form
{
    readonly float k;
    readonly TextBox path;
    readonly Button browse, go, cancel;
    readonly CheckBox cStart, cDesk, cShell, cAuto, cRun;
    readonly Label pathLabel, doneText, sub;
    string installedExe;

    int S(int v) { return (int)Math.Round(v * k); }

    public SetupForm()
    {
        using (var g = CreateGraphics()) k = g.DpiX / 96f;
        bool update = Installer.Installed;

        Text = "Установка ImgConv";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(460), S(330));
        BackColor = Color.FromArgb(248, 249, 251);
        using (var b = Art.Draw(SystemInformation.SmallIconSize.Width)) Icon = Icon.FromHandle(b.GetHicon());

        // Шапка
        var head = new Panel { Bounds = new Rectangle(0, 0, S(460), S(72)), BackColor = Color.White };
        head.Paint += (s, e) => e.Graphics.DrawLine(Pens.Gainsboro, 0, head.Height - 1, head.Width, head.Height - 1);
        head.Controls.Add(new PictureBox { Image = Art.Draw(S(44)), Bounds = new Rectangle(S(18), S(14), S(44), S(44)) });
        head.Controls.Add(new Label { Text = update ? "Обновление ImgConv" : "Установка ImgConv", AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13f), Location = new Point(S(74), S(12)) });
        sub = new Label { Text = "Лёгкий конвертер фото, который живёт в трее", AutoSize = true,
            ForeColor = Color.DimGray, Location = new Point(S(76), S(42)) };
        head.Controls.Add(sub);
        Controls.Add(head);

        pathLabel = new Label { Text = "Папка установки", AutoSize = true, Location = new Point(S(18), S(88)) };
        Controls.Add(pathLabel);
        path = new TextBox { Text = Installer.DefaultDir, Bounds = new Rectangle(S(18), S(109), S(330), S(24)) };
        Controls.Add(path);
        browse = new Button { Text = "Обзор…", Bounds = new Rectangle(S(356), S(107), S(86), S(27)) };
        browse.Click += (s, e) => Browse();
        Controls.Add(browse);

        cStart = Check("Ярлык в меню «Пуск»", 150, true);
        cDesk  = Check("Ярлык на рабочем столе", 175, false);
        cShell = Check("Пункт «Конвертировать в» в меню Проводника", 200, true);
        cAuto  = Check("Запускать вместе с Windows (в трее)", 225, true);

        doneText = new Label { Bounds = new Rectangle(S(18), S(92), S(424), S(110)), Visible = false,
            Text = "ImgConv установлен.\n\nИконка появится в трее рядом с часами. Нажмите на неё, чтобы открыть конвертер.\n" +
                   "Удалить программу можно в «Параметры → Приложения»." };
        Controls.Add(doneText);
        cRun = Check("Запустить ImgConv", 210, true);
        cRun.Visible = false;

        go = new Button { Text = update ? "Обновить" : "Установить", Bounds = new Rectangle(S(342), S(284), S(100), S(30)) };
        go.Click += (s, e) => Go();
        cancel = new Button { Text = "Отмена", Bounds = new Rectangle(S(234), S(284), S(100), S(30)), DialogResult = DialogResult.Cancel };
        Controls.Add(go);
        Controls.Add(cancel);
        AcceptButton = go;
        CancelButton = cancel;
        ActiveControl = go;
    }

    CheckBox Check(string t, int y, bool v)
    {
        var c = new CheckBox { Text = t, Checked = v, AutoSize = true, Location = new Point(S(18), S(y)) };
        Controls.Add(c);
        return c;
    }

    void Browse()
    {
        using (var d = new FolderBrowserDialog { Description = "Куда установить ImgConv (будет создана папка ImgConv)" })
            if (d.ShowDialog(this) == DialogResult.OK)
                path.Text = Path.Combine(d.SelectedPath, "ImgConv");
    }

    void Go()
    {
        if (installedExe != null)   // экран «Готово»
        {
            if (cRun.Checked) Process.Start(new ProcessStartInfo(installedExe) { WorkingDirectory = Path.GetDirectoryName(installedExe) });
            Close();
            return;
        }

        go.Enabled = cancel.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            installedExe = Installer.Run(path.Text.Trim(), cStart.Checked, cDesk.Checked, cShell.Checked, cAuto.Checked);
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            go.Enabled = cancel.Enabled = true;
            MessageBox.Show(this, "Не удалось установить:\n" + ex.Message, "ImgConv", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Cursor = Cursors.Default;

        foreach (Control c in new Control[] { pathLabel, path, browse, cStart, cDesk, cShell, cAuto, cancel }) c.Visible = false;
        doneText.Visible = cRun.Visible = true;
        sub.Text = "Готово";
        go.Text = "Готово";
        go.Enabled = true;
    }
}
