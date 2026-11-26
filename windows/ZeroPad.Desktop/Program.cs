using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ZeroPad.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new CompanionForm());
    }
}

internal sealed class CompanionForm : Form
{
    private readonly Color background = Color.FromArgb(10, 15, 27);
    private readonly Color panel = Color.FromArgb(27, 37, 56);
    private readonly Color border = Color.FromArgb(63, 83, 113);
    private readonly Color text = Color.FromArgb(240, 245, 255);
    private readonly Color muted = Color.FromArgb(159, 174, 198);
    private readonly Label status = new();
    private readonly TextBox pairingKey = new();
    private readonly Label ipLabel = new();
    private readonly RichTextBox log = new();
    private Process? server;
    private bool stopping;

    public CompanionForm()
    {
        Text = "ZeroPad · Windows Companion";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 680);
        Size = new Size(760, 790);
        BackColor = background;
        ForeColor = text;
        Font = new Font("Segoe UI", 10f);
        BuildLayout();
        FormClosed += (_, _) => StopServer();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 7, BackColor = background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = background };
        var brand = new Label { Text = "◈  ZEROPAD", Font = new Font("Segoe UI Semibold", 22f), ForeColor = text, AutoSize = true, Location = new Point(0, 2) };
        var subtitle = new Label { Text = "WINDOWS COMPANION  ·  PHONE GAMEPAD", Font = new Font("Segoe UI", 9f), ForeColor = muted, AutoSize = true, Location = new Point(2, 42) };
        header.Controls.Add(brand); header.Controls.Add(subtitle);
        var themeBadge = new Label { Text = "GLASS UI", ForeColor = Color.FromArgb(135, 185, 255), BackColor = panel, AutoSize = true, Padding = new Padding(12, 7, 12, 7), Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(560, 12) };
        header.Controls.Add(themeBadge);
        root.Controls.Add(header, 0, 0);

        var statusCard = Card();
        statusCard.Controls.Add(new Label { Text = "COMPANION STATUS", ForeColor = muted, Font = new Font("Segoe UI Semibold", 9f), AutoSize = true, Location = new Point(17, 12) });
        status.Text = "●  Stopped";
        status.ForeColor = Color.FromArgb(255, 190, 100);
        status.Font = new Font("Segoe UI Semibold", 16f);
        status.AutoSize = true; status.Location = new Point(17, 38);
        statusCard.Controls.Add(status);
        root.Controls.Add(statusCard, 0, 1);

        var pairCard = Card();
        pairCard.Controls.Add(new Label { Text = "PAIRING KEY", ForeColor = muted, Font = new Font("Segoe UI Semibold", 9f), AutoSize = true, Location = new Point(17, 12) });
        pairingKey.ReadOnly = true; pairingKey.BorderStyle = BorderStyle.FixedSingle; pairingKey.BackColor = Color.FromArgb(17, 25, 39); pairingKey.ForeColor = text;
        pairingKey.Font = new Font("Consolas", 10f); pairingKey.Location = new Point(17, 39); pairingKey.Width = 500; pairingKey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pairCard.Controls.Add(pairingKey);
        var copy = Button("Copy key", true); copy.Location = new Point(530, 37); copy.Anchor = AnchorStyles.Top | AnchorStyles.Right; copy.Width = 100; copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(pairingKey.Text)) Clipboard.SetText(pairingKey.Text); };
        pairCard.Controls.Add(copy);
        var keyNote = new Label { Text = "Paste this key into ZeroPad on your Android phone. Keep it private.", ForeColor = muted, AutoSize = true, Location = new Point(17, 77) };
        pairCard.Controls.Add(keyNote);
        root.Controls.Add(pairCard, 0, 2);

        var networkCard = Card();
        networkCard.Controls.Add(new Label { Text = "NETWORK", ForeColor = muted, Font = new Font("Segoe UI Semibold", 9f), AutoSize = true, Location = new Point(17, 12) });
        ipLabel.Text = "PC IP: " + GetLocalIp();
        ipLabel.ForeColor = text; ipLabel.Font = new Font("Segoe UI Semibold", 13f); ipLabel.AutoSize = true; ipLabel.Location = new Point(17, 38);
        networkCard.Controls.Add(ipLabel);
        networkCard.Controls.Add(new Label { Text = "Wi-Fi UDP 26760     ·     USB / ADB TCP 26761", ForeColor = muted, AutoSize = true, Location = new Point(17, 70) });
        root.Controls.Add(networkCard, 0, 3);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = background, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0) };
        var start = Button("▶  Start companion", true); start.Width = 190; start.Click += (_, _) => StartServer();
        var stop = Button("■  Stop", false); stop.Width = 110; stop.Click += (_, _) => StopServer();
        var refresh = Button("↻  Refresh IP", false); refresh.Width = 130; refresh.Click += (_, _) => ipLabel.Text = "PC IP: " + GetLocalIp();
        actions.Controls.Add(start); actions.Controls.Add(stop); actions.Controls.Add(refresh);
        root.Controls.Add(actions, 0, 4);

        var logCard = Card();
        logCard.Controls.Add(new Label { Text = "DIAGNOSTICS", ForeColor = muted, Font = new Font("Segoe UI Semibold", 9f), AutoSize = true, Location = new Point(17, 12) });
        log.Dock = DockStyle.Fill; log.BackColor = Color.FromArgb(14, 21, 33); log.ForeColor = Color.FromArgb(194, 211, 234); log.BorderStyle = BorderStyle.None; log.Font = new Font("Consolas", 9f);
        log.Location = new Point(17, 38); log.Size = new Size(600, 200); log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logCard.Padding = new Padding(16, 36, 16, 12); logCard.Controls.Add(log);
        root.Controls.Add(logCard, 0, 5);

        var footer = new Label { Text = "Use a trusted private network · Do not expose the controller port to the internet", ForeColor = muted, Font = new Font("Segoe UI", 8.5f), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
        root.Controls.Add(footer, 0, 6);
        AppendLog("Ready. Start the companion, then copy the pairing key to Android.");
    }

    private Panel Card() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = panel,
        Padding = new Padding(0),
        Margin = new Padding(0, 0, 0, 8)
    };

    private Button Button(string label, bool primary)
    {
        var b = new Button { Text = label, FlatStyle = FlatStyle.Flat, Height = 38, Width = 120, BackColor = primary ? Color.FromArgb(82, 139, 255) : Color.FromArgb(35, 48, 69), ForeColor = text, Font = new Font("Segoe UI Semibold", 9.5f), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
        b.FlatAppearance.BorderColor = primary ? Color.FromArgb(110, 160, 255) : border;
        b.FlatAppearance.BorderSize = 1;
        return b;
    }

    private void StartServer()
    {
        if (server is { HasExited: false }) return;
        var path = Path.Combine(AppContext.BaseDirectory, "server", "ZeroPad.Server.exe");
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "The server executable was not found. Keep the 'server' folder beside ZeroPad.Desktop.exe.", "Server missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            AppendLog("ERROR: missing " + path);
            return;
        }
        try
        {
            stopping = false;
            server = new Process { StartInfo = new ProcessStartInfo(path) { WorkingDirectory = Path.GetDirectoryName(path)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 }, EnableRaisingEvents = true };
            server.OutputDataReceived += (_, e) => { if (e.Data is not null) { AppendLog(e.Data); const string prefix = "Pairing key (enter this in Android): "; if (e.Data.StartsWith(prefix, StringComparison.Ordinal)) SetKey(e.Data[prefix.Length..].Trim()); } };
            server.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog("ERR " + e.Data); };
            server.Exited += (_, _) => { if (!stopping) SetStatus("●  Server exited", Color.FromArgb(255, 190, 100)); };
            server.Start(); server.BeginOutputReadLine(); server.BeginErrorReadLine();
            SetStatus("●  Running", Color.FromArgb(92, 225, 166));
            AppendLog("Server process started.");
        }
        catch (Exception ex) { AppendLog("ERROR: " + ex.Message); SetStatus("●  Start failed", Color.FromArgb(255, 130, 130)); }
    }

    private void StopServer()
    {
        stopping = true;
        try { if (server is { HasExited: false }) { server.Kill(entireProcessTree: true); server.WaitForExit(1500); } } catch { }
        SetStatus("●  Stopped", Color.FromArgb(255, 190, 100));
    }

    private void SetKey(string value)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => SetKey(value))); return; }
        pairingKey.Text = value;
    }

    private void SetStatus(string value, Color color)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(value, color))); return; }
        status.Text = value; status.ForeColor = color;
    }

    private void AppendLog(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(line))); return; }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        log.SelectionStart = log.TextLength; log.ScrollToCaret();
    }

    private static string GetLocalIp()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(address.Address) && !address.Address.ToString().StartsWith("169.254."))
                    return address.Address.ToString();
        return "Connect to Wi-Fi";
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(10, 15, 27), Color.FromArgb(23, 37, 61), 35f);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}
