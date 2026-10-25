using System;
using System.Threading.Tasks;
using NAudio.Wave;

namespace MiniCloClo;

// Short tones made in memory, played through CloClo's one audio session
// like everything else it plays. "Listening" is the cue to start speaking: a
// quick rising two-note chime, much shorter than saying "Listening.", so
// the microphone opens almost as soon as the button is held.
public static class Earcon
{
    private const int SampleRate = 44100;

    public static Task ListeningAsync() => PlayAsync((660, 0.07), (990, 0.09));

    // Before a reminder (#39): a gentle falling two-note bell, longer and
    // lower than the listening chime so the two aren't confused.
    public static Task ReminderAsync() => PlayAsync((988, 0.16), (740, 0.24));

    // Each note is a sine with a short fade in and out (no clicks), played
    // one after another. Completes when playback ends, at most a second.
    private static async Task PlayAsync(params (double Frequency, double Seconds)[] notes)
    {
        var total = 0;
        foreach (var (_, seconds) in notes) total += (int)(SampleRate * seconds);
        var samples = new float[total];
        var offset = 0;
        foreach (var (frequency, seconds) in notes)
        {
            var count = (int)(SampleRate * seconds);
            var fade = SampleRate / 200; // 5 ms
            for (var i = 0; i < count; i++)
            {
                var envelope = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)fade);
                samples[offset + i] = (float)(0.25 * envelope * Math.Sin(2 * Math.PI * frequency * i / SampleRate));
            }
            offset += count;
        }

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WasapiPlayer? player = null;
        try
        {
            player = new WasapiPlayerBuilder().Build();
            player.PlaybackStopped += (_, _) => finished.TrySetResult();
            player.Init(new ArraySampleProvider(samples).ToWaveProvider());
            player.Play();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // No output device: the label and the blue badge still show it.
        }
        finally
        {
            if (player != null)
            {
                try { await player.DisposeAsync(); } catch { /* the device went away */ }
            }
        }
    }

    private sealed class ArraySampleProvider(float[] samples) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var count = Math.Min(buffer.Length, samples.Length - _position);
            samples.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }
}
