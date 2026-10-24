using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Where a setup step stands. Four states and a fifth for "running", because
/// the Windows forms learned the hard way that two are not enough: a step
/// that has content but never finished, a folder that will work but is short
/// of room, an index built once and since outgrown - each read as finished
/// when the only marks were a tick and a cross.
/// </summary>
internal enum StepState
{
    /// <summary>Not done yet. Not an error - most steps on a new library are here.</summary>
    Pending,
    Complete,

    /// <summary>Usable, but worth a look: partly loaded, out of date, synced, tight on room.</summary>
    Attention,

    /// <summary>Cannot be used as it stands.</summary>
    Problem,
    Running
}

/// <summary>
/// A step's state as a small mark: a filled disc with a tick when it is done,
/// a triangle when it wants attention, an empty ring when it is simply still
/// to do. The ring rather than the red cross the Windows forms use for "not
/// yet", because on a new library that is every step, and a column of red
/// crosses greets a newcomer with what looks like a list of failures.
/// </summary>
internal sealed class StateMark : Decorator
{
    private readonly double _size;
    private StepState? _state;

    public StateMark(double size, StepState state = StepState.Pending)
    {
        _size = size;
        Width = size;
        Height = size;
        VerticalAlignment = VerticalAlignment.Center;
        State = state;
    }

    public StepState State
    {
        get => _state ?? StepState.Pending;
        set
        {
            if (_state == value) return;
            _state = value;
            Child = Build(value);
        }
    }

    private Control Build(StepState state)
    {
        switch (state)
        {
            case StepState.Complete:
            case StepState.Running:
            {
                var disc = new Border { CornerRadius = new CornerRadius(_size / 2) };
                disc.Bind(Border.BackgroundProperty, disc.GetResourceObservable(state == StepState.Complete ? "Cc.Verdigris" : "Cc.Accent"));

                var glyph = new PathIcon
                {
                    Data = Glyphs.Get(state == StepState.Complete ? "Check" : "Hourglass"),
                    Width = _size * 0.56,
                    Height = _size * 0.56,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                glyph.Bind(PathIcon.ForegroundProperty, glyph.GetResourceObservable("Cc.OnAccent"));
                disc.Child = glyph;
                return disc;
            }

            case StepState.Attention:
            case StepState.Problem:
            {
                var glyph = new PathIcon { Data = Glyphs.Get("Warning"), Width = _size, Height = _size };
                glyph.Bind(PathIcon.ForegroundProperty, glyph.GetResourceObservable(state == StepState.Problem ? "Cc.Danger" : "Cc.Gold"));
                return glyph;
            }

            default:
            {
                var ring = new Ellipse { Width = _size - 2, Height = _size - 2, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                ring.Bind(Shape.StrokeProperty, ring.GetResourceObservable("Cc.InkFaint"));
                return ring;
            }
        }
    }
}

/// <summary>Small builders the setup windows share.</summary>
internal static class SetupLook
{
    /// <summary>
    /// The painted tile for a step, from what the step is. Decoration only -
    /// nothing is decided by it - so a collection the catalogue adds later
    /// simply gets the reader's tile until someone gives it its own.
    /// </summary>
    public static (string TileKey, string Glyph) ArtFor(SetupDataSource source)
    {
        var title = source.Title;

        if (title.Contains("Lemma", StringComparison.OrdinalIgnoreCase)) return ("core-vocabulary", "Vocabulary");
        if (title.Contains("Dictionar", StringComparison.OrdinalIgnoreCase)) return ("word-study", "WordStudy");
        if (title.Contains("Map", StringComparison.OrdinalIgnoreCase)) return ("places-map", "PlacesMap");
        if (title.Contains("Archaeology", StringComparison.OrdinalIgnoreCase)) return ("myth-network", "Image");
        if (title.Contains("Manuscript Hands", StringComparison.OrdinalIgnoreCase)) return ("medieval-hands", "MedievalHands");
        if (title.Contains("Nordic", StringComparison.OrdinalIgnoreCase)) return ("nordic", "Nordic");
        if (title.Contains("Citations", StringComparison.OrdinalIgnoreCase)) return ("concordance", "Hash");
        return ("reader", "Work");
    }

    public static TextBlock Text(string text, params string[] classes)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        foreach (var c in classes) block.Classes.Add(c);
        return block;
    }

    /// <summary>A link to a web page, opened in the reader's own browser.</summary>
    public static Button Link(string text, string url, Action<string>? failed = null)
    {
        var link = new Button { Content = text, Classes = { "link" }, HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(link, url);
        link.Click += (_, _) =>
        {
            // A browser that won't open is not a setup failure, and an alert
            // would imply it was. The address is the useful thing to hand over.
            if (!MacShell.Open(url)) failed?.Invoke($"Couldn't open a browser. Go to {url} yourself.");
        };
        return link;
    }

    /// <summary>A big, clear button: the one a step is about, or its partner.</summary>
    public static Button BigButton(string text, bool accent = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 210,
            MinHeight = 36,
            Padding = new Thickness(18, 8),
            FontSize = 13.5,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (accent) button.Classes.Add("accent");
        return button;
    }

    /// <summary>A heading and an optional explanation, as Settings lays a section out.</summary>
    public static StackPanel Section(string title, string? explanation = null)
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 22) };
        stack.Children.Add(new TextBlock { Text = title, Classes = { "h2" } });
        if (explanation != null) stack.Children.Add(Text(explanation, "caption"));
        return stack;
    }

    /// <summary>A read-only log in the monospace face, for a step whose output is a report.</summary>
    public static TextBox LogBox(double height) => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = height,
        FontSize = 11.5,
        FontFamily = (FontFamily)Application.Current!.FindResource("Cc.MonoFont")!
    };

    /// <summary>The state colour for a line of text beside a mark.</summary>
    public static string InkFor(StepState state) => state switch
    {
        StepState.Complete => "Cc.Verdigris",
        StepState.Problem => "Cc.Danger",
        _ => "Cc.InkMuted"
    };
}
