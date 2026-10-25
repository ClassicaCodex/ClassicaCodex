using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassicaCodex.Mac.Features.WordStudy;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// Draws a horizontal bar chart of a work's most frequent words - its
/// stylistic "fingerprint".
///
/// The Mac counterpart of the Windows FingerprintCanvas: one row per word,
/// the bar's length its relative frequency against the commonest word's, the
/// percentage after it. The words are set in the reading face, since they are
/// Greek or Latin; the colours are the palette's and are looked up at each
/// paint, so the chart follows the appearance when it changes.
/// </summary>
public sealed class FingerprintCanvas : Control
{
    /// <summary>The shortest a row is drawn, as on Windows, so a squeezed chart scrolls rather than overlaps.</summary>
    private const double MinRowHeight = 18;

    /// <summary>The tallest, so a chart given a whole pane does not draw bars as thick as buttons.</summary>
    private const double MaxRowHeight = 24;

    private IReadOnlyList<(string Word, double Frequency)> _words = Array.Empty<(string, double)>();

    private readonly FontFamily _wordFont = ToolPane.Font("Cc.SourceFont");

    public FingerprintCanvas()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>What is drawn, for the tests.</summary>
    internal IReadOnlyList<(string Word, double Frequency)> Words => _words;

    public void SetData(IReadOnlyList<(string Word, double Frequency)> words)
    {
        _words = words;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(0, _words.Count == 0 ? 40 : _words.Count * MinRowHeight + 8);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var ink = Brush("Cc.Ink");
        var muted = Brush("Cc.InkMuted");

        if (_words.Count == 0)
        {
            var empty = new FormattedText(
                "Run an analysis to see a fingerprint here.",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Italic), 13, muted);
            context.DrawText(empty, new Point(14, 12));
            return;
        }

        var bar = Brush("Cc.Verdigris");
        var rule = new Pen(Brush("Cc.Rule"), 1);

        var labels = _words
            .Select(w => new FormattedText(w.Word, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(_wordFont), 15, ink))
            .ToList();

        // The label column is as wide as the longest word, and never narrower
        // than the Windows chart's fixed seventy pixels.
        var labelWidth = Math.Max(70, labels.Max(l => l.Width) + 26);
        var maxFrequency = _words.Max(w => w.Frequency);
        var rowHeight = Math.Clamp((Bounds.Height - 8) / _words.Count, MinRowHeight, MaxRowHeight);
        var barArea = Math.Max(Bounds.Width - labelWidth - 74, 40);
        const double top = 4;

        context.DrawLine(rule, new Point(labelWidth - 0.5, top), new Point(labelWidth - 0.5, top + rowHeight * _words.Count));

        for (var i = 0; i < _words.Count; i++)
        {
            var (_, frequency) = _words[i];
            var y = top + i * rowHeight;
            var label = labels[i];

            context.DrawText(label, new Point(labelWidth - 12 - label.Width, y + (rowHeight - label.Height) / 2));

            var barWidth = maxFrequency > 0 ? frequency / maxFrequency * barArea : 0;
            var barHeight = Math.Max(rowHeight - 8, 6);
            var barRect = new Rect(labelWidth, y + (rowHeight - barHeight) / 2, Math.Max(barWidth, 1), barHeight);
            context.DrawRectangle(bar, null, barRect, 3, 3);

            var percent = new FormattedText(
                frequency.ToString("P2", CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11.5, muted);
            context.DrawText(percent, new Point(labelWidth + barWidth + 6, y + (rowHeight - percent.Height) / 2));
        }
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;
}
