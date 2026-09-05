using System.Media;

namespace ClassicaCodex.UI;

/// <summary>Short synthesized square-wave effects; no files, network or audio thread waits.</summary>
internal sealed class BronzeArcadeSound : IDisposable
{
    private readonly List<(MemoryStream Stream, SoundPlayer Player)> _sounds = new();
    public bool Enabled { get; set; } = true;

    public BronzeArcadeSound()
    {
        foreach (var (frequency, seconds) in new[] { (440, .06), (180, .09), (70, .14), (660, .3) })
        {
            const int rate = 11025;
            var count = (int)(rate * seconds);
            var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
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
            stream.Position = 0;
            _sounds.Add((stream, new SoundPlayer(stream)));
        }
    }

    public void Play(int index)
    {
        if (!Enabled) return;
        try { _sounds[index].Player.Play(); } catch { /* Sound is optional on systems without an audio device. */ }
    }
    public void Dispose() { foreach (var (stream, player) in _sounds) { player.Stop(); player.Dispose(); stream.Dispose(); } }
}
