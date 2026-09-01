using System.Drawing;
using System.Windows.Forms;

namespace RevitCodexBridge.Addin;

internal enum UiButtonKind
{
    Primary,
    Secondary,
    Quiet,
    Danger
}

internal static class UiTheme
{
    public static readonly Color WindowBackground = Color.FromArgb(245, 247, 250);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(218, 224, 232);
    public static readonly Color Text = Color.FromArgb(31, 41, 55);
    public static readonly Color MutedText = Color.FromArgb(92, 104, 121);
    public static readonly Color Primary = Color.FromArgb(37, 99, 235);
    public static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216);
    public static readonly Color Danger = Color.FromArgb(185, 28, 28);
    public static readonly Color InfoSurface = Color.FromArgb(239, 246, 255);

    public static void ApplyForm(Form form)
    {
        form.BackColor = WindowBackground;
        form.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
    }

    public static Panel CreateSurfacePanel(Padding padding)
    {
        return new Panel
        {
            BackColor = Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            Padding = padding
        };
    }

    public static Label CreateTitle(Control owner, string text, float size = 16F)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Text,
            Font = new Font(owner.Font.FontFamily, size, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    public static Label CreateMutedLabel(string text = "")
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = MutedText,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    public static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    public static Button CreateButton(string text, UiButtonKind kind, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Width = Math.Max(86, text.Length * 14 + 30),
            Height = 34,
            Margin = new Padding(8, 8, 0, 8),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };

        ApplyButtonKind(button, kind);
        button.Click += onClick;
        return button;
    }

    public static void ApplyButtonKind(Button button, UiButtonKind kind)
    {
        button.FlatAppearance.BorderSize = 1;
        switch (kind)
        {
            case UiButtonKind.Primary:
                button.BackColor = Primary;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = PrimaryHover;
                break;
            case UiButtonKind.Danger:
                button.BackColor = Danger;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(127, 29, 29);
                break;
            case UiButtonKind.Quiet:
                button.BackColor = Surface;
                button.ForeColor = MutedText;
                button.FlatAppearance.BorderColor = Border;
                break;
            default:
                button.BackColor = Color.FromArgb(248, 250, 252);
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = Border;
                break;
        }
    }

    public static void StyleTextBox(TextBox textBox, bool monospace = false)
    {
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = Color.White;
        textBox.ForeColor = Text;
        textBox.Font = monospace
            ? new Font("Consolas", 10F, FontStyle.Regular)
            : new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
    }

    public static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.BackColor = Color.White;
        comboBox.ForeColor = Text;
    }
}
