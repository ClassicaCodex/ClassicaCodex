using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ClassicaCodex.Mac.Common;

/// <summary>
/// Keeps every line of a wrapped passage the same distance from the next.
///
/// Avalonia sets each line's baseline from the tallest face drawing on that
/// line, even when LineHeight fixes how tall the line is. A character the
/// reading face lacks is drawn from another face, and on Windows that face
/// is nearly always Segoe UI, which stands half as tall again as Palatino
/// Linotype above the baseline. So the one line holding that character sits
/// lower than its neighbours and runs into the line under it.
///
/// Palatino Linotype has no U+02BC, the elision mark of every Perseus Greek
/// text, and no lunate sigma or combining breathings either: 229,000 of
/// 801,000 Greek passages in a full library hold one of them, and Middle High
/// German, Old Norse and the Egyptian transliterations are much the same.
///
/// The cure is to start every line from the interface face's measurements.
/// The block's own face is only used to measure a line, so it is set to the
/// interface face, and the text goes in runs that carry the reading face.
/// Every line then has the same ascent before anything is drawn in it, and a
/// borrowed glyph has nothing taller to bring. Segoe UI, Segoe UI Historic
/// and Segoe UI Symbol share their measurements, and of the faces Windows
/// borrows from for what Palatino Linotype and Georgia lack in a full
/// library, only Sans Serif Collection stands taller, for fewer than one
/// borrowed character in two hundred.
///
/// Measured that way, every line sits lower in its height than the reading
/// face alone would put it, and the last line's descenders hang past the
/// block. So the block is lifted by exactly that much and given the same
/// back below: the text lands where it always did, the row is as tall as it
/// was, and nothing is cut off.
///
/// Only for blocks that fix their LineHeight. Without one, a line is as tall
/// as its tallest face, and measuring from the interface face would make
/// every line taller rather than steady.
/// </summary>
public static class SteadyLines
{
    private static readonly ConcurrentDictionary<string, double> LiftPerEm = new();
    private static readonly ConditionalWeakTable<TextBlock, StrongBox<double>> Lifted = new();

    /// <summary>
    /// Sets <paramref name="text"/> in <paramref name="face"/>, steadied. Use
    /// in place of Text and FontFamily, after FontSize, LineHeight and Margin.
    /// </summary>
    public static void SetText(TextBlock block, string? text, FontFamily face)
    {
        block.Inlines = new InlineCollection { new Run(text ?? string.Empty) { FontFamily = face } };
        Steady(block, face);
    }

    /// <summary>
    /// For a block built from runs that each name their own face: measures
    /// its lines from the interface face, lifted to where
    /// <paramref name="face"/> would have put them. A run left to inherit the
    /// block's face would be drawn in the interface face, so give each one a
    /// face. Call after FontSize, LineHeight and Margin are set.
    /// </summary>
    public static void Steady(TextBlock block, FontFamily face, double? fontSize = null)
    {
        block.FontFamily = FontFamily.Default;

        // A block that is filled again - the collation's detail, say - is
        // lifted from where it started, not from where the last lift left it.
        var lift = LiftFor(face) * (fontSize ?? block.FontSize);
        var previous = Lifted.TryGetValue(block, out var box) ? box.Value : 0;
        if (lift == previous) return;

        var margin = block.Margin;
        block.Margin = new Thickness(margin.Left, margin.Top + previous - lift, margin.Right, margin.Bottom - previous + lift);
        Lifted.AddOrUpdate(block, new StrongBox<double>(lift));
    }

    /// <summary>A face from the palette - Cc.SourceFont and the like.</summary>
    public static FontFamily Face(string key) => (FontFamily)Application.Current!.FindResource(key)!;

    /// <summary>The text of a block filled by <see cref="SetText"/>, or by Text.</summary>
    public static string TextOf(TextBlock block) =>
        block.Inlines is { Count: > 0 } inlines
            ? string.Concat(inlines.OfType<Run>().Select(run => run.Text))
            : block.Text ?? string.Empty;

    /// <summary>
    /// How much lower, in ems, a line sits when it is measured from the
    /// interface face as well as <paramref name="face"/>.
    ///
    /// Avalonia centres a line's ascent and descent in its LineHeight, which
    /// puts the baseline at half of (ascent - descent + LineHeight), using
    /// the largest ascent and descent among the faces on the line. The
    /// difference between the two baselines is all that is needed.
    /// </summary>
    internal static double LiftFor(FontFamily face) => LiftPerEm.GetOrAdd(face.ToString(), _ =>
    {
        if (!TryMeasure(new Typeface(face), out var readingAscent, out var readingDescent)
            || !TryMeasure(new Typeface(FontFamily.Default), out var interfaceAscent, out var interfaceDescent))
        {
            return 0;
        }

        var ascent = Math.Max(readingAscent, interfaceAscent);
        var descent = Math.Max(readingDescent, interfaceDescent);
        return ((ascent - readingAscent) - (descent - readingDescent)) / 2;
    });

    private static bool TryMeasure(Typeface typeface, out double ascent, out double descent)
    {
        ascent = descent = 0;
        if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs)) return false;

        var metrics = glyphs.Metrics;
        if (metrics.DesignEmHeight <= 0) return false;

        ascent = -metrics.Ascent / (double)metrics.DesignEmHeight;
        descent = metrics.Descent / (double)metrics.DesignEmHeight;
        return true;
    }
}
