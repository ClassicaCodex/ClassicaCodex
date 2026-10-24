using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// Short synthesized square-wave effects: no network, and nothing on the game
/// loop ever waits for audio.
///
/// The four effects are the Windows game's, byte for byte - a 440 Hz blip for
/// a throw or a spell, a 180 Hz knock for a kill, a 70 Hz thud for a wound, a
/// 660 Hz chime for a victory - written once as tiny WAV files to a temporary
/// folder and handed to afplay, the player every Mac has. Windows plays them
/// from memory with SoundPlayer; the Mac has no in-process equivalent without
/// pulling in an audio framework, and a command-line player started per
/// effect costs a few milliseconds the game never notices.
///
/// A new effect cuts off the one still sounding, as PlaySound does on
/// Windows, so a flurry of javelins makes a rattle rather than a pile-up of
/// players. If the tones cannot be written - a full or read-only temporary
/// folder - macOS's own sounds stand in, the nearest in character.
///
/// Sound is optional: a machine without an audio device, or without afplay
/// (anything that is not a Mac), simply plays nothing. On by default, as on
/// Windows; the game's sound switch and its saved setting decide after that.
/// </summary>
internal sealed class BronzeArcadeSound : IDisposable
{
    private static readonly (int Frequency, double Seconds)[] Effects = { (440, .06), (180, .09), (70, .14), (660, .3) };
    private static readonly string[] SystemStandIns = { "Tink", "Pop", "Basso", "Glass" };

    private string? _folder;
    private string?[]? _clips;
    private Process? _playing;
    private bool _disposed;

    public bool Enabled { get; set; } = true;

    public void Play(int index)
    {
        if (!Enabled || _disposed || !OperatingSystem.IsMacOS()) return;

        try
        {
            var file = Clip(index) ?? $"/System/Library/Sounds/{SystemStandIns[index]}.aiff";
            Stop();

            var start = new ProcessStartInfo("/usr/bin/afplay") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(file);
            _playing = Process.Start(start);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // Sound is optional on systems without an audio device.
        }
    }

    /// <summary>The effect's WAV file, written on first use; null if it could not be.</summary>
    private string? Clip(int index)
    {
        if (_clips != null) return _clips[index];

        _clips = new string?[Effects.Length];
        try
        {
            _folder = Path.Combine(Path.GetTempPath(), $"ClassicaCodex-BronzeAndThunder-{Environment.ProcessId}");
            Directory.CreateDirectory(_folder);

            for (var i = 0; i < Effects.Length; i++)
            {
                var path = Path.Combine(_folder, $"effect-{i}.wav");
                File.WriteAllBytes(path, Wave(Effects[i].Frequency, Effects[i].Seconds));
                _clips[i] = path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The stand-ins play instead.
        }

        return _clips[index];
    }

    /// <summary>8-bit mono at 11,025 Hz, a square wave fading linearly to silence - the Windows game's tone.</summary>
    internal static byte[] Wave(int frequency, double seconds)
    {
        const int rate = 11025;
        var count = (int)(rate * seconds);
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            w.Write("RIFF"u8.ToArray()); w.Write(36 + count); w.Write("WAVEfmt "u8.ToArray());
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate);
            w.Write((short)1); w.Write((short)8); w.Write("data"u8.ToArray()); w.Write(count);
            for (var i = 0; i < count; i++)
            {
                var envelope = 1 - (double)i / count;
                w.Write((byte)(128 + (Math.Sin(i * frequency * 2 * Math.PI / rate) > 0 ? 12 : -12) * envelope));
            }
        }

        return stream.ToArray();
    }

    private void Stop()
    {
        var playing = _playing;
        _playing = null;
        if (playing == null) return;

        try
        {
            if (!playing.HasExited) playing.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // It finished on its own in the meantime.
        }
        finally
        {
            playing.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();

        try
        {
            if (_folder != null && Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A few hundred bytes left in the temporary folder, which the system clears.
        }
    }
}
