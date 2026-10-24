using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Mac.Features.Research.Bench;

/// <summary>
/// A choice in the project list's "Show" menu. Statuses are a property of a
/// project, but which of them the list shows is a property of the view, so the
/// two are kept apart: this type names the view's choices, and never writes
/// anything.
/// </summary>
internal sealed class StatusFilter
{
    private readonly Func<ResearchProjectStatus, bool> _admits;

    private StatusFilter(string label, Func<ResearchProjectStatus, bool> admits)
    {
        Label = label;
        _admits = admits;
    }

    public string Label { get; }

    public bool Admits(ResearchProjectStatus status) => _admits(status);

    public override string ToString() => Label;

    /// <summary>Everything still in play - the default, and what the Bench used to show.</summary>
    public static readonly StatusFilter Current = new("Current", s => s != ResearchProjectStatus.Archived);

    public static readonly StatusFilter Everything = new("All", _ => true);

    private static StatusFilter Only(string label, ResearchProjectStatus only) => new(label, s => s == only);

    public static readonly StatusFilter[] Choices =
    [
        Current,
        Only("Active", ResearchProjectStatus.Active),
        Only("On hold", ResearchProjectStatus.OnHold),
        Only("Concluded", ResearchProjectStatus.Concluded),
        Only("Archived", ResearchProjectStatus.Archived),
        Everything
    ];

    /// <summary>Lowercase, for reading inside a sentence.</summary>
    public static string Describe(ResearchProjectStatus status) => status switch
    {
        ResearchProjectStatus.OnHold => "on hold",
        ResearchProjectStatus.Concluded => "concluded",
        ResearchProjectStatus.Archived => "archived",
        _ => "active"
    };

    /// <summary>Capitalised, for a menu item.</summary>
    public static string Title(ResearchProjectStatus status)
    {
        var described = Describe(status);
        return char.ToUpperInvariant(described[0]) + described[1..];
    }
}
