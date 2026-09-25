using System.Drawing;
using System.Windows.Forms;

namespace InstallerBuilder.App;

/// <summary>A látványterv színei és a gyakori vezérlők (Windows 11 világos stílus, Segoe UI).</summary>
public static class Theme
{
    public static readonly Color Accent = Color.FromArgb(15, 108, 189);
    public static readonly Color AccentSoft = Color.FromArgb(232, 241, 250);
    public static readonly Color Text = Color.FromArgb(27, 27, 27);
    public static readonly Color TextMuted = Color.FromArgb(92, 92, 92);
    public static readonly Color Border = Color.FromArgb(224, 224, 224);
    public static readonly Color Window = Color.FromArgb(243, 243, 243);
    public static readonly Color Ok = Color.FromArgb(14, 122, 13);
    public static readonly Color Warn = Color.FromArgb(157, 93, 0);
    public static readonly Color WarnSoft = Color.FromArgb(253, 241, 227);
    public static readonly Color Danger = Color.FromArgb(196, 43, 28);
    public static readonly Color DangerSoft = Color.FromArgb(253, 236, 234);
    public static readonly Color InfoSoft = Color.FromArgb(232, 241, 250);

    public static readonly Font Base = new("Segoe UI", 9.75f);
    public static readonly Font Bold = new("Segoe UI", 9.75f, FontStyle.Bold);
    public static readonly Font Heading = new("Segoe UI Semibold", 12f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Mono = new("Consolas", 9f);

    public static Button PrimaryButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(0, 36),
            Padding = new Padding(12, 0, 12, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = Bold,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static Button Button(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(0, 32),
            Padding = new Padding(8, 0, 8, 0),
        };
    }

    public static Label Heading2(string text) => new()
    {
        Text = text,
        Font = Heading,
        AutoSize = true,
        Margin = new Padding(0, 4, 0, 6),
    };

    public static Label Muted(string text, int width = 0) => new()
    {
        Text = text,
        ForeColor = TextMuted,
        AutoSize = true,
        MaximumSize = width > 0 ? new Size(width, 0) : Size.Empty,
        Margin = new Padding(0, 2, 0, 6),
    };

    /// <summary>Színes figyelmeztető / tájékoztató doboz.</summary>
    public static Label Callout(string text, Color fore, Color back, int width) => new()
    {
        Text = text,
        ForeColor = fore,
        BackColor = back,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        MinimumSize = new Size(width, 0),
        Padding = new Padding(8, 6, 8, 6),
        Margin = new Padding(0, 6, 0, 6),
    };

    /// <summary>Címke + mező egymás alatt.</summary>
    public static Control Field(string label, Control input, int width)
    {
        input.Width = width;
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
        input.Margin = new Padding(0);
        panel.Controls.Add(input);
        return panel;
    }

    /// <summary>Függőleges, automatikus méretű oszlop.</summary>
    public static FlowLayoutPanel Column() => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
    };

    /// <summary>Vízszintes sor.</summary>
    public static FlowLayoutPanel Row() => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0, 0, 0, 6),
    };
}
