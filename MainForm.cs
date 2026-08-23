using LibVLCSharp.Shared;

namespace CctvPip.App;

internal sealed class MainForm : Form
{
    private const double VideoAspectRatio = 16.0 / 9.0;
    private readonly AppConfig config;
    private readonly LibVLC libVlc;
    private readonly Panel stage = new();
    private readonly CameraView cctv1View = new("CCTV1");
    private readonly CameraView cctv2View = new("CCTV2");
    private readonly ContextMenuStrip contextMenu = new();
    private readonly ToolStripMenuItem settingsMenuItem = new();
    private readonly ToolStripMenuItem swapFeedsMenuItem = new();
    private readonly ToolStripMenuItem fullscreenMenuItem = new();
    private AppSettings currentSettings;
    private StreamPlayer? cctv1Player;
    private StreamPlayer? cctv2Player;
    private OptionsForm? optionsForm;
    private bool swapped;
    private bool isFullscreen;
    private Rectangle previousBounds;
    private FormBorderStyle previousBorderStyle;
    private FormWindowState previousWindowState;
    private bool previousTopMost;
    private MainWindowMouseHook? mouseHook;

    public MainForm(AppConfig config)
    {
        this.config = config;
        currentSettings = AppSettings.FromConfig(config);
        libVlc = new LibVLC(GetLibVlcOptions());

        Text = "Stream PIP Viewer";
        BackColor = Color.Black;
        MinimumSize = new Size(960, 620);
        StartPosition = FormStartPosition.CenterScreen;
        SetInitialSize();

        stage.BackColor = Color.Black;
        stage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        stage.Controls.Add(cctv1View);
        stage.Controls.Add(cctv2View);

        Controls.Add(stage);
        BuildContextMenu();
        mouseHook = new MainWindowMouseHook(this, contextMenu, ToggleFullscreen, SwapStreams, IsPointInsidePip, IsMouseActionAllowed);
        AttachContextMenu(this);
        AttachContextMenu(stage);
        cctv1View.AttachContextMenu(contextMenu);
        cctv2View.AttachContextMenu(contextMenu);

        cctv1Player = new StreamPlayer(libVlc, cctv1View, StreamId.Cctv1, () => this.config);
        cctv2Player = new StreamPlayer(libVlc, cctv2View, StreamId.Cctv2, () => this.config);

        ApplyLayout();

        Resize += (_, _) => ApplyLayout();
        Shown += (_, _) =>
        {
            cctv1Player.Start();
            cctv2Player.Start();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            cctv1Player?.Dispose();
            cctv2Player?.Dispose();
            optionsForm?.Dispose();
            mouseHook?.Dispose();
            mouseHook = null;
            contextMenu.Dispose();
            libVlc.Dispose();
            config.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && isFullscreen)
        {
            ExitFullscreen();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ApplyLayout()
    {
        var available = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        var stageBounds = FitAspect(available, VideoAspectRatio);
        stage.Bounds = stageBounds;

        var main = swapped ? cctv2View : cctv1View;
        var pip = swapped ? cctv1View : cctv2View;

        main.Bounds = new Rectangle(Point.Empty, stage.ClientSize);
        main.BorderStyle = BorderStyle.None;
        main.Padding = Padding.Empty;
        main.BackColor = Color.Black;

        var pipBounds = GetClampedPipBounds(stage.ClientSize);
        pip.Bounds = pipBounds;
        pip.BorderStyle = BorderStyle.FixedSingle;
        pip.BackColor = currentSettings.PipBorderColor;
        pip.Padding = new Padding(2);
        pip.BringToFront();

        UpdateOptionsCoordinateLimits(pipBounds.Size);
    }

    private Rectangle GetClampedPipBounds(Size mainSize)
    {
        var width = Math.Clamp((int)Math.Round(mainSize.Width * currentSettings.PipScale), 160, Math.Max(160, mainSize.Width));
        var height = Math.Max(90, (int)Math.Round(width / VideoAspectRatio));

        if (height > mainSize.Height)
        {
            height = mainSize.Height;
            width = Math.Max(160, (int)Math.Round(height * VideoAspectRatio));
        }

        var maxX = Math.Max(0, mainSize.Width - width);
        var maxY = Math.Max(0, mainSize.Height - height);
        var x = Math.Clamp(currentSettings.PipX, 0, maxX);
        var y = Math.Clamp(currentSettings.PipY, 0, maxY);

        if (x != currentSettings.PipX || y != currentSettings.PipY)
        {
            currentSettings = currentSettings with { PipX = x, PipY = y };
            optionsForm?.RefreshFromSettings(currentSettings);
        }

        return new Rectangle(x, y, width, height);
    }

    private void UpdateOptionsCoordinateLimits(Size pipSize)
    {
        optionsForm?.SetCoordinateLimits(
            Math.Max(0, stage.ClientSize.Width - pipSize.Width),
            Math.Max(0, stage.ClientSize.Height - pipSize.Height));
    }

    private void BuildContextMenu()
    {
        settingsMenuItem.Text = "Settings";
        settingsMenuItem.Click += (_, _) => ShowOptions();

        swapFeedsMenuItem.Text = "Swap feeds";
        swapFeedsMenuItem.Click += (_, _) => SwapStreams();

        fullscreenMenuItem.Click += (_, _) => ToggleFullscreen();

        contextMenu.Opening += (_, _) =>
        {
            fullscreenMenuItem.Text = isFullscreen ? "Exit Fullscreen" : "Fullscreen";
        };

        contextMenu.Items.Add(settingsMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(swapFeedsMenuItem);
        contextMenu.Items.Add(fullscreenMenuItem);
    }

    private void AttachContextMenu(Control control)
    {
        control.ContextMenuStrip = contextMenu;
    }

    private bool IsMouseActionAllowed(Point screenPoint)
    {
        if (!Visible || IsDisposed || contextMenu.Visible)
        {
            return false;
        }

        if (optionsForm is { IsDisposed: false, Visible: true } && optionsForm.Bounds.Contains(screenPoint))
        {
            return false;
        }

        return RectangleToScreen(ClientRectangle).Contains(screenPoint);
    }

    private bool IsPointInsidePip(Point screenPoint)
    {
        var pip = swapped ? cctv1View : cctv2View;
        return pip.Visible && pip.RectangleToScreen(pip.ClientRectangle).Contains(screenPoint);
    }

    private string[] GetLibVlcOptions()
    {
        var file = PropertiesFile.Load(config.Path);
        var value = file.Get("libvlc.options") ?? "--no-video-title-show,--avcodec-hw=any";
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private void ShowOptions()
    {
        if (optionsForm is null || optionsForm.IsDisposed)
        {
            optionsForm = new OptionsForm(
                currentSettings,
                config,
                SwapStreams,
                PreviewSettings,
                SaveSettings,
                CancelSettings,
                OpenConfig);
            optionsForm.StartPosition = FormStartPosition.Manual;
            optionsForm.Location = new Point(
                Math.Min(Right - optionsForm.Width, Screen.FromControl(this).WorkingArea.Right - optionsForm.Width),
                Math.Min(Bottom - optionsForm.Height, Screen.FromControl(this).WorkingArea.Bottom - optionsForm.Height));
            optionsForm.Show(this);
            ApplyLayout();
            return;
        }

        optionsForm.Show();
        optionsForm.BringToFront();
        optionsForm.Focus();
    }

    private void SwapStreams()
    {
        swapped = !swapped;
        ApplyLayout();
    }

    private void PreviewSettings(AppSettings settings)
    {
        currentSettings = settings;
        ApplyLayout();
    }

    private void SaveSettings(AppSettings settings)
    {
        var oldSettings = AppSettings.FromConfig(config);
        currentSettings = settings;
        config.ApplySettings(settings);
        ApplyLayout();

        if (!StringComparer.Ordinal.Equals(oldSettings.Cctv1Url, settings.Cctv1Url) ||
            !StringComparer.Ordinal.Equals(oldSettings.Cctv2Url, settings.Cctv2Url))
        {
            cctv1Player?.Restart();
            cctv2Player?.Restart();
        }
    }

    private void CancelSettings()
    {
        currentSettings = AppSettings.FromConfig(config);
        ApplyLayout();
    }

    private void ToggleFullscreen()
    {
        if (isFullscreen)
        {
            ExitFullscreen();
            return;
        }

        EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        if (isFullscreen)
        {
            return;
        }

        previousBounds = Bounds;
        previousBorderStyle = FormBorderStyle;
        previousWindowState = WindowState;
        previousTopMost = TopMost;

        SuspendLayout();
        isFullscreen = true;
        MinimumSize = Size.Empty;
        WindowState = FormWindowState.Normal;
        FormBorderStyle = FormBorderStyle.None;
        Bounds = Screen.FromControl(this).Bounds;
        TopMost = true;
        ResumeLayout();
        ApplyLayout();
    }

    private void ExitFullscreen()
    {
        if (!isFullscreen)
        {
            return;
        }

        SuspendLayout();
        isFullscreen = false;
        TopMost = previousTopMost;
        FormBorderStyle = previousBorderStyle;
        Bounds = previousBounds;
        WindowState = previousWindowState;
        MinimumSize = new Size(960, 620);
        ResumeLayout();
        ApplyLayout();
    }

    private void OpenConfig()
    {
        try
        {
            config.OpenInDefaultEditor();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open Config Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetInitialSize()
    {
        var working = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var targetWidth = Math.Min(1920, Math.Max(960, working.Width - 80));
        var targetHeight = Math.Min(1166, Math.Max(620, working.Height - 80));
        ClientSize = new Size(targetWidth, targetHeight);
    }

    private static Rectangle FitAspect(Rectangle bounds, double aspectRatio)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var width = bounds.Width;
        var height = (int)Math.Round(width / aspectRatio);

        if (height > bounds.Height)
        {
            height = bounds.Height;
            width = (int)Math.Round(height * aspectRatio);
        }

        var x = bounds.Left + (bounds.Width - width) / 2;
        var y = bounds.Top + (bounds.Height - height) / 2;
        return new Rectangle(x, y, width, height);
    }

}
