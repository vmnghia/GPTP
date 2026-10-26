using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace ManifoldEditor.App.Services;

/// <summary>
/// Plays a sound's WAV bytes from the MPQ (PyDAT's SoundsTab play button; previews spec §3)
/// through Windows' media player. A new play stops a sound still playing.
/// </summary>
sealed class SoundPlayer : IDisposable
{
    readonly MediaPlayer player = new();
    readonly DispatcherQueue? dispatcher = DispatcherQueue.GetForCurrentThread();

    /// <summary>Why Windows couldn't play the last sound, raised on the thread that made the player.</summary>
    public event Action<string>? Failed;

    public SoundPlayer()
    {
        player.MediaFailed += (_, args) =>
        {
            string reason = string.IsNullOrEmpty(args.ErrorMessage) ? args.Error.ToString() : args.ErrorMessage;
            if (dispatcher is null || !dispatcher.TryEnqueue(() => Failed?.Invoke(reason))) Failed?.Invoke(reason);
        };
    }

    /// <summary>Stops a sound still playing, then plays these bytes.</summary>
    public async Task Play(byte[] wav)
    {
        player.Pause();
        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(wav.AsBuffer());
        stream.Seek(0);
        player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
        player.Play();
    }

    public void Dispose() => player.Dispose();
}
