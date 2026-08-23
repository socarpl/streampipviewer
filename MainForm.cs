namespace CctvPip.App;

internal sealed class MainForm : Form
{
    private const double VideoAspectRatio = 16.0 / 9.0;
    private readonly AppConfig config;
    private readonly Panel stage = new();
    private readonly CameraView cctv1View = new("CCTV1");
    private readonly CameraView cctv2View = new("CCTV2");
    private readonly ContextMenuStrip contextMenu = new();
    private readonly ToolStripMenuItem settingsMenuItem = new();
    private readonly ToolStripMenuItem muteAllMenuItem = new();
    private readonly ToolStripMenuItem unmuteAllMenuItem = new();
    private readonly ToolStripMenuItem toggleMainStreamMuteMenuItem = new();
    private readonly ToolStripMenuItem togglePipStreamMuteMenuItem = new();
    private readonly ToolStripMenuItem swapFeedsMenuItem = new();
    private readonly ToolStripMenuItem fullscreenMenuItem = new();
    private AppSettings currentSettings;
    private HostedStreamProcess? cctv1Player;
    private HostedStreamProcess? cctv2Player;
    private OptionsForm? optionsForm;
    private bool swapped;
    private bool isFullscreen;
    private Rectangle previousBounds;
    private FormBorderStyle previousBorderStyle;
    private FormWindowState previousWindowState;
    private bool previousTopMost;
    private MainWindowMouseHook? mouseHook;

    /// <summary>
    /// Creates the main viewer window, initializes layout controls, context menu actions, mouse hooks, and hosted stream controllers.
    /// </summary>
    /// <param name="config">The loaded application configuration used for stream URLs and layout settings.</param>
    public MainForm(AppConfig config)
    {
        this.config = config;
        currentSettings = AppSettings.FromConfig(config);

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

        cctv1Player = new HostedStreamProcess(cctv1View, StreamId.Cctv1, () => this.config);
        cctv2Player = new HostedStreamProcess(cctv2View, StreamId.Cctv2, () => this.config);

        ApplyLayout();

        Resize += (_, _) => ApplyLayout();
        Shown += (_, _) =>
        {
            cctv1Player.Start();
            cctv2Player.Start();
        };
    }

    /// <summary>
    /// Releases hosted stream processes, auxiliary forms, hooks, context menus, and persisted configuration resources.
    /// </summary>
    /// <param name="disposing">Indicates whether managed resources should be disposed.</param>
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
            config.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Handles application-level keyboard shortcuts before normal WinForms processing.
    /// </summary>
    /// <param name="msg">The current Windows message being processed.</param>
    /// <param name="keyData">The key combination associated with the message.</param>
    /// <returns><see langword="true"/> when the key was handled by this form; otherwise the base result.</returns>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && isFullscreen)
        {
            ExitFullscreen();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Calculates the main stage, MainWindow, and PIP bounds, then resizes embedded stream hosts to match.
    /// </summary>
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
        cctv1Player?.ResizeToHost();
        cctv2Player?.ResizeToHost();
    }

    /// <summary>
    /// Calculates PIP bounds from the current scale and coordinates while clamping them to the visible main area.
    /// </summary>
    /// <param name="mainSize">The current size of the main video stage.</param>
    /// <returns>The clamped PIP rectangle relative to the stage.</returns>
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

    /// <summary>
    /// Updates the settings dialog's maximum PIP coordinate values for the current stage and PIP size.
    /// </summary>
    /// <param name="pipSize">The current PIP size used to compute maximum X and Y values.</param>
    private void UpdateOptionsCoordinateLimits(Size pipSize)
    {
        optionsForm?.SetCoordinateLimits(
            Math.Max(0, stage.ClientSize.Width - pipSize.Width),
            Math.Max(0, stage.ClientSize.Height - pipSize.Height));
    }

    /// <summary>
    /// Builds the right-click context menu and wires all menu items to their viewer actions.
    /// </summary>
    private void BuildContextMenu()
    {
        settingsMenuItem.Text = "Settings";
        settingsMenuItem.Click += (_, _) => ShowOptions();

        muteAllMenuItem.Text = "Mute All";
        muteAllMenuItem.Click += (_, _) => SetAllStreamsMuted(true);

        unmuteAllMenuItem.Text = "Unmute All";
        unmuteAllMenuItem.Click += (_, _) => SetAllStreamsMuted(false);

        toggleMainStreamMuteMenuItem.Click += (_, _) => ToggleMainStreamMuted();
        togglePipStreamMuteMenuItem.Click += (_, _) => TogglePipStreamMuted();

        swapFeedsMenuItem.Text = "Swap feeds";
        swapFeedsMenuItem.Click += (_, _) => SwapStreams();

        fullscreenMenuItem.Click += (_, _) => ToggleFullscreen();

        contextMenu.Opening += (_, _) =>
        {
            fullscreenMenuItem.Text = isFullscreen ? "Exit Fullscreen" : "Fullscreen";
            UpdateAudioMenuItems();
        };

        contextMenu.Items.Add(settingsMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(muteAllMenuItem);
        contextMenu.Items.Add(unmuteAllMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(toggleMainStreamMuteMenuItem);
        contextMenu.Items.Add(togglePipStreamMuteMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(swapFeedsMenuItem);
        contextMenu.Items.Add(fullscreenMenuItem);
    }

    /// <summary>
    /// Assigns the shared context menu to a WinForms control.
    /// </summary>
    /// <param name="control">The control that should display the shared context menu.</param>
    private void AttachContextMenu(Control control)
    {
        control.ContextMenuStrip = contextMenu;
    }

    /// <summary>
    /// Determines whether a mouse-hook action should be handled by this form at the supplied screen point.
    /// </summary>
    /// <param name="screenPoint">The mouse position in screen coordinates.</param>
    /// <returns><see langword="true"/> when the point belongs to the active viewer area and no modal UI should suppress it.</returns>
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

    /// <summary>
    /// Determines whether a screen point is inside the current PIP view.
    /// </summary>
    /// <param name="screenPoint">The mouse position in screen coordinates.</param>
    /// <returns><see langword="true"/> when the point is inside the visible PIP rectangle.</returns>
    private bool IsPointInsidePip(Point screenPoint)
    {
        var pip = swapped ? cctv1View : cctv2View;
        return pip.Visible && pip.RectangleToScreen(pip.ClientRectangle).Contains(screenPoint);
    }

    /// <summary>
    /// Gets the hosted stream process currently displayed as the MainWindow stream.
    /// </summary>
    /// <returns>The current MainWindow stream controller, or <see langword="null"/> if it is not initialized.</returns>
    private HostedStreamProcess? GetMainPlayer()
    {
        return swapped ? cctv2Player : cctv1Player;
    }

    /// <summary>
    /// Gets the hosted stream process currently displayed as the PIP stream.
    /// </summary>
    /// <returns>The current PIP stream controller, or <see langword="null"/> if it is not initialized.</returns>
    private HostedStreamProcess? GetPipPlayer()
    {
        return swapped ? cctv1Player : cctv2Player;
    }

    /// <summary>
    /// Applies the same mute state to both stream host processes.
    /// </summary>
    /// <param name="muted">Set to <see langword="true"/> to mute both streams, or <see langword="false"/> to unmute both streams.</param>
    private void SetAllStreamsMuted(bool muted)
    {
        cctv1Player?.SetMuted(muted);
        cctv2Player?.SetMuted(muted);
        UpdateAudioMenuItems();
    }

    /// <summary>
    /// Toggles mute for whichever stream is currently displayed in the MainWindow role.
    /// </summary>
    private void ToggleMainStreamMuted()
    {
        GetMainPlayer()?.ToggleMuted();
        UpdateAudioMenuItems();
    }

    /// <summary>
    /// Toggles mute for whichever stream is currently displayed in the PIP role.
    /// </summary>
    private void TogglePipStreamMuted()
    {
        GetPipPlayer()?.ToggleMuted();
        UpdateAudioMenuItems();
    }

    /// <summary>
    /// Refreshes the MainWindow and PIP mute menu labels from the current per-stream mute state.
    /// </summary>
    private void UpdateAudioMenuItems()
    {
        UpdateToggleMuteMenuItem(toggleMainStreamMuteMenuItem, GetMainPlayer(), "Main Stream");
        UpdateToggleMuteMenuItem(togglePipStreamMuteMenuItem, GetPipPlayer(), "PIP Stream");
    }

    /// <summary>
    /// Updates one mute toggle menu item to show whether activating it will mute or unmute a stream role.
    /// </summary>
    /// <param name="item">The menu item to update.</param>
    /// <param name="player">The stream controller associated with the displayed role.</param>
    /// <param name="label">The user-facing role label shown in the menu text.</param>
    private static void UpdateToggleMuteMenuItem(ToolStripMenuItem item, HostedStreamProcess? player, string label)
    {
        item.Enabled = player is not null;
        item.Text = player?.IsMuted == true ? $"Unmute {label}" : $"Mute {label}";
    }

    /// <summary>
    /// Shows the non-modal settings dialog, creating it if needed and positioning it near the main window.
    /// </summary>
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

    /// <summary>
    /// Swaps the visual MainWindow and PIP roles without changing which stream process owns each camera feed.
    /// </summary>
    private void SwapStreams()
    {
        swapped = !swapped;
        ApplyLayout();
    }

    /// <summary>
    /// Applies temporary layout settings from the options dialog without writing them to disk.
    /// </summary>
    /// <param name="settings">The draft settings to preview in the main window.</param>
    private void PreviewSettings(AppSettings settings)
    {
        currentSettings = settings;
        ApplyLayout();
    }

    /// <summary>
    /// Saves settings to the config file, reapplies layout, and restarts streams when either source URL changes.
    /// </summary>
    /// <param name="settings">The settings confirmed by the user in the options dialog.</param>
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

    /// <summary>
    /// Discards draft settings and restores the layout from the persisted configuration.
    /// </summary>
    private void CancelSettings()
    {
        currentSettings = AppSettings.FromConfig(config);
        ApplyLayout();
    }

    /// <summary>
    /// Switches between fullscreen and normal windowed mode.
    /// </summary>
    private void ToggleFullscreen()
    {
        if (isFullscreen)
        {
            ExitFullscreen();
            return;
        }

        EnterFullscreen();
    }

    /// <summary>
    /// Saves the normal window state and expands the form to fill the current screen without window chrome.
    /// </summary>
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

    /// <summary>
    /// Restores the saved window state from before fullscreen mode.
    /// </summary>
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

    /// <summary>
    /// Opens the active configuration file and reports failures in a message box.
    /// </summary>
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

    /// <summary>
    /// Selects an initial client size that fits the primary working area while respecting app minimums and maximums.
    /// </summary>
    private void SetInitialSize()
    {
        var working = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var targetWidth = Math.Min(1920, Math.Max(960, working.Width - 80));
        var targetHeight = Math.Min(1166, Math.Max(620, working.Height - 80));
        ClientSize = new Size(targetWidth, targetHeight);
    }

    /// <summary>
    /// Fits a rectangle of the requested aspect ratio inside a bounding rectangle and centers it.
    /// </summary>
    /// <param name="bounds">The available bounding rectangle.</param>
    /// <param name="aspectRatio">The target width divided by height.</param>
    /// <returns>The largest centered rectangle that fits inside <paramref name="bounds"/> with the requested aspect ratio.</returns>
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
