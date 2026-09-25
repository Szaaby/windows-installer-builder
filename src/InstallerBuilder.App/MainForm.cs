using System.Drawing;
using System.Windows.Forms;
using InstallerBuilder.Core;

namespace InstallerBuilder.App;

/// <summary>A főablak: profil-menü + a három fül (Programok, Windows beállítások, Készítés).</summary>
public sealed class MainForm : Form
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(18, 6) };
    private readonly ProgramsPage _programs = new();
    private readonly SettingsPage _settings = new();
    private readonly BuildPage _build;
    private BuildProfile _profile = new();
    private string? _profilePath;
    private bool _dirty;

    /// <summary>Automatikus mentés: a program a legutóbbi állapottal nyílik meg (a jelszó is ebben a fájlban van).</summary>
    private static readonly string AutosavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsTelepitoKeszito", "utolso-profil.json");

    public MainForm(string? profilePath = null)
    {
        Text = "Windows Telepítő Készítő";
        Font = Theme.Base;
        BackColor = Theme.Window;
        MinimumSize = new Size(1180, 760);
        Size = new Size(1320, 860);
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // ikon nélkül is működik
        }

        _build = new BuildPage(() => _profile, Preflight);

        var menu = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, Padding = new Padding(8, 4, 8, 4), BackColor = Color.White };
        menu.Items.Add(new ToolStripButton("Új profil", null, (_, _) => NewProfile()));
        menu.Items.Add(new ToolStripButton("Profil megnyitása…", null, (_, _) => OpenProfile()));
        menu.Items.Add(new ToolStripButton("Profil mentése", null, (_, _) => SaveProfile(false)));
        menu.Items.Add(new ToolStripButton("Mentés másként…", null, (_, _) => SaveProfile(true)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripButton("Súgó", null, (_, _) => WindowsTools.OpenUrl("https://github.com/Szaaby/windows-installer-builder#readme")));

        AddTab("Programok", _programs);
        AddTab("Windows beállítások", _settings);
        AddTab("Készítés", _build);

        Controls.Add(_tabs);
        Controls.Add(menu);

        _programs.Changed += MarkDirty;
        _settings.Changed += MarkDirty;

        if (profilePath is not null && File.Exists(profilePath))
        {
            LoadProfile(profilePath);
        }
        else if (File.Exists(AutosavePath))
        {
            try
            {
                SetProfile(ProfileStore.Load(AutosavePath), null);
            }
            catch
            {
                SetProfile(DefaultProfile(), null);
            }
        }
        else
        {
            SetProfile(DefaultProfile(), null);
        }

        Shown += async (_, _) =>
        {
            try
            {
                await _build.InitializeAsync();
            }
            catch
            {
                // a lemezlista hibáját a Készítés fül naplója jelzi
            }
        };
        FormClosing += (_, e) =>
        {
            Autosave();
            if (_dirty && _profilePath is not null &&
                MessageBox.Show(this, "A profil módosult. Mented?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                SaveProfile(false);
            }
        };
    }

    private void AddTab(string title, Control page)
    {
        var tab = new TabPage(title) { BackColor = Color.White, Padding = new Padding(0) };
        tab.Controls.Add(page);
        _tabs.TabPages.Add(tab);
    }

    private static BuildProfile DefaultProfile()
    {
        var p = new BuildProfile();
        foreach (var id in new[] { "Google.Chrome", "7zip.7zip", "VideoLAN.VLC" })
        {
            var c = CuratedPrograms.All.First(x => x.WingetId == id);
            p.Programs.Add(new ProgramEntry { Name = c.Name, Source = ProgramSource.Winget, WingetId = c.WingetId });
        }
        p.Windows.UserName = Environment.UserName.Length <= 20 ? Environment.UserName : "";
        return p;
    }

    private void SetProfile(BuildProfile profile, string? path)
    {
        _profile = profile;
        _profilePath = path;
        _programs.Bind(profile);
        _settings.Bind(profile);
        _dirty = false;
        UpdateTitle();
    }

    private void MarkDirty()
    {
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Text = "Windows Telepítő Készítő" + (_profilePath is null ? "" : $" – {Path.GetFileName(_profilePath)}") + (_dirty ? " *" : "");

    private void NewProfile()
    {
        if (MessageBox.Show(this, "Új, alapértelmezett profilt kezdesz? A jelenlegi lista elvész, ha nincs elmentve.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
        {
            SetProfile(DefaultProfile(), null);
        }
    }

    private void OpenProfile()
    {
        using var open = new OpenFileDialog { Filter = "Profil (*.json)|*.json", Title = "Profil megnyitása" };
        if (open.ShowDialog(this) == DialogResult.OK)
        {
            LoadProfile(open.FileName);
        }
    }

    private void LoadProfile(string path)
    {
        try
        {
            SetProfile(ProfileStore.Load(path), path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A profil nem olvasható: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveProfile(bool saveAs)
    {
        var path = _profilePath;
        if (saveAs || path is null)
        {
            using var save = new SaveFileDialog { Filter = "Profil (*.json)|*.json", FileName = "sajat-profil.json", Title = "Profil mentése" };
            if (save.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            path = save.FileName;
        }
        try
        {
            ProfileStore.Save(_profile, path!);
            _profilePath = path;
            _dirty = false;
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A profil nem menthető: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Autosave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AutosavePath)!);
            ProfileStore.Save(_profile, AutosavePath);
        }
        catch
        {
            // az automatikus mentés hibája nem akadályozhatja a kilépést
        }
    }

    /// <summary>A készítés előtti ellenőrzés (a profil hibái) - null = rendben.</summary>
    private string? Preflight()
    {
        var problems = new List<string>();
        if (_settings.ValidatePasswords() is { } pw)
        {
            problems.Add(pw);
        }
        problems.AddRange(MediaPlanner.Validate(_profile));
        Autosave();
        return problems.Count == 0 ? null : "Javítsd ezeket, mielőtt elkészíted:" + Environment.NewLine + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", problems);
    }

    // ---- a CI önteszthez (Program.cs --selftest) ----

    internal void LoadSampleForSelfTest()
    {
        var p = DefaultProfile();
        p.Windows.UserName = "Teszt";
        p.Windows.Password = "jelszo";
        p.Programs.Add(new ProgramEntry { Name = "Saját driver", Source = ProgramSource.Local, LocalPath = Application.ExecutablePath, SilentArgs = "/S" });
        SetProfile(p, null);
        if (Preflight() is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
        var plan = MediaPlanner.Plan(p);
        if (!plan.Any(f => f.RelativePath == MediaLayout.AutounattendFile))
        {
            throw new InvalidOperationException("A terv nem tartalmaz autounattend.xml-t.");
        }
    }

    internal IEnumerable<int> AllPagesForSelfTest() => Enumerable.Range(0, _tabs.TabCount);

    internal void ShowPageForSelfTest(int index) => _tabs.SelectedIndex = index;
}
