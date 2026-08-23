using LibVLCSharp.Shared;

namespace CctvPip.App;

internal sealed class StreamPlayer : IDisposable
{
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
        RunOnUi(() => view.ShowNoSignal("NO SIGNAL"));

        lock (sync)
        {
            generation = ++playbackGeneration;
            mediaPlayer?.Stop();
            mediaPlayer?.Dispose();
            mediaPlayer = new MediaPlayer(libVlc)
            {
                EnableHardwareDecoding = true
            };

            mediaPlayer.Playing += (_, _) =>
            {
                hasPlayed = true;
                liveGeneration = generation;
                RunOnUi(view.ShowLive);
            };
            mediaPlayer.EncounteredError += (_, _) => BeginReconnect("Decoder/player failure");
            mediaPlayer.EndReached += (_, _) => BeginReconnect("Stream ended");

            view.VideoView.MediaPlayer = mediaPlayer;
        }

        try
        {
            using var media = new Media(libVlc, new Uri(definition.Url));
            foreach (var option in definition.PlayerOptions)
            {
                media.AddOption(option);
            }

            if (!mediaPlayer.Play(media))
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
