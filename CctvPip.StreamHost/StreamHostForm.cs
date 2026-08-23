using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using System.Runtime.CompilerServices;

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

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == HostCommandMessage)
        {
            HandleHostCommand(m.WParam.ToInt32());
            return;
        }

        base.WndProc(ref m);
    }

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

    private void Restart()
    {
        StartPlayback();
    }

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

    private void SetMuted(bool muted, [CallerMemberName] string callerName = "")
    {
        this.muted = muted;
        if (mediaPlayer is not null)
        {
            ApplyMuteState(mediaPlayer, muted);
        }

        System.Diagnostics.Trace.WriteLine($"[{callerName}] {options.Label} muted={muted}");
        UpdateUiState();
    }

    private void ApplyMuteState(MediaPlayer player, bool muted)
    {
        System.Diagnostics.Trace.WriteLine($"{options.Label} ApplyMuteState={muted} player={RuntimeHelpers.GetHashCode(player)} process={Environment.ProcessId}");

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

    private void RememberActiveAudioTrack(MediaPlayer player)
    {
        var currentTrack = player.AudioTrack;
        if (currentTrack >= 0)
        {
            lastActiveAudioTrack = currentTrack;
        }
    }

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

    private void ShowStatus(string message)
    {
        statusLabel.Text = $"{options.Label}{Environment.NewLine}{message}{Environment.NewLine}PID {Environment.ProcessId}";
        statusLabel.Visible = true;
        statusLabel.BringToFront();
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        Text = $"{options.Label} - {(muted ? "Muted" : "Unmuted")} - PID {Environment.ProcessId}";
        toggleMuteMenuItem.Text = muted ? "Unmute" : "Mute";
    }
}
