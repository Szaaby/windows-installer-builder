using System.Drawing;
using System.Windows.Forms;
using InstallerBuilder.Core;

namespace InstallerBuilder.App;

/// <summary>Egyszerű párbeszédablak-váz: tartalom + OK / Mégse.</summary>
public class DialogBase : Form
{
    protected readonly FlowLayoutPanel Body = Theme.Column();
    protected readonly Button OkButton = Theme.PrimaryButton("OK");

    public DialogBase(string title)
    {
        Text = title;
        Font = Theme.Base;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.White;
        Padding = new Padding(16);

        var outer = Theme.Column();
        outer.Controls.Add(Body);
        var buttons = Theme.Row();
        buttons.Anchor = AnchorStyles.Right;
        var cancel = Theme.Button("Mégse");
        cancel.DialogResult = DialogResult.Cancel;
        OkButton.Click += (_, _) =>
        {
            var error = ValidateInput();
            if (error is not null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(OkButton);
        outer.Controls.Add(buttons);
        Controls.Add(outer);
        AcceptButton = OkButton;
        CancelButton = cancel;
    }

    protected virtual string? ValidateInput() => null;
}

/// <summary>Winget azonosító kézi megadása.</summary>
public sealed class WingetIdDialog : DialogBase
{
    private readonly TextBox _name = new();
    private readonly TextBox _id = new();

    public WingetIdDialog() : base("Winget azonosító megadása")
    {
        Body.Controls.Add(Theme.Muted("Az azonosítót a winget.run vagy a \"winget search\" parancs mutatja (pl. Mozilla.Firefox).", 420));
        Body.Controls.Add(Theme.Field("Azonosító", _id, 420));
        Body.Controls.Add(Theme.Field("Név a listában", _name, 420));
        OkButton.Text = "Hozzáadás";
        _id.TextChanged += (_, _) =>
        {
            if (!_nameEdited)
            {
                _name.Text = _id.Text.Split('.').LastOrDefault() ?? "";
            }
        };
        _name.KeyPress += (_, _) => _nameEdited = true;
    }

    private bool _nameEdited;

    public string WingetId => _id.Text.Trim();
    public string ProgramName => _name.Text.Trim();

    protected override string? ValidateInput() =>
        WingetId.Length == 0 || WingetId.Contains(' ') ? "Adj meg egy érvényes azonosítót (szóköz nélkül, pl. Mozilla.Firefox)." :
        ProgramName.Length == 0 ? "Adj nevet a programnak." : null;
}

/// <summary>Saját telepítő felvétele: fájl, név, csendes kapcsolók (javaslattal).</summary>
public sealed class LocalInstallerDialog : DialogBase
{
    private readonly TextBox _name = new();
    private readonly TextBox _args = new();
    private readonly Label _hint;

    public LocalInstallerDialog(string path) : base("Saját telepítő hozzáadása")
    {
        FilePath = path;
        var suggestion = InstallerTypeDetector.Suggest(path);
        var size = new FileInfo(path).Length;
        Body.Controls.Add(new Label { Text = Path.GetFileName(path), Font = Theme.Bold, AutoSize = true });
        Body.Controls.Add(Theme.Muted($"{MediaWriter.FormatSize(size)} · {suggestion.Note}", 460));
        _name.Text = Path.GetFileNameWithoutExtension(path);
        _args.Text = suggestion.Args;
        Body.Controls.Add(Theme.Field("Név a listában", _name, 460));
        Body.Controls.Add(Theme.Field("Csendes telepítés kapcsolói", _args, 460));
        _hint = Theme.Muted("Gyakori kapcsolók: NSIS: /S · Inno Setup: /VERYSILENT /NORESTART · InstallShield: /s /v\"/qn\" · egyéb: /quiet vagy /silent. " +
            "Ha üresen hagyod, a telepítő a saját ablakával indul, és kattintani kell benne.", 460);
        Body.Controls.Add(_hint);
        if (size > InstallMediaValidator.Fat32MaxFileSize)
        {
            Body.Controls.Add(Theme.Callout("4 GB-nál nagyobb fájl: FAT32-es pendrive-ra nem fér rá (ISO-ba és NTFS-re igen).", Theme.Warn, Theme.WarnSoft, 460));
        }
        OkButton.Text = "Hozzáadás";
    }

    public string FilePath { get; }
    public string ProgramName => _name.Text.Trim();
    public string SilentArgs => _args.Text.Trim();

    protected override string? ValidateInput() => ProgramName.Length == 0 ? "Adj nevet a programnak." : null;
}
