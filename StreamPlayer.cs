using LibVLCSharp.Shared;
using System.Runtime.CompilerServices;

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



    public StreamPlayer(LibVLC libVlc, CameraView view, StreamId streamId, Func<AppConfig> configProvider)
    {
        this.libVlc = libVlc;
        this.view = view;
        this.streamId = streamId;
        this.configProvider = configProvider;
    }

    public void Start()
    {
        view.ShowNoSignal("NO SIGNAL");
        StartPlayback();
    }

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

    public bool ToggleMuted([CallerMemberName] string callerName = "")
    {
        lock (sync)
        {
            System.Diagnostics.Trace.WriteLine($"[{callerName}] ToggleMuted: {muted} -> {!muted}");
            muted = !muted;
            if (mediaPlayer is not null)
            {
                ApplyMuteState(mediaPlayer, muted);
            }

            return muted;
        }
    }

    public void Dispose()
    {
        disposed = true;
        reconnectCts?.Cancel();
        reconnectCts?.Dispose();
        mediaPlayer?.Stop();
        mediaPlayer?.Dispose();
    }

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

    private void ApplyMuteState(MediaPlayer player, bool muted)
    {
        System.Diagnostics.Trace.WriteLine("ApplyMuteState:" + muted.ToString() + "for player " + RuntimeHelpers.GetHashCode(player));

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
