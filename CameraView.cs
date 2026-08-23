using LibVLCSharp.WinForms;

namespace CctvPip.App;

internal sealed class CameraView : Panel
{
    private readonly VideoView videoView = new();
    private readonly Label statusLabel = new();

    /// <summary>
    /// Creates a video container with a LibVLC video surface and an overlaid status label.
    /// </summary>
    /// <param name="streamLabel">The label shown in no-signal and error states for this stream.</param>
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

    /// <summary>
    /// Attaches the shared application context menu to the panel, video surface, and status overlay.
    /// </summary>
    /// <param name="menu">The context menu to show when the user right-clicks this camera view.</param>
    public void AttachContextMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        videoView.ContextMenuStrip = menu;
        statusLabel.ContextMenuStrip = menu;
    }

    /// <summary>
    /// Shows the status overlay with a stream label, message, and optional technical detail.
    /// </summary>
    /// <param name="message">The primary status message to display.</param>
    /// <param name="detail">Optional additional detail, such as an exception message or connection reason.</param>
    public void ShowNoSignal(string message, string? detail = null)
    {
        statusLabel.Text = string.IsNullOrWhiteSpace(detail)
            ? $"{StreamLabel}{Environment.NewLine}{message}"
            : $"{StreamLabel}{Environment.NewLine}{message}{Environment.NewLine}{TrimDetail(detail)}";
        statusLabel.Visible = true;
        statusLabel.BringToFront();
    }

    /// <summary>
    /// Hides the status overlay so the live video surface is unobstructed.
    /// </summary>
    public void ShowLive()
    {
        statusLabel.Visible = false;
    }

    /// <summary>
    /// Recalculates the status-label font size whenever the camera view is resized.
    /// </summary>
    /// <param name="eventargs">The resize event arguments supplied by WinForms.</param>
    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        statusLabel.Font = new Font(
            SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif,
            Math.Clamp(Width / 44f, 10f, 22f),
            FontStyle.Bold);
    }

    /// <summary>
    /// Normalizes and truncates long status detail text so it fits inside the overlay.
    /// </summary>
    /// <param name="detail">The raw status detail text to display.</param>
    /// <returns>A single-line detail string capped to the overlay's maximum display length.</returns>
    private static string TrimDetail(string detail)
    {
        detail = detail.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return detail.Length <= 240 ? detail : detail[..240] + "...";
    }
}
