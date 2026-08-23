using LibVLCSharp.Shared;

namespace CctvPip.App;

internal sealed class StreamPlayer : IDisposable
{
    private const int MutedVolume = 0;
    private const int UnmutedVolume = 100;
    private readonly LibVLC libVlc;
    private readonly CameraView view;
    private readonly Func<AppConfig> configProvider;
    private readonly StreamId streamId;
    private readonly object sync = new();

    private MediaPlayer? mediaPlayer;
    private CancellationTokenSource? reconnectCts;
    private bool hasPlayed;
    private int playbackGeneration;
    private int liveGeneration;
    private int lastActiveAudioTrack = -1;
    private bool muted = true;
    private bool disposed;

    /// <summary>
    /// Creates an in-process LibVLC stream player bound to one camera view and one configured stream.
    /// </summary>
    /// <param name="libVlc">The LibVLC runtime instance used to create media players.</param>
    /// <param name="view">The camera view that receives video output and status updates.</param>
    /// <param name="streamId">The configured stream to play.</param>
    /// <param name="configProvider">A function that returns the latest application configuration.</param>
    public StreamPlayer(LibVLC libVlc, CameraView view, StreamId streamId, Func<AppConfig> configProvider)
    {
        this.libVlc = libVlc;
        this.view = view;
        this.streamId = streamId;
        this.configProvider = configProvider;
    }

    /// <summary>
    /// Shows the initial no-signal state and starts playback for the configured stream.
    /// </summary>
    public void Start()
    {
        view.ShowNoSignal("NO SIGNAL");
        StartPlayback();
    }

    /// <summary>
    /// Cancels reconnect work, resets playback state, and starts the stream again from the current configuration.
    /// </summary>
    public void Restart()
    {
        reconnectCts?.Cancel();
        hasPlayed = false;
        liveGeneration = 0;
        view.ShowNoSignal("NO SIGNAL");
        StartPlayback();
    }

    public bool IsMuted
    {
        get
        {
            lock (sync)
            {
                return muted;
            }
        }
    }

    /// <summary>
    /// Stores and applies the desired mute state to the current media player.
    /// </summary>
    /// <param name="muted">Set to <see langword="true"/> to disable audio, or <see langword="false"/> to restore audio.</param>
    public void SetMuted(bool muted)
    {
        lock (sync)
        {
            this.muted = muted;
            if (mediaPlayer is not null)
            {
                ApplyMuteState(mediaPlayer, muted);
            }
        }
    }

    /// <summary>
    /// Flips the current mute state and applies it to the active media player.
    /// </summary>
    /// <returns>The new mute state after toggling.</returns>
    public bool ToggleMuted()
    {
        lock (sync)
        {
            muted = !muted;
            if (mediaPlayer is not null)
            {
                ApplyMuteState(mediaPlayer, muted);
            }

            return muted;
        }
    }

    /// <summary>
    /// Cancels reconnect work, stops playback, and releases LibVLC media-player resources.
    /// </summary>
    public void Dispose()
    {
        disposed = true;
        reconnectCts?.Cancel();
        reconnectCts?.Dispose();
        mediaPlayer?.Stop();
        mediaPlayer?.Dispose();
    }

    /// <summary>
    /// Creates a new LibVLC media player, attaches event handlers, applies mute state, and starts the configured media item.
    /// </summary>
    /// <returns>The playback generation number assigned to this start attempt.</returns>
    private int StartPlayback()
    {
        if (disposed)
        {
            return playbackGeneration;
        }

        var definition = configProvider().GetStream(streamId);
        int generation;
        MediaPlayer player;
        RunOnUi(() => view.ShowNoSignal("NO SIGNAL"));

        lock (sync)
        {
            generation = ++playbackGeneration;
            mediaPlayer?.Stop();
            mediaPlayer?.Dispose();
            player = new MediaPlayer(libVlc)
            {
                EnableHardwareDecoding = true
            };
            ApplyMuteState(player, muted);

            player.Playing += (_, _) =>
            {
                lock (sync)
                {
                    if (ReferenceEquals(mediaPlayer, player))
                    {
                        ApplyMuteState(player, muted);
                    }
                }

                hasPlayed = true;
                liveGeneration = generation;
                RunOnUi(view.ShowLive);
            };
            player.EncounteredError += (_, _) => BeginReconnect("Decoder/player failure");
            player.EndReached += (_, _) => BeginReconnect("Stream ended");

            mediaPlayer = player;
            view.VideoView.MediaPlayer = player;
        }

        try
        {
            using var media = new Media(libVlc, new Uri(definition.Url));
            foreach (var option in definition.PlayerOptions)
            {
                media.AddOption(option);
            }

            if (!player.Play(media))
            {
                BeginReconnect("Player rejected the RTSP media item");
            }
        }
        catch (Exception ex)
        {
            BeginReconnect(ex.Message);
        }

        return generation;
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
    /// Records the current active audio track so it can be restored after muting disables audio tracks.
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
    /// Starts a fresh reconnect loop for the most recent playback failure reason.
    /// </summary>
    /// <param name="reason">The user-facing or technical reason that triggered reconnect handling.</param>
    private void BeginReconnect(string reason)
    {
        if (disposed)
        {
            return;
        }

        lock (sync)
        {
            reconnectCts?.Cancel();
            reconnectCts?.Dispose();
            reconnectCts = new CancellationTokenSource();
            _ = ReconnectAsync(reason, reconnectCts.Token);
        }
    }

    /// <summary>
    /// Attempts to reconnect the stream according to the configured retry count and delay.
    /// </summary>
    /// <param name="initialReason">The failure reason that started the reconnect loop.</param>
    /// <param name="token">A cancellation token used to stop the reconnect loop when playback restarts or the player is disposed.</param>
    /// <returns>A task that completes when reconnect succeeds, fails permanently, or is cancelled.</returns>
    private async Task ReconnectAsync(string initialReason, CancellationToken token)
    {
        var config = configProvider();
        var attempts = config.ReconnectAttempts;
        var delay = config.ReconnectDelayMs;
        var latestReason = initialReason;

        for (var attempt = 1; attempt <= attempts && !token.IsCancellationRequested; attempt++)
        {
            RunOnUi(() => view.ShowNoSignal("NO SIGNAL", $"Reconnect attempt {attempt}/{attempts}. {latestReason}"));

            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                var generation = StartPlayback();
                await Task.Delay(1200, token).ConfigureAwait(false);

                if (liveGeneration >= generation)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                latestReason = ex.Message;
            }
        }

        var finalMessage = hasPlayed
            ? "NO SIGNAL. Reconnection failed"
            : "NO SIGNAL - Failed to connect";

        RunOnUi(() => view.ShowNoSignal(finalMessage, latestReason));
    }

    /// <summary>
    /// Executes an action on the camera view's UI thread when needed.
    /// </summary>
    /// <param name="action">The UI update to run.</param>
    private void RunOnUi(Action action)
    {
        if (view.IsDisposed)
        {
            return;
        }

        if (view.InvokeRequired)
        {
            try
            {
                view.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        action();
    }
}
