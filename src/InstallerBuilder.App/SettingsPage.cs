using System.Drawing;
using System.Windows.Forms;
using InstallerBuilder.Core;

namespace InstallerBuilder.App;

/// <summary>"Windows beállítások" fül: nyelv és időzóna, helyi felhasználó, a telepítés menete.</summary>
public sealed class SettingsPage : UserControl
{
    private sealed record Choice(string Value, string Text)
    {
        public override string ToString() => Text;
    }

    private static readonly Choice[] Languages =
    {
        new("hu-HU", "Magyar (hu-HU)"), new("en-US", "Angol – USA (en-US)"), new("en-GB", "Angol – UK (en-GB)"),
        new("de-DE", "Német (de-DE)"), new("sk-SK", "Szlovák (sk-SK)"), new("ro-RO", "Román (ro-RO)"),
    };

    private static readonly Choice[] Keyboards =
    {
        new("040e:0000040e", "Magyar (040e)"), new("0409:00000409", "Angol – USA (0409)"), new("0809:00000809", "Angol – UK (0809)"),
        new("0407:00000407", "Német (0407)"), new("041b:0000041b", "Szlovák (041b)"), new("0418:00010418", "Román (0418)"),
    };

    private readonly CheckBox _regional = new() { Text = "Nyelv, billentyűzet és időzóna beállítása", AutoSize = true, Font = Theme.Bold };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _keyboard = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _timeZone = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly CheckBox _createUser = new() { Text = "Helyi felhasználó létrehozása", AutoSize = true, Font = Theme.Bold };
    private readonly TextBox _userName = new();
    private readonly TextBox _fullName = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _password2 = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _autoLogon = new() { Text = "Első indításkor automatikus bejelentkezés", AutoSize = true };
    private readonly Label _passwordWarning;

    private readonly CheckBox _skipOobe = new() { Text = "Kezdeti kérdések átugrása (licenc, adatvédelem)", AutoSize = true };
    private readonly TextBox _computerName = new();
    private readonly NumericUpDown _autoStart = new() { Minimum = 0, Maximum = 3600, Width = 70 };
    private readonly CheckBox _reboot = new() { Text = "Újraindítás a telepítés végén", AutoSize = true };

    private BuildProfile _profile = new();
    private bool _loading;

    public event Action? Changed;

    public SettingsPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(16);
        AutoScroll = true;

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        for (var i = 0; i < 3; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        }
        Controls.Add(grid);
        const int w = 330;

        // ---- nyelv ----
        var regional = Card();
        regional.Controls.Add(_regional);
        regional.Controls.Add(Theme.Muted("A telepítő nem kérdezi meg a nyelvet, a billentyűzetet és a régiót. A felület nyelvéhez ugyanilyen nyelvű Windows kell (a letöltésnél válaszd ugyanezt).", w));
        _language.Items.AddRange(Languages);
        _keyboard.Items.AddRange(Keyboards);
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            _timeZone.Items.Add(new Choice(tz.Id, tz.DisplayName));
        }
        _timeZone.DropDownWidth = 460;
        regional.Controls.Add(Theme.Field("Windows nyelve", _language, w));
        regional.Controls.Add(Theme.Field("Billentyűzet", _keyboard, w));
        regional.Controls.Add(Theme.Field("Időzóna", _timeZone, w));
        grid.Controls.Add(regional, 0, 0);

        // ---- felhasználó ----
        var user = Card();
        user.Controls.Add(_createUser);
        user.Controls.Add(Theme.Muted("Rendszergazda fiók, Microsoft-fiók nélkül. A Windows nem kér fiókot, és ehhez internetet sem.", w));
        user.Controls.Add(Theme.Field("Felhasználónév", _userName, w));
        user.Controls.Add(Theme.Field("Teljes név (nem kötelező)", _fullName, w));
        user.Controls.Add(Theme.Field("Jelszó (üres = jelszó nélkül)", _password, w));
        user.Controls.Add(Theme.Field("Jelszó újra", _password2, w));
        user.Controls.Add(_autoLogon);
        _passwordWarning = Theme.Callout("A jelszó a pendrive-on visszafejthető formában van. Telepítés után érdemes megváltoztatni.", Theme.Warn, Theme.WarnSoft, w);
        user.Controls.Add(_passwordWarning);
        grid.Controls.Add(user, 1, 0);

        // ---- menet ----
        var flow = Card();
        flow.Controls.Add(new Label { Text = "Telepítés menete", Font = Theme.Bold, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
        flow.Controls.Add(_skipOobe);
        _computerName.MaxLength = 15;
        flow.Controls.Add(Theme.Field("Számítógép neve (üres = automatikus)", _computerName, w));
        flow.Controls.Add(new Label { Text = "Programválasztó", Font = Theme.Bold, AutoSize = true, Margin = new Padding(0, 10, 0, 4) });
        var auto = Theme.Row();
        auto.Controls.Add(new Label { Text = "Magától indul", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        auto.Controls.Add(_autoStart);
        auto.Controls.Add(new Label { Text = "mp múlva (0 = megvárja)", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
        flow.Controls.Add(auto);
        flow.Controls.Add(_reboot);
        flow.Controls.Add(Theme.Callout("A telepítő továbbra is megkérdezi: melyik lemezre kerüljön a Windows és melyik kiadás (termékkulcs). Egy lemezt sosem töröl magától.", Theme.Accent, Theme.InfoSoft, w));
        grid.Controls.Add(flow, 2, 0);

        foreach (var c in new Control[] { _regional, _language, _keyboard, _timeZone, _createUser, _userName, _fullName, _password, _password2, _autoLogon, _skipOobe, _computerName, _autoStart, _reboot })
        {
            switch (c)
            {
                case CheckBox cb: cb.CheckedChanged += (_, _) => Save(); break;
                case ComboBox combo: combo.SelectedIndexChanged += (_, _) => Save(); break;
                case NumericUpDown n: n.ValueChanged += (_, _) => Save(); break;
                default: c.TextChanged += (_, _) => Save(); break;
            }
        }
    }

    private static FlowLayoutPanel Card()
    {
        var card = Theme.Column();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(14);
        card.Margin = new Padding(0, 0, 12, 12);
        card.BorderStyle = BorderStyle.FixedSingle;
        return card;
    }

    public void Bind(BuildProfile profile)
    {
        _profile = profile;
        _loading = true;
        var w = profile.Windows;
        _regional.Checked = w.ApplyRegionalSettings;
        Select(_language, w.UiLanguage);
        Select(_keyboard, w.InputLocale);
        Select(_timeZone, w.TimeZone);
        _createUser.Checked = w.CreateLocalUser;
        _userName.Text = w.UserName;
        _fullName.Text = w.DisplayName ?? "";
        _password.Text = w.Password;
        _password2.Text = w.Password;
        _autoLogon.Checked = w.AutoLogonOnce;
        _skipOobe.Checked = w.SkipOobeQuestions;
        _computerName.Text = w.ComputerName ?? "";
        _autoStart.Value = Math.Clamp(profile.Installer.AutoStartSeconds, 0, 3600);
        _reboot.Checked = profile.Installer.RebootWhenDone;
        _loading = false;
        UpdateEnabled();
    }

    private static void Select(ComboBox combo, string value)
    {
        foreach (Choice c in combo.Items)
        {
            if (string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = c;
                return;
            }
        }
        if (combo.Items.Count > 0 && combo.SelectedIndex < 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private void Save()
    {
        if (_loading)
        {
            return;
        }
        var w = _profile.Windows;
        w.ApplyRegionalSettings = _regional.Checked;
        if (_language.SelectedItem is Choice lang)
        {
            w.UiLanguage = lang.Value;
            w.UserLocale = lang.Value;
            w.SystemLocale = lang.Value;
        }
        if (_keyboard.SelectedItem is Choice kb)
        {
            w.InputLocale = kb.Value;
        }
        if (_timeZone.SelectedItem is Choice tz)
        {
            w.TimeZone = tz.Value;
        }
        w.CreateLocalUser = _createUser.Checked;
        w.UserName = _userName.Text.Trim();
        w.DisplayName = string.IsNullOrWhiteSpace(_fullName.Text) ? null : _fullName.Text.Trim();
        w.Password = _password.Text;
        w.AutoLogonOnce = _autoLogon.Checked;
        w.SkipOobeQuestions = _skipOobe.Checked;
        w.ComputerName = string.IsNullOrWhiteSpace(_computerName.Text) ? null : _computerName.Text.Trim();
        _profile.Installer.AutoStartSeconds = (int)_autoStart.Value;
        _profile.Installer.RebootWhenDone = _reboot.Checked;
        UpdateEnabled();
        Changed?.Invoke();
    }

    private void UpdateEnabled()
    {
        foreach (var c in new Control[] { _language, _keyboard, _timeZone })
        {
            c.Enabled = _regional.Checked;
        }
        foreach (var c in new Control[] { _userName, _fullName, _password, _password2, _autoLogon })
        {
            c.Enabled = _createUser.Checked;
        }
        _passwordWarning.Visible = _createUser.Checked && _password.Text.Length > 0;
    }

    /// <summary>A készítés előtti ellenőrzés: a két jelszó egyezik-e.</summary>
    public string? ValidatePasswords() =>
        _createUser.Checked && _password.Text != _password2.Text ? "A két jelszó nem egyezik (Windows beállítások)." : null;
}
