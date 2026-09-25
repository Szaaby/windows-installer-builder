using System.Drawing;
using System.Windows.Forms;
using InstallerBuilder.Core;

namespace InstallerBuilder.App;

/// <summary>"Programok" fül: a telepítendő lista (bal) és a hozzáadás (jobb: winget keresés, népszerűek, saját telepítő).</summary>
public sealed class ProgramsPage : UserControl
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly ListView _results = new();
    private readonly Label _status = new();
    private readonly Label _searchInfo;
    private BuildProfile _profile = new();
    private bool _loading;

    public event Action? Changed;

    public ProgramsPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
        Controls.Add(layout);

        // ---- bal: a lista ----
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(0, 0, 16, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var head = Theme.Row();
        head.Controls.Add(Theme.Heading2("Telepítendő programok"));
        head.Controls.Add(Theme.Muted("A pipa: a telepítéskor megjelenő listában alapból be lesz jelölve."));
        left.Controls.Add(head, 0, 0);

        SetupGrid();
        left.Controls.Add(_grid, 0, 1);

        var buttons = Theme.Row();
        buttons.Margin = new Padding(0, 8, 0, 0);
        var up = Theme.Button("▲ Fel");
        var down = Theme.Button("▼ Le");
        var remove = Theme.Button("Törlés");
        var verify = Theme.Button("Azonosítók ellenőrzése");
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(1);
        remove.Click += (_, _) => RemoveSelected();
        verify.Click += async (_, _) => await VerifyIdsAsync(verify);
        buttons.Controls.AddRange(new Control[] { up, down, remove, verify });
        left.Controls.Add(buttons, 0, 2);

        _status.AutoSize = true;
        _status.ForeColor = Theme.TextMuted;
        _status.Margin = new Padding(0, 8, 0, 0);
        left.Controls.Add(_status, 0, 3);
        layout.Controls.Add(left, 0, 0);

        // ---- jobb: hozzáadás ----
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16, 0, 0, 0),
        };
        right.Paint += (_, e) => e.Graphics.DrawLine(new Pen(Theme.Border), 0, 0, 0, right.Height);
        right.Controls.Add(new Label { Text = "Program keresése (winget)", Font = Theme.Bold, AutoSize = true });
        var searchRow = Theme.Row();
        _search.Width = 270;
        _search.PlaceholderText = "pl. firefox";
        var searchButton = Theme.PrimaryButton("Keresés");
        searchButton.MinimumSize = new Size(0, 28);
        searchButton.Click += async (_, _) => await SearchAsync(searchButton);
        _search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SearchAsync(searchButton);
            }
        };
        searchRow.Controls.Add(_search);
        searchRow.Controls.Add(searchButton);
        right.Controls.Add(searchRow);

        _results.View = View.Details;
        _results.FullRowSelect = true;
        _results.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _results.Size = new Size(360, 170);
        _results.Columns.Add("Név", 150);
        _results.Columns.Add("Azonosító", 140);
        _results.Columns.Add("Verzió", 60);
        _results.DoubleClick += (_, _) => AddSelectedResult();
        right.Controls.Add(_results);
        var addResult = Theme.Button("+ Kijelölt hozzáadása");
        addResult.Click += (_, _) => AddSelectedResult();
        right.Controls.Add(addResult);
        _searchInfo = Theme.Muted("", 360);
        right.Controls.Add(_searchInfo);

        right.Controls.Add(new Label { Text = "Népszerű programok", Font = Theme.Bold, AutoSize = true, Margin = new Padding(0, 12, 0, 4) });
        var chips = new FlowLayoutPanel { Size = new Size(365, 190), AutoScroll = true, WrapContents = true };
        foreach (var p in CuratedPrograms.All)
        {
            var chip = new Button { Text = "+ " + p.Name, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(245, 245, 245), Margin = new Padding(0, 0, 6, 6) };
            chip.FlatAppearance.BorderColor = Theme.Border;
            var toolTip = new ToolTip();
            toolTip.SetToolTip(chip, $"{p.WingetId} · {p.Category}");
            chip.Click += (_, _) => AddWinget(p.Name, p.WingetId);
            chips.Controls.Add(chip);
        }
        right.Controls.Add(chips);

        right.Controls.Add(new Label { Text = "Nincs a winget-ben?", Font = Theme.Bold, AutoSize = true, Margin = new Padding(0, 12, 0, 4) });
        var addLocal = Theme.Button("Saját telepítő hozzáadása (.exe, .msi, .reg…)");
        addLocal.Width = 360;
        addLocal.AutoSize = false;
        addLocal.Height = 34;
        addLocal.Click += (_, _) => AddLocal();
        right.Controls.Add(addLocal);
        var addId = Theme.Button("Winget azonosító kézi megadása…");
        addId.Width = 360;
        addId.AutoSize = false;
        addId.Height = 34;
        addId.Click += (_, _) =>
        {
            using var dlg = new WingetIdDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                AddWinget(dlg.ProgramName, dlg.WingetId);
            }
        };
        right.Controls.Add(addId);
        layout.Controls.Add(right, 1, 0);
    }

    private void SetupGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.GridColor = Color.FromArgb(239, 239, 239);
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 247, 247);
        _grid.ColumnHeadersDefaultCellStyle.Font = Theme.Bold;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _grid.RowTemplate.Height = 30;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "sel", HeaderText = "Alapból", Width = 64 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Név", Width = 200 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "source", HeaderText = "Forrás", Width = 110, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "Azonosító / fájl", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "args", HeaderText = "Csendes kapcsolók", Width = 160 });
        _grid.Columns["id"]!.DefaultCellStyle.Font = Theme.Mono;
        _grid.Columns["args"]!.DefaultCellStyle.Font = Theme.Mono;

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (_loading || e.RowIndex < 0 || e.RowIndex >= _profile.Programs.Count)
            {
                return;
            }
            var p = _profile.Programs[e.RowIndex];
            var row = _grid.Rows[e.RowIndex];
            p.DefaultSelected = row.Cells["sel"].Value is true;
            p.Name = row.Cells["name"].Value?.ToString()?.Trim() ?? p.Name;
            if (p.Source == ProgramSource.Local)
            {
                p.SilentArgs = row.Cells["args"].Value?.ToString()?.Trim();
            }
            UpdateStatus();
            Changed?.Invoke();
        };
    }

    public void Bind(BuildProfile profile)
    {
        _profile = profile;
        RefreshGrid();
    }

    private void RefreshGrid(int select = -1)
    {
        _loading = true;
        _grid.Rows.Clear();
        foreach (var p in _profile.Programs)
        {
            var isLocal = p.Source == ProgramSource.Local;
            var detail = isLocal
                ? $"{Path.GetFileName(p.LocalPath)}{(p.LocalPath is not null && File.Exists(p.LocalPath) ? " · " + MediaWriter.FormatSize(new FileInfo(p.LocalPath).Length) : " · HIÁNYZIK")}"
                : p.WingetId;
            var i = _grid.Rows.Add(p.DefaultSelected, p.Name, isLocal ? "saját telepítő" : "winget", detail, isLocal ? p.SilentArgs : "–");
            var row = _grid.Rows[i];
            row.Cells["source"].Style.ForeColor = isLocal ? Theme.Warn : Theme.Accent;
            row.Cells["args"].ReadOnly = !isLocal;
            if (!isLocal)
            {
                row.Cells["args"].Style.ForeColor = Theme.TextMuted;
            }
        }
        if (select >= 0 && select < _grid.Rows.Count)
        {
            _grid.ClearSelection();
            _grid.Rows[select].Selected = true;
            _grid.CurrentCell = _grid.Rows[select].Cells["name"];
        }
        _loading = false;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var winget = _profile.Programs.Count(p => p.Source == ProgramSource.Winget);
        var locals = _profile.Programs.Where(p => p.Source == ProgramSource.Local).ToList();
        var size = locals.Where(p => p.LocalPath is not null && File.Exists(p.LocalPath)).Sum(p => new FileInfo(p.LocalPath!).Length);
        _status.Text = $"{_profile.Programs.Count} program · {winget} winget · {locals.Count} saját telepítő ({MediaWriter.FormatSize(size)}) · " +
                       $"{_profile.Programs.Count(p => p.DefaultSelected)} alapból bejelölve";
    }

    private void AddWinget(string name, string id)
    {
        if (_profile.Programs.Any(p => p.Source == ProgramSource.Winget && string.Equals(p.WingetId, id, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"{name} már a listában van.", "Program hozzáadása", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _profile.Programs.Add(new ProgramEntry { Name = name, Source = ProgramSource.Winget, WingetId = id });
        RefreshGrid(_profile.Programs.Count - 1);
        Changed?.Invoke();
    }

    private void AddLocal()
    {
        using var open = new OpenFileDialog
        {
            Title = "Saját telepítő kiválasztása",
            Filter = "Telepítők (*.exe;*.msi;*.msix;*.msixbundle;*.appx;*.reg;*.cmd;*.bat;*.ps1)|*.exe;*.msi;*.msix;*.msixbundle;*.appx;*.reg;*.cmd;*.bat;*.ps1|Minden fájl (*.*)|*.*",
        };
        if (open.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        using var dlg = new LocalInstallerDialog(open.FileName);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        _profile.Programs.Add(new ProgramEntry { Name = dlg.ProgramName, Source = ProgramSource.Local, LocalPath = dlg.FilePath, SilentArgs = dlg.SilentArgs });
        RefreshGrid(_profile.Programs.Count - 1);
        Changed?.Invoke();
    }

    private int SelectedIndex => _grid.CurrentRow?.Index ?? -1;

    private void MoveSelected(int delta)
    {
        var i = SelectedIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _profile.Programs.Count)
        {
            return;
        }
        (_profile.Programs[i], _profile.Programs[j]) = (_profile.Programs[j], _profile.Programs[i]);
        RefreshGrid(j);
        Changed?.Invoke();
    }

    private void RemoveSelected()
    {
        var i = SelectedIndex;
        if (i < 0)
        {
            return;
        }
        _profile.Programs.RemoveAt(i);
        RefreshGrid(Math.Min(i, _profile.Programs.Count - 1));
        Changed?.Invoke();
    }

    private async Task SearchAsync(Button button)
    {
        var q = _search.Text.Trim();
        if (q.Length < 2)
        {
            return;
        }
        button.Enabled = false;
        _searchInfo.Text = "Keresés…";
        _results.Items.Clear();
        try
        {
            var found = await WingetClient.SearchAsync(q);
            foreach (var r in found.Take(50))
            {
                var item = new ListViewItem(new[] { r.Name, r.Id, r.Version }) { Tag = r };
                if (r.Truncated)
                {
                    item.ForeColor = Theme.TextMuted;
                }
                _results.Items.Add(item);
            }
            _searchInfo.Text = found.Count == 0 ? "Nincs találat." :
                found.Any(r => r.Truncated) ? "A szürke sorok azonosítója túl hosszú a kijelzéshez – azokat a \"kézi megadás\"-sal vedd fel." :
                $"{found.Count} találat – dupla kattintás vagy a gomb hozzáadja.";
        }
        catch (Exception ex)
        {
            _searchInfo.Text = "A winget nem érhető el ezen a gépen: " + ex.Message + " (A népszerű programok és a kézi megadás így is működik.)";
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private void AddSelectedResult()
    {
        if (_results.SelectedItems.Count == 0 || _results.SelectedItems[0].Tag is not WingetSearchResult r)
        {
            return;
        }
        if (r.Truncated)
        {
            MessageBox.Show(this, "Ennek a találatnak az azonosítója le van vágva a winget kimenetében. Vedd fel a \"Winget azonosító kézi megadása\" gombbal.", "Program hozzáadása", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        AddWinget(r.Name, r.Id);
    }

    private async Task VerifyIdsAsync(Button button)
    {
        var ids = _profile.Programs.Where(p => p.Source == ProgramSource.Winget).ToList();
        if (ids.Count == 0)
        {
            return;
        }
        button.Enabled = false;
        var bad = new List<string>();
        try
        {
            for (var i = 0; i < ids.Count; i++)
            {
                _status.Text = $"Ellenőrzés: {ids[i].Name} ({i + 1} / {ids.Count})…";
                if (!await WingetClient.ExistsAsync(ids[i].WingetId!))
                {
                    bad.Add($"{ids[i].Name} ({ids[i].WingetId})");
                }
            }
            MessageBox.Show(this, bad.Count == 0 ? "Minden azonosító létezik a winget katalógusban." :
                "Ezek nem találhatók a winget katalógusban (elírás, vagy megszűnt csomag):" + Environment.NewLine + string.Join(Environment.NewLine, bad),
                "Azonosítók ellenőrzése", MessageBoxButtons.OK, bad.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A winget nem érhető el ezen a gépen: " + ex.Message, "Azonosítók ellenőrzése", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
            UpdateStatus();
        }
    }
}
