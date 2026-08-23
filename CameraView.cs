using LibVLCSharp.WinForms;

namespace CctvPip.App;

internal sealed class CameraView : Panel
{
    private readonly VideoView videoView = new();
    private readonly Label statusLabel = new();

    public CameraView(string streamLabel)
    {
        StreamLabel = streamLabel;
        BackColor = Color.Black;
        BorderStyle = BorderStyle.None;

        videoView.BackColor = Color.Black;
        videoView.Dock = DockStyle.Fill;

        statusLabel.BackColor = Color.FromArgb(210, 0, 0, 0);
        statusLabel.ForeColor = Color.White;
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 16, FontStyle.Bold);
        statusLabel.Padding = new Padding(16);
        statusLabel.UseCompatibleTextRendering = true;

        Controls.Add(statusLabel);
        Controls.Add(videoView);
        statusLabel.BringToFront();

        ShowNoSignal("NO SIGNAL");
    }

    public string StreamLabel { get; }

    public VideoView VideoView => videoView;

    public void AttachContextMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        videoView.ContextMenuStrip = menu;
        statusLabel.ContextMenuStrip = menu;
    }

    public void ShowNoSignal(string message, string? detail = null)
    {
        statusLabel.Text = string.IsNullOrWhiteSpace(detail)
            ? $"{StreamLabel}{Environment.NewLine}{message}"
            : $"{StreamLabel}{Environment.NewLine}{message}{Environment.NewLine}{TrimDetail(detail)}";
        statusLabel.Visible = true;
        statusLabel.BringToFront();
    }

    public void ShowLive()
    {
        statusLabel.Visible = false;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        statusLabel.Font = new Font(
            SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif,
            Math.Clamp(Width / 44f, 10f, 22f),
            FontStyle.Bold);
    }

    private static string TrimDetail(string detail)
    {
        detail = detail.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return detail.Length <= 240 ? detail : detail[..240] + "...";
    }
}
