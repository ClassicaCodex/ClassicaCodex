namespace ClassicaCodex.UI;

/// <summary>Draws a horizontal bar chart of a work's most frequent words - its stylistic "fingerprint".</summary>
public class FingerprintCanvas : Panel
{
    private List<(string Word, double Frequency)> _words = new();

    public FingerprintCanvas()
    {
        DoubleBuffered = true;
        BackColor = ReadingTheme.Surface;
    }

    public void SetData(List<(string Word, double Frequency)> words)
    {
        _words = words;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        if (_words.Count == 0)
        {
            using var emptyFont = new Font(Font, FontStyle.Italic);
            using var emptyBrush = new SolidBrush(ReadingTheme.MutedText);
            e.Graphics.DrawString("Run an analysis to see a fingerprint here.", emptyFont, emptyBrush, new PointF(8, 8));
            return;
        }

        var maxFreq = _words.Max(w => w.Frequency);
        var rowHeight = Math.Max(Height / _words.Count, 16);
        const int labelWidth = 70;
        var barAreaWidth = Math.Max(Width - labelWidth - 60, 40);

        for (var i = 0; i < _words.Count; i++)
        {
            var (word, freq) = _words[i];
            var y = i * rowHeight;
            var barWidth = maxFreq > 0 ? (float)(freq / maxFreq * barAreaWidth) : 0;

            using var wordBrush = new SolidBrush(ReadingTheme.Text);
            e.Graphics.DrawString(word, Font, wordBrush, new PointF(2, y + 2));

            var barRect = new RectangleF(labelWidth, y + 3, barWidth, rowHeight - 8);
            using var brush = new SolidBrush(Color.CadetBlue);
            e.Graphics.FillRectangle(brush, barRect);

            using var freqBrush = new SolidBrush(ReadingTheme.MutedText);
            e.Graphics.DrawString($"{freq:P2}", Font, freqBrush, new PointF(labelWidth + barWidth + 4, y + 2));
        }
    }
}
