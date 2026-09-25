using System.Drawing;
using System.Windows.Forms;
using InstallerBuilder.Core;

namespace InstallerBuilder.App;

/// <summary>"Készítés" fül: 1. Windows forrása, 2. mit készítsen, a folyamat lépésenként + napló.</summary>
public sealed class BuildPage : UserControl
{
    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }

    private readonly RadioButton _srcDownload = new() { Text = "Friss letöltés – a legújabb Windows a Microsoft hivatalos szerveréről", AutoSize = true, Checked = true };
    private readonly RadioButton _srcIso = new() { Text = "Meglévő ISO fájl", AutoSize = true };
    private readonly RadioButton _srcUsb = new() { Text = "Kész telepítő pendrive kiegészítése (Media Creation Tool / Rufus)", AutoSize = true };
    private readonly ComboBox _version = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _isoPath = new();
    private readonly Panel _downloadPanel = new() { AutoSize = true };
    private readonly Panel _isoPanel = new() { AutoSize = true };

    private readonly RadioButton _tgtUsb = new() { Text = "Bootolható pendrive – formázza és elkészíti a nulláról", AutoSize = true, Checked = true };
    private readonly RadioButton _tgtIso = new() { Text = "Új ISO fájl (VM-hez vagy későbbre – Windows ADK kell)", AutoSize = true };
    private readonly RadioButton _tgtFolder = new() { Text = "Csak a kiegészítő fájlok egy mappába (kézi másoláshoz)", AutoSize = true };
    private readonly ComboBox _disk = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _existingDrive = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _outputIso = new();
    private readonly TextBox _oscdimg = new();
    private readonly TextBox _outputFolder = new();
    private readonly Panel _usbPanel = new() { AutoSize = true };
    private readonly Panel _existingPanel = new() { AutoSize = true };
    private readonly Panel _isoOutPanel = new() { AutoSize = true };
    private readonly Panel _folderPanel = new() { AutoSize = true };
    private readonly Label _formatWarning;
    private readonly FlowLayoutPanel _checks = Theme.Column();
    private readonly Button _start = Theme.PrimaryButton("Pendrive elkészítése");
    private readonly Button _cancel = Theme.Button("Megszakítás");

    private readonly ListView _steps = new();
    private readonly ProgressBar _overall = new();
    private readonly Label _overallText = new() { AutoSize = true, ForeColor = Theme.TextMuted };
    private readonly TextBox _log = new();

    private readonly Func<BuildProfile> _profile;
    private readonly Func<string?> _preflight;
    private CancellationTokenSource? _cts;
    private List<DiskInfo> _disks = new();

    public BuildPage(Func<BuildProfile> profile, Func<string?> preflight)
    {
        _profile = profile;
        _preflight = preflight;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 600));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        const int w = 560;
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        // ---- 1. forrás ----
        left.Controls.Add(Theme.Heading2("1. Windows forrása"));
        left.Controls.Add(_srcDownload);
        left.Controls.Add(_srcIso);
        left.Controls.Add(_srcUsb);
        _version.Items.Add(new Choice<WindowsVersion>(WindowsVersion.Windows11, "Windows 11 (legújabb)"));
        _version.Items.Add(new Choice<WindowsVersion>(WindowsVersion.Windows10, "Windows 10 (22H2)"));
        _version.SelectedIndex = 0;
        foreach (var (value, text) in new[] { ("Hungarian", "Magyar"), ("English International", "Angol (nemzetközi)"), ("English", "Angol (USA)"), ("German", "Német"), ("Slovak", "Szlovák"), ("Romanian", "Román") })
        {
            _language.Items.Add(new Choice<string>(value, text));
        }
        _language.SelectedIndex = 0;
        var dl = Theme.Column();
        var dlRow = Theme.Row();
        dlRow.Controls.Add(Theme.Field("Verzió", _version, 220));
        dlRow.Controls.Add(Theme.Field("Nyelv", _language, 180));
        dlRow.Controls.Add(Theme.Field("Architektúra", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Items = { "x64 (64 bites)" }, SelectedIndex = 0, Enabled = false }, 130));
        dl.Controls.Add(dlRow);
        dl.Controls.Add(Theme.Muted($"Kb. 5–7 GB. Az ISO ide kerül, és a következő készítésnél újra felhasználható: {WindowsTools.DownloadsFolder()}. " +
            "Ha a Microsoft épp nem engedi az automatikus letöltést, megnyitjuk a letöltőoldalt.", w));
        _downloadPanel.Controls.Add(dl);
        left.Controls.Add(_downloadPanel);
        _isoPanel.Controls.Add(PathRow("ISO fájl", _isoPath, w, () =>
        {
            using var open = new OpenFileDialog { Filter = "ISO fájl (*.iso)|*.iso", Title = "Windows ISO kiválasztása" };
            return open.ShowDialog(this) == DialogResult.OK ? open.FileName : null;
        }));
        left.Controls.Add(_isoPanel);

        // ---- 2. cél ----
        left.Controls.Add(new Label { Height = 1, Width = w, BackColor = Theme.Border, Margin = new Padding(0, 10, 0, 10) });
        left.Controls.Add(Theme.Heading2("2. Mit készítsen?"));
        left.Controls.Add(_tgtUsb);
        left.Controls.Add(_tgtIso);
        left.Controls.Add(_tgtFolder);

        var usb = Theme.Column();
        var diskRow = Theme.Row();
        diskRow.Controls.Add(Theme.Field("Pendrive (csak USB-s meghajtók)", _disk, 420));
        var refresh = Theme.Button("Frissítés");
        refresh.Margin = new Padding(8, 18, 0, 0);
        refresh.Click += async (_, _) => await RefreshDisksAsync();
        diskRow.Controls.Add(refresh);
        usb.Controls.Add(diskRow);
        _formatWarning = Theme.Callout("", Color.FromArgb(138, 28, 18), Theme.DangerSoft, w);
        usb.Controls.Add(_formatWarning);
        _usbPanel.Controls.Add(usb);
        left.Controls.Add(_usbPanel);

        var existing = Theme.Column();
        var exRow = Theme.Row();
        exRow.Controls.Add(Theme.Field("A kész telepítő pendrive", _existingDrive, 420));
        var refresh2 = Theme.Button("Frissítés");
        refresh2.Margin = new Padding(8, 18, 0, 0);
        refresh2.Click += (_, _) => RefreshDrives();
        exRow.Controls.Add(refresh2);
        existing.Controls.Add(exRow);
        existing.Controls.Add(Theme.Muted("A pendrive-on lévő Windows megmarad, csak a programválasztó és a beállítások kerülnek rá.", w));
        _existingPanel.Controls.Add(existing);
        left.Controls.Add(_existingPanel);

        var isoOut = Theme.Column();
        isoOut.Controls.Add(PathRow("Új ISO fájl helye", _outputIso, w, () =>
        {
            using var save = new SaveFileDialog { Filter = "ISO fájl (*.iso)|*.iso", FileName = "Windows-telepito.iso" };
            return save.ShowDialog(this) == DialogResult.OK ? save.FileName : null;
        }));
        _oscdimg.Text = WindowsTools.FindOscdimg() ?? "";
        isoOut.Controls.Add(PathRow("oscdimg.exe (Windows ADK – Deployment Tools)", _oscdimg, w, () =>
        {
            using var open = new OpenFileDialog { Filter = "oscdimg.exe|oscdimg.exe" };
            return open.ShowDialog(this) == DialogResult.OK ? open.FileName : null;
        }));
        var adk = new LinkLabel { Text = "Nincs Windows ADK-d? Letöltés a Microsofttól (elég a \"Deployment Tools\" részt telepíteni)", AutoSize = true };
        adk.LinkClicked += (_, _) => WindowsTools.OpenUrl("https://learn.microsoft.com/windows-hardware/get-started/adk-install");
        isoOut.Controls.Add(adk);
        _isoOutPanel.Controls.Add(isoOut);
        left.Controls.Add(_isoOutPanel);

        _folderPanel.Controls.Add(PathRow("Mappa", _outputFolder, w, () =>
        {
            using var folder = new FolderBrowserDialog { Description = "Hova kerüljenek a fájlok?" };
            return folder.ShowDialog(this) == DialogResult.OK ? folder.SelectedPath : null;
        }));
        left.Controls.Add(_folderPanel);

        _checks.Margin = new Padding(0, 6, 0, 6);
        left.Controls.Add(_checks);
        var actions = Theme.Row();
        actions.Margin = new Padding(0, 10, 0, 0);
        _start.MinimumSize = new Size(300, 44);
        _start.Click += async (_, _) => await StartAsync();
        _cancel.Enabled = false;
        _cancel.MinimumSize = new Size(0, 44);
        _cancel.Click += (_, _) => _cts?.Cancel();
        actions.Controls.Add(_start);
        actions.Controls.Add(_cancel);
        left.Controls.Add(actions);
        layout.Controls.Add(left, 0, 0);

        // ---- folyamat ----
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var head = Theme.Row();
        head.Controls.Add(Theme.Heading2("Folyamat"));
        _overallText.Margin = new Padding(12, 10, 0, 0);
        head.Controls.Add(_overallText);
        right.Controls.Add(head, 0, 0);
        _overall.Dock = DockStyle.Top;
        _overall.Height = 10;
        right.Controls.Add(_overall, 0, 1);
        right.Controls.Add(new Label { Height = 8 }, 0, 2);
        _steps.View = View.Details;
        _steps.HeaderStyle = ColumnHeaderStyle.None;
        _steps.FullRowSelect = true;
        _steps.Dock = DockStyle.Fill;
        _steps.Columns.Add("", 30);
        _steps.Columns.Add("", 330);
        _steps.Columns.Add("", 240);
        right.Controls.Add(_steps, 0, 3);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.BackColor = Color.FromArgb(27, 27, 27);
        _log.ForeColor = Color.FromArgb(230, 230, 230);
        _log.Font = Theme.Mono;
        _log.Margin = new Padding(0, 10, 0, 0);
        right.Controls.Add(_log, 0, 4);
        layout.Controls.Add(right, 1, 0);

        foreach (var rb in new[] { _srcDownload, _srcIso, _srcUsb, _tgtUsb, _tgtIso, _tgtFolder })
        {
            rb.CheckedChanged += (_, _) => UpdateVisibility();
        }
        _disk.SelectedIndexChanged += (_, _) => UpdateVisibility();
        UpdateVisibility();
    }

    private static Control PathRow(string label, TextBox box, int width, Func<string?> browse)
    {
        var panel = Theme.Column();
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
        var row = Theme.Row();
        box.Width = width - 100;
        row.Controls.Add(box);
        var b = Theme.Button("Tallózás…");
        b.Click += (_, _) =>
        {
            var path = browse();
            if (path is not null)
            {
                box.Text = path;
            }
        };
        row.Controls.Add(b);
        panel.Controls.Add(row);
        return panel;
    }

    public async Task InitializeAsync()
    {
        RefreshDrives();
        await RefreshDisksAsync();
        RenderSteps();
    }

    private async Task RefreshDisksAsync()
    {
        _disk.Items.Clear();
        try
        {
            _disks = (await WindowsTools.GetDisksAsync()).Where(d => d.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch (Exception ex)
        {
            _disks = new();
            AppendLog("A lemezek listázása nem sikerült: " + ex.Message);
        }
        foreach (var d in _disks)
        {
            var letters = d.DriveLetters.Count > 0 ? string.Join(", ", d.DriveLetters.Select(l => l + ":\\")) + " " : "";
            _disk.Items.Add(new Choice<DiskInfo>(d, $"{letters}{d.FriendlyName} · {d.SizeBytes / 1_000_000_000.0:0} GB (lemez {d.Number})"));
        }
        if (_disk.Items.Count > 0)
        {
            _disk.SelectedIndex = 0;
        }
        UpdateVisibility();
    }

    private void RefreshDrives()
    {
        _existingDrive.Items.Clear();
        var system = Path.GetPathRoot(Environment.SystemDirectory);
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Removable or DriveType.Fixed && !string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase)))
        {
            var isInstaller = InstallMediaValidator.FindInstallImage(d.RootDirectory.FullName) is not null;
            _existingDrive.Items.Add(new Choice<string>(d.RootDirectory.FullName,
                $"{d.Name} {d.VolumeLabel} · {d.TotalSize / 1_000_000_000.0:0} GB · {d.DriveFormat}{(isInstaller ? " · Windows telepítő" : "")}"));
        }
        if (_existingDrive.Items.Count > 0)
        {
            _existingDrive.SelectedIndex = 0;
        }
    }

    private void UpdateVisibility()
    {
        _downloadPanel.Visible = _srcDownload.Checked;
        _isoPanel.Visible = _srcIso.Checked;
        var existing = _srcUsb.Checked;
        _tgtUsb.Enabled = _tgtIso.Enabled = !existing;
        _usbPanel.Visible = _tgtUsb.Checked && !existing;
        _existingPanel.Visible = existing;
        _isoOutPanel.Visible = _tgtIso.Checked && !existing;
        _folderPanel.Visible = _tgtFolder.Checked && !existing;

        if (_disk.SelectedItem is Choice<DiskInfo> d)
        {
            var why = WindowsMediaCommands.WhyNotFormattable(d.Value);
            _formatWarning.Text = why ?? $"A pendrive MINDEN adata törlődik ({d.Text}). Indítás előtt még egyszer rákérdezünk.";
        }
        else
        {
            _formatWarning.Text = "Nincs csatlakoztatott USB-s pendrive. Dugj be egyet (legalább 8 GB), majd kattints a Frissítés gombra.";
        }

        _start.Text = existing ? "Pendrive kiegészítése"
            : _tgtFolder.Checked ? "Fájlok mentése"
            : (_srcDownload.Checked ? "Letöltés és " : "") + (_tgtUsb.Checked ? "pendrive készítése" : "ISO készítése");
        if (!_srcDownload.Checked && !existing && !_tgtFolder.Checked)
        {
            _start.Text = char.ToUpper(_start.Text[0]) + _start.Text[1..];
        }
        if (_steps.IsHandleCreated && _cts is null)
        {
            RenderSteps();
        }
    }

    private BuildRequest? CreateRequest(out string? error)
    {
        error = null;
        var source = _srcDownload.Checked ? SourceMode.Download : _srcIso.Checked ? SourceMode.IsoFile : SourceMode.ExistingUsb;
        var target = source == SourceMode.ExistingUsb ? TargetMode.Usb : _tgtUsb.Checked ? TargetMode.Usb : _tgtIso.Checked ? TargetMode.Iso : TargetMode.Folder;
        if (_tgtFolder.Checked && source != SourceMode.ExistingUsb)
        {
            target = TargetMode.Folder;
        }
        if (source == SourceMode.IsoFile && target != TargetMode.Folder && !File.Exists(_isoPath.Text))
        {
            error = "Válaszd ki a Windows ISO fájlt.";
        }
        else if (source == SourceMode.ExistingUsb && _existingDrive.SelectedItem is null)
        {
            error = "Válaszd ki a kész telepítő pendrive-ot.";
        }
        else if (source != SourceMode.ExistingUsb && target == TargetMode.Usb && _disk.SelectedItem is not Choice<DiskInfo>)
        {
            error = "Nincs kiválasztható pendrive. Dugj be egyet, és kattints a Frissítés gombra.";
        }
        else if (source != SourceMode.ExistingUsb && target == TargetMode.Usb && WindowsMediaCommands.WhyNotFormattable(((Choice<DiskInfo>)_disk.SelectedItem!).Value) is { } why)
        {
            error = why;
        }
        else if (target == TargetMode.Iso && (string.IsNullOrWhiteSpace(_outputIso.Text) || !File.Exists(_oscdimg.Text)))
        {
            error = "Add meg az új ISO helyét és az oscdimg.exe-t (Windows ADK).";
        }
        else if (target == TargetMode.Folder && string.IsNullOrWhiteSpace(_outputFolder.Text))
        {
            error = "Válaszd ki a mappát.";
        }
        if (error is not null)
        {
            return null;
        }
        return new BuildRequest
        {
            Source = source,
            Version = ((Choice<WindowsVersion>)_version.SelectedItem!).Value,
            Language = ((Choice<string>)_language.SelectedItem!).Value,
            IsoPath = _isoPath.Text,
            Target = target,
            Disk = (_disk.SelectedItem as Choice<DiskInfo>)?.Value,
            ExistingRoot = (_existingDrive.SelectedItem as Choice<string>)?.Value,
            OutputIso = _outputIso.Text,
            OscdimgPath = _oscdimg.Text,
            OutputFolder = _outputFolder.Text,
        };
    }

    private void RenderSteps(List<BuildStep>? steps = null)
    {
        steps ??= BuildRunner.PlanSteps(CreateRequest(out _) ?? new BuildRequest
        {
            Source = _srcDownload.Checked ? SourceMode.Download : _srcIso.Checked ? SourceMode.IsoFile : SourceMode.ExistingUsb,
            Target = _tgtUsb.Checked ? TargetMode.Usb : _tgtIso.Checked ? TargetMode.Iso : TargetMode.Folder,
        });
        _steps.Items.Clear();
        foreach (var s in steps)
        {
            _steps.Items.Add(new ListViewItem(new[] { "○", s.Title, "" }) { Name = s.Key, ForeColor = Theme.TextMuted });
        }
    }

    private void OnStep(string key, StepState state, string? info)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnStep(key, state, info));
            return;
        }
        var item = _steps.Items[key];
        if (item is null)
        {
            return;
        }
        (item.Text, item.ForeColor) = state switch
        {
            StepState.Running => ("›", Theme.Accent),
            StepState.Done => ("✓", Theme.Ok),
            StepState.Failed => ("✗", Theme.Danger),
            StepState.Skipped => ("–", Theme.TextMuted),
            _ => ("○", Theme.TextMuted),
        };
        item.SubItems[2].Text = info ?? (state == StepState.Done ? "kész" : state == StepState.Running ? "folyamatban" : "");
        item.EnsureVisible();
    }

    private void OnOverall(int pct)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnOverall(pct));
            return;
        }
        _overall.Value = Math.Clamp(pct, 0, 100);
        _overallText.Text = $"összesen {pct}%";
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
            return;
        }
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
    }

    private async Task StartAsync()
    {
        var problem = _preflight();
        if (problem is not null)
        {
            MessageBox.Show(this, problem, "A készítés nem indítható", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var request = CreateRequest(out var error);
        if (request is null)
        {
            MessageBox.Show(this, error, "A készítés nem indítható", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (request.Source != SourceMode.ExistingUsb && request.Target == TargetMode.Usb)
        {
            var d = request.Disk!;
            var name = $"{string.Join(", ", d.DriveLetters.Select(l => l + ":\\"))} {d.FriendlyName} ({d.SizeBytes / 1_000_000_000.0:0} GB)";
            if (MessageBox.Show(this, $"A(z) {name} pendrive MINDEN adata törlődik.{Environment.NewLine}{Environment.NewLine}Folytatod?",
                    "Pendrive formázása", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes
                || MessageBox.Show(this, $"Utolsó figyelmeztetés: a(z) {name} tartalma visszavonhatatlanul elvész. Biztos vagy benne?",
                    "Pendrive formázása", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
        }

        _cts = new CancellationTokenSource();
        _start.Enabled = false;
        _cancel.Enabled = true;
        _log.Clear();
        _overall.Value = 0;
        RenderSteps(BuildRunner.PlanSteps(request));
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var runner = new BuildRunner(_profile(), request, http);
        runner.StepChanged += OnStep;
        runner.Log += AppendLog;
        runner.Overall += OnOverall;
        try
        {
            await Task.Run(() => runner.RunAsync(_cts.Token));
            AppendLog("✓ Minden kész.");
            MessageBox.Show(this, "Kész! Részletek a naplóban.", "Windows Telepítő Készítő", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Megszakítva.");
        }
        catch (MicrosoftDownloadBlockedException ex)
        {
            AppendLog(ex.Message);
            if (MessageBox.Show(this, ex.Message + Environment.NewLine + Environment.NewLine +
                    "Megnyitom a Microsoft letöltőoldalát: töltsd le onnan az ISO-t (ugyanazzal a nyelvvel), majd itt válaszd a \"Meglévő ISO fájl\" lehetőséget.",
                    "Letöltés", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
            {
                WindowsTools.OpenUrl(ex.PageUrl);
            }
        }
        catch (Exception ex)
        {
            AppendLog("HIBA: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Hiba a készítés közben", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _start.Enabled = true;
            _cancel.Enabled = false;
            if (request.Target == TargetMode.Usb && request.Source != SourceMode.ExistingUsb)
            {
                await RefreshDisksAsync();
            }
        }
    }
}
