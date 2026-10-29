// ImgConv installer. ImgConv.exe is embedded as a resource.
// Installs to %LOCALAPPDATA%\Programs\ImgConv — no admin rights needed.
// Silent install: ImgConv-Setup.exe /S
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
            string note;
            try { Installer.Run(Installer.DefaultDir, true, false, true, true, Installer.CanWin11, out note); return 0; }
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
    public const string Version = "1.2";
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

    // The Windows 11 menu parts are bundled only when build.ps1 found the C++ compiler and Windows SDK.
    static bool HasResource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceInfo(name) != null; }
    public static bool CanWin11 { get { return Win11Menu.Supported && HasResource("ImgConv.msix"); } }

    static void Extract(string name, string dir)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var z = new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress))
        using (var f = File.Create(Path.Combine(dir, name))) z.CopyTo(f);
    }

    public static string Run(string dir, bool startMenu, bool desktop, bool shellMenu, bool autorun, bool win11, out string win11Note)
    {
        win11Note = null;
        dir = Path.GetFullPath(dir);
        if (!string.Equals(Path.GetFileName(dir), "ImgConv", StringComparison.OrdinalIgnoreCase))
            dir = Path.Combine(dir, "ImgConv");

        // Close a running copy so the file can be replaced.
        foreach (var p in Process.GetProcessesByName("ImgConv"))
            try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }

        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "ImgConv.exe");
        Extract("ImgConv.exe", dir);
        if (HasResource("ImgConvCodecs.dll")) // jpegli + JPEG XL, with the licenses it requires
        {
            Extract("ImgConvCodecs.dll", dir);
            Extract("THIRD-PARTY-NOTICES.txt", dir);
        }
        if (HasResource("ImgConv.msix"))
        {
            Win11Menu.MoveAwayDll(dir); // Explorer may have the old one loaded
            foreach (var r in new[] { "ImgConvMenu.dll", "ImgConv.msix", "ImgConv.cer" }) Extract(r, dir);
        }

        Shortcut(Environment.SpecialFolder.Programs, exe, startMenu);
        Shortcut(Environment.SpecialFolder.DesktopDirectory, exe, desktop);

        using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
        {
            k.SetValue("DisplayName", "ImgConv — image converter");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "ImgConv");
            k.SetValue("DisplayIcon", "\"" + exe + "\",0");
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", "\"" + exe + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + exe + "\" /uninstall /quiet");
            long size = 0;
            foreach (var f in Directory.GetFiles(dir)) size += new FileInfo(f).Length;
            k.SetValue("EstimatedSize", (int)(size / 1024) + 1, RegistryValueKind.DWord);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        // The app itself registers the Explorer menu and autostart, so paths are correct.
        using (var p = Process.Start(new ProcessStartInfo(exe, "/setup " + (shellMenu ? "1" : "0") + " " + (autorun ? "1" : "0"))
               { UseShellExecute = false }))
            p.WaitForExit(15000);

        if (win11) win11Note = Win11Menu.Register(dir);
        else if (HasResource("ImgConv.msix")) Win11Menu.Unregister();
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
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Image converter" });
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
    readonly CheckBox cStart, cDesk, cShell, cAuto, cWin11, cRun;
    readonly Label pathLabel, doneText, sub;
    string installedExe;

    int S(int v) { return (int)Math.Round(v * k); }

    public SetupForm()
    {
        using (var g = CreateGraphics()) k = g.DpiX / 96f;
        bool update = Installer.Installed;

        Text = "ImgConv Setup";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(460), S(340));
        BackColor = Color.FromArgb(248, 249, 251);
        using (var b = Art.Draw(SystemInformation.SmallIconSize.Width)) Icon = Icon.FromHandle(b.GetHicon());

        // Header
        var head = new Panel { Bounds = new Rectangle(0, 0, S(460), S(72)), BackColor = Color.White };
        head.Paint += (s, e) => e.Graphics.DrawLine(Pens.Gainsboro, 0, head.Height - 1, head.Width, head.Height - 1);
        head.Controls.Add(new PictureBox { Image = Art.Draw(S(44)), Bounds = new Rectangle(S(18), S(14), S(44), S(44)) });
        head.Controls.Add(new Label { Text = update ? "Update ImgConv" : "Install ImgConv", AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13f), Location = new Point(S(74), S(12)) });
        sub = new Label { Text = "A tiny image converter that lives in your tray", AutoSize = true,
            ForeColor = Color.DimGray, Location = new Point(S(76), S(42)) };
        head.Controls.Add(sub);
        Controls.Add(head);

        pathLabel = new Label { Text = "Install folder", AutoSize = true, Location = new Point(S(18), S(88)) };
        Controls.Add(pathLabel);
        path = new TextBox { Text = Installer.DefaultDir, Bounds = new Rectangle(S(18), S(109), S(330), S(24)) };
        Controls.Add(path);
        browse = new Button { Text = "Browse…", Bounds = new Rectangle(S(356), S(107), S(86), S(27)) };
        browse.Click += (s, e) => Browse();
        Controls.Add(browse);

        cStart = Check("Start menu shortcut", 150, true);
        cDesk  = Check("Desktop shortcut", 175, false);
        cShell = Check("Add \"Convert to\" to the Explorer right-click menu", 200, true);
        cAuto  = Check("Start with Windows (in the tray)", 225, true);
        cWin11 = Check("Also add it to the new Windows 11 menu (asks for admin once)", 250, Installer.CanWin11);
        cWin11.Visible = Installer.CanWin11;
        cShell.CheckedChanged += (s, e) => { cWin11.Enabled = cShell.Checked; if (!cShell.Checked) cWin11.Checked = false; };

        doneText = new Label { Bounds = new Rectangle(S(18), S(92), S(424), S(140)), Visible = false,
            Text = "ImgConv is installed.\n\nLook for its icon in the tray next to the clock. Click it to open the converter.\n" +
                   "You can uninstall it any time from Settings → Apps." };
        Controls.Add(doneText);
        cRun = Check("Launch ImgConv", 240, true);
        cRun.Visible = false;

        go = new Button { Text = update ? "Update" : "Install", Bounds = new Rectangle(S(342), S(294), S(100), S(30)) };
        go.Click += (s, e) => Go();
        cancel = new Button { Text = "Cancel", Bounds = new Rectangle(S(234), S(294), S(100), S(30)), DialogResult = DialogResult.Cancel };
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
        using (var d = new FolderBrowserDialog { Description = "Where to install ImgConv (an ImgConv folder will be created)" })
            if (d.ShowDialog(this) == DialogResult.OK)
                path.Text = Path.Combine(d.SelectedPath, "ImgConv");
    }

    void Go()
    {
        if (installedExe != null)   // "Finish" screen
        {
            if (cRun.Checked) Process.Start(new ProcessStartInfo(installedExe) { WorkingDirectory = Path.GetDirectoryName(installedExe) });
            Close();
            return;
        }

        go.Enabled = cancel.Enabled = false;
        Cursor = Cursors.WaitCursor;
        string note;
        try
        {
            installedExe = Installer.Run(path.Text.Trim(), cStart.Checked, cDesk.Checked, cShell.Checked, cAuto.Checked,
                                         cWin11.Visible && cWin11.Checked, out note);
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            go.Enabled = cancel.Enabled = true;
            MessageBox.Show(this, "Installation failed:\n" + ex.Message, "ImgConv", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Cursor = Cursors.Default;

        if (note != null)
        {
            string first = note.Split('\n')[0].Trim();
            doneText.Text += "\n\nThe new Windows 11 menu wasn't set up (" + (first.Length > 120 ? first.Substring(0, 120) + "…" : first) +
                             "). \"Convert to\" is still under Show more options.";
        }
        foreach (Control c in new Control[] { pathLabel, path, browse, cStart, cDesk, cShell, cAuto, cWin11, cancel }) c.Visible = false;
        doneText.Visible = cRun.Visible = true;
        sub.Text = "All done";
        go.Text = "Finish";
        go.Enabled = true;
    }
}
