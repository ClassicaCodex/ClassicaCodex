using System.Drawing.Drawing2D;
using ClassicaCodex.Core;

namespace ClassicaCodex.UI;

/// <summary>Original hand-authored pixel silhouettes, drawn on the arcade's native pixel grid.</summary>
internal sealed class BronzeSprites : IDisposable
{
    private readonly Dictionary<string, Bitmap> _sprites = new();
    private static readonly Color[] Palette = { Color.Transparent, Color.FromArgb(27, 17, 44),
        Color.FromArgb(255, 207, 113), Color.FromArgb(197, 110, 49), Color.FromArgb(246, 151, 116),
        Color.FromArgb(217, 50, 103), Color.FromArgb(102, 240, 216), Color.FromArgb(99, 129, 68),
        Color.FromArgb(173, 112, 197), Color.FromArgb(255, 244, 208) };

    public BronzeSprites()
    {
        Add("hero", new[] {
            "000055550000", "000555555000", "000222220000", "000233324000",
            "000234994000", "000034440000", "002222330000", "022323344300",
            "223323344440", "233322330000", "023333330000", "000555550000",
            "000555550000", "000440440000", "000330330000", "003330333000" });
        Add("Serpent", new[] {
            "0000000777700000", "0000007272770000", "0000007997770000", "0000007777700000",
            "0000000075500000", "0000777070000000", "0077227770000000", "0777777777770000",
            "7722777772277000", "7777000077777000", "0777777777770770", "0007777777700077" });
        Add("Harpy", new[] {
            "8000000440000008", "8800002442000088", "8880004994000888", "8888004444008888",
            "0888805555088880", "0088885555888800", "0008888888888000", "0000888888880000",
            "0000088888800000", "0000008888000000", "0000020000200000", "0000220000220000" });
        Add("Boar", new[] {
            "0000030000030000", "0000333333330000", "0003333333333300", "0033383333333330",
            "0333333333333333", "0333333333993333", "0333333333333399", "0033333333333990",
            "0003333333333300", "0000330000330000", "0000330000330000", "0000110000110000" });
        Add("Cyclops", new[] {
            "0000333333000000", "0003444444300000", "0004499944400000", "0004491944400000",
            "0004499944400000", "0003444444300000", "0000441144000000", "0034444444430000",
            "0344333333443000", "3443333333344300", "3443333333344300", "0443333333344000",
            "0003355555330000", "0003555555530000", "0000440044000000", "0003330033300000" });
        Add("Gorgon", new[] {
            "0077070070770000", "0727777777277000", "0777727777770000", "0077444477700000",
            "0074499447700000", "0007444470000000", "0000777700000000", "0008888880000000",
            "0088888888000000", "0888888888800000", "0088888888000000", "0007777770000000",
            "0077227777000000", "0777777777770000", "0077777770077700", "0000000000007770" });
        Add("Hydra", new[] {
            "0077700000077700", "0727700770077270", "0797707227077970", "0077707997077700",
            "0007700770077000", "0007770770777000", "0000777777770000", "0000077777700000",
            "0000772277770000", "0007777777777000", "0077777777777700", "0777707777077770",
            "0777007777007770", "0770007777000770", "0077777777777700", "0007770000777000" });
    }

    private void Add(string name, string[] rows)
    {
        var bitmap = new Bitmap(rows.Max(r => r.Length), rows.Length);
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++) bitmap.SetPixel(x, y, Palette[rows[y][x] - '0']);
        _sprites[name] = bitmap;
    }

    public void Draw(Graphics g, string name, float x, float y, bool flip, bool boss, float time, bool flash)
    {
        var sprite = _sprites[name];
        var scale = boss ? 2 : 1;
        var bob = (int)(Math.Sin(time * 9) * 1.2);
        var state = g.Save();
        g.TranslateTransform((int)x, (int)y + bob);
        if (flip) g.ScaleTransform(-1, 1);
        g.DrawImage(sprite, new Rectangle(-sprite.Width * scale / 2, -sprite.Height * scale + 5,
            sprite.Width * scale, sprite.Height * scale), 0, 0, sprite.Width, sprite.Height, GraphicsUnit.Pixel);
        if (flash)
        {
            using var pen = new Pen(Color.FromArgb(255, 244, 208));
            g.DrawRectangle(pen, -sprite.Width * scale / 2, -sprite.Height * scale + 5, sprite.Width * scale, sprite.Height * scale);
        }
        g.Restore(state);
    }

    public void Dispose() { foreach (var sprite in _sprites.Values) sprite.Dispose(); }
}
