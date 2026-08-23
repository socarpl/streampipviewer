using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace CctvPip.StreamHost;

internal sealed class StreamHostForm : Form
{
    private const int HostCommandMessage = 0x8000 + 321;
    private const int CommandMute = 1;
    private const int CommandUnmute = 2;
    private const int CommandRestart = 3;
    private const int CommandShutdown = 4;
    private const int MutedVolume = 0;
    private const int UnmutedVolume = 100;

    private readonly HostOptions options;
    private readonly LibVLC libVlc;
    private readonly VideoView videoView = new();
    private readonly Label statusLabel = new();
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem toggleMuteMenuItem = new();
    private readonly ToolStripMenuItem restartMenuItem = new();
    private MediaPlayer? mediaPlayer;
    private bool muted;
    private int lastActiveAudioTrack = -1;

    /// <summary>
    /// Creates a one-stream playback window that can run standalone or be embedded by the main app.
    /// </summary>
    /// <param name="options">The resolved stream URL, LibVLC options, media options, startup mute state, and hosting mode.</param>
    public StreamHostForm(HostOptions options)
    {
        this.options = options;
        muted = options.StartMuted;
        libVlc = new LibVLC(options.LibVlcOptions);

        Text = $"{options.Label} - {(muted ? "Muted" : "Unmuted")}";
        BackColor = Color.Black;
        ClientSize = new Size(960, 540);
        StartPosition = options.Hosted ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
        ShowInTaskbar = !options.Hosted;

        if (options.Hosted)
        {
            FormBorderStyle = FormBorderStyle.None;
            Location = new Point(-32000, -32000);
        }
        else
        {
            MinimumSize = new Size(480, 270);
        }

        videoView.BackColor = Color.Black;
        videoView.Dock = DockStyle.Fill;

        statusLabel.BackColor = Color.FromArgb(210, 0, 0, 0);
        statusLabel.ForeColor = Color.White;
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 14, FontStyle.Bold);
        statusLabel.Padding = new Padding(16);

        Controls.Add(statusLabel);
        Controls.Add(videoView);
        statusLabel.BringToFront();

        if (!options.Hosted)
        {
            BuildMenu();
            ContextMenuStrip = menu;
            videoView.ContextMenuStrip = menu;
            statusLabel.ContextMenuStrip = menu;
        }

        Shown += (_, _) => StartPlayback();
    }

    /// <summary>
    /// Handles standalone keyboard shortcuts for mute toggle and stream restart.
    /// </summary>
    /// <param name="msg">The current Windows message being processed.</param>
    /// <param name="keyData">The key combination associated with the message.</param>
    /// <returns><see langword="true"/> when the key was handled by this form; otherwise the base result.</returns>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.M)
        {
            SetMuted(!muted);
            return true;
        }

        if (keyData == Keys.R)
        {
            Restart();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Receives host-control window messages from the main app and dispatches them before normal message processing.
    /// </summary>
    /// <param name="m">The Windows message received by this form.</param>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == HostCommandMessage)
        {
            HandleHostCommand(m.WParam.ToInt32());
            return;
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// Stops playback and releases menu, media player, and LibVLC resources.
    /// </summary>
    /// <param name="disposing">Indicates whether managed resources should be disposed.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            mediaPlayer?.Stop();
            mediaPlayer?.Dispose();
            menu.Dispose();
            libVlc.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Builds the standalone right-click menu used when the host is not embedded.
    /// </summary>
    private void BuildMenu()
    {
        toggleMuteMenuItem.Click += (_, _) => SetMuted(!muted);
        restartMenuItem.Text = "Restart";
        restartMenuItem.Click += (_, _) => Restart();
        menu.Opening += (_, _) => UpdateUiState();
        menu.Items.Add(toggleMuteMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(restartMenuItem);
    }

    /// <summary>
    /// Creates a new LibVLC media player, attaches playback state handlers, applies mute state, and starts the stream.
    /// </summary>
    private void StartPlayback()
    {
        ShowStatus("STARTING");

        mediaPlayer?.Stop();
        mediaPlayer?.Dispose();

        var player = new MediaPlayer(libVlc)
        {
            EnableHardwareDecoding = true
        };

        player.Playing += (_, _) =>
        {
            BeginInvoke(() =>
            {
                statusLabel.Visible = false;
                ApplyMuteState(player, muted);
                UpdateUiState();
            });
        };
        player.EncounteredError += (_, _) => BeginInvoke(() => ShowStatus("PLAYER ERROR"));
        player.EndReached += (_, _) => BeginInvoke(() => ShowStatus("STREAM ENDED"));

        mediaPlayer = player;
        videoView.MediaPlayer = player;
        ApplyMuteState(player, muted);

        try
        {
            using var media = new Media(libVlc, new Uri(options.Url));
            foreach (var option in options.MediaOptions)
            {
                media.AddOption(option);
            }

            if (!player.Play(media))
            {
                ShowStatus("PLAY REJECTED");
            }
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
        }
    }

    /// <summary>
    /// Restarts playback for the current stream URL and media options.
    /// </summary>
    private void Restart()
    {
        StartPlayback();
    }

    /// <summary>
    /// Handles a command sent by the main app to mute, unmute, restart, or close this host.
    /// </summary>
    /// <param name="command">The command identifier received in the host-control window message.</param>
    private void HandleHostCommand(int command)
    {
        switch (command)
        {
            case CommandMute:
                SetMuted(true);
                break;
            case CommandUnmute:
                SetMuted(false);
                break;
            case CommandRestart:
                Restart();
                break;
            case CommandShutdown:
                BeginInvoke(Close);
                break;
        }
    }

    /// <summary>
    /// Stores and applies the desired mute state for this single-stream host process.
    /// </summary>
    /// <param name="muted">Set to <see langword="true"/> to disable audio, or <see langword="false"/> to restore audio.</param>
    private void SetMuted(bool muted)
    {
        this.muted = muted;
        if (mediaPlayer is not null)
        {
            ApplyMuteState(mediaPlayer, muted);
        }

        UpdateUiState();
    }

    /// <summary>
    /// Applies the requested mute state using volume and audio-track selection.
    /// </summary>
    /// <param name="player">The media player whose audio state should be changed.</param>
    /// <param name="muted">Set to <see langword="true"/> to mute audio, or <see langword="false"/> to restore audio.</param>
    private void ApplyMuteState(MediaPlayer player, bool muted)
    {
        if (muted)
        {
            RememberActiveAudioTrack(player);
            player.Volume = MutedVolume;
            player.SetAudioTrack(-1);
            return;
        }

        player.Volume = UnmutedVolume;
        RestoreAudioTrack(player);
    }

    /// <summary>
    /// Records the currently active audio track so it can be restored after muting disables audio tracks.
    /// </summary>
    /// <param name="player">The media player whose active audio track should be remembered.</param>
    private void RememberActiveAudioTrack(MediaPlayer player)
    {
        var currentTrack = player.AudioTrack;
        if (currentTrack >= 0)
        {
            lastActiveAudioTrack = currentTrack;
        }
    }

    /// <summary>
    /// Restores the remembered audio track, or selects the first available audio track when the remembered track is unavailable.
    /// </summary>
    /// <param name="player">The media player whose audio track should be restored.</param>
    private void RestoreAudioTrack(MediaPlayer player)
    {
        if (lastActiveAudioTrack >= 0 && player.SetAudioTrack(lastActiveAudioTrack))
        {
            return;
        }

        var firstAvailableTrack = GetFirstAvailableAudioTrack(player);
        if (firstAvailableTrack >= 0)
        {
            lastActiveAudioTrack = firstAvailableTrack;
            player.SetAudioTrack(firstAvailableTrack);
        }
    }

    /// <summary>
    /// Finds the first playable audio track exposed by LibVLC for a media player.
    /// </summary>
    /// <param name="player">The media player whose audio track descriptions should be inspected.</param>
    /// <returns>The first non-negative audio track ID, or <c>-1</c> when no audio track is available.</returns>
    private static int GetFirstAvailableAudioTrack(MediaPlayer player)
    {
        var descriptions = player.AudioTrackDescription;
        if (descriptions is null)
        {
            return -1;
        }

        foreach (var track in descriptions)
        {
            if (track.Id >= 0)
            {
                return track.Id;
            }
        }

        return -1;
    }

    /// <summary>
    /// Shows an overlay status message with the stream label and process ID.
    /// </summary>
    /// <param name="message">The status message to display over the video surface.</param>
    private void ShowStatus(string message)
    {
        statusLabel.Text = $"{options.Label}{Environment.NewLine}{message}{Environment.NewLine}PID {Environment.ProcessId}";
        statusLabel.Visible = true;
        statusLabel.BringToFront();
        UpdateUiState();
    }

    /// <summary>
    /// Updates the window title and standalone mute menu label from the current mute state.
    /// </summary>
    private void UpdateUiState()
    {
        Text = $"{options.Label} - {(muted ? "Muted" : "Unmuted")} - PID {Environment.ProcessId}";
        toggleMuteMenuItem.Text = muted ? "Unmute" : "Mute";
    }
}
