using System.Drawing.Drawing2D;
using static FlowScribe.NativeMethods;

namespace FlowScribe;

/// <summary>
/// A small pill near the bottom of the screen that shows "Listening",
/// "Transcribing" or a problem. It never takes focus and ignores the mouse,
/// so the text still lands in the field you were typing in.
/// </summary>
sealed class Hud : Form
{
    const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    readonly Label _label = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
    };
    readonly System.Windows.Forms.Timer _hideTimer = new();

    public Hud()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 32, 36);
        Opacity = 0.94;
        Padding = new Padding(18, 6, 18, 6);
        Controls.Add(_label);
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); HidePill(); };
        CreateHandle();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return p;
        }
    }

    /// Sticky messages stay until replaced or hidden; others disappear after `seconds`.
    public void ShowMessage(string message, bool sticky = false, double seconds = 3)
    {
        _hideTimer.Stop();
        _label.Text = message;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var textSize = TextRenderer.MeasureText(message, _label.Font, new Size(440, 0), TextFormatFlags.WordBreak);
        var width = Math.Clamp(textSize.Width + Padding.Horizontal + 8, 160, 480);
        var height = Math.Max(36, textSize.Height + Padding.Vertical + 8);
        Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Bottom - height - 40, width, height);
        using (var path = Rounded(new Rectangle(0, 0, width, height), Math.Min(18, height / 2)))
            Region = new Region(path);
        ShowWindow(Handle, SW_SHOWNOACTIVATE);
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        if (!sticky)
        {
            _hideTimer.Interval = (int)(seconds * 1000);
            _hideTimer.Start();
        }
    }

    public void HidePill()
    {
        _hideTimer.Stop();
        Hide();
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
