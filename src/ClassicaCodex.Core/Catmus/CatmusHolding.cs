namespace ClassicaCodex.Core.Catmus;

/// <summary>
/// A CATMuS manuscript this library actually holds, as opposed to one the
/// catalogue merely lists - see <see cref="CatmusCatalogue"/> for that
/// distinction, which the Palaeography window turns into "downloaded" and
/// "available".
/// </summary>
public sealed class CatmusHolding
{
    public long ManuscriptId { get; init; }
    public string Shelfmark { get; init; } = string.Empty;
    public string? Language { get; init; }
    public int? Century { get; init; }
    public string? ScriptType { get; init; }
    public string? Genre { get; init; }
    public string? Verse { get; init; }
    public string? Project { get; init; }
    public int LineCount { get; init; }

    /// <summary>
    /// File name of this manuscript's image pack inside the data folder, or
    /// null while only its transcriptions have been downloaded.
    ///
    /// A file name rather than a path, so a library that is moved to another
    /// folder - or another machine - still finds its pictures.
    /// </summary>
    public string? ImagePack { get; init; }

    public bool HasImages => !string.IsNullOrEmpty(ImagePack);
}

/// <summary>
/// One line of a manuscript: the transcription, and where its photograph
/// sits in the image pack.
///
/// <b>There is no line number here and that is not an omission.</b> CATMuS
/// shuffles its rows and records neither page nor position, so the only
/// honest address for a line is where it sits in the file it came from.
/// Anything presented as "line 14" would be line 14 of the shuffle.
/// </summary>
public sealed class CatmusLine
{
    public long LineId { get; init; }
    public long ManuscriptId { get; init; }

    /// <summary>The shard this line came from, and its position in it - together, its identity in the dataset.</summary>
    public string ShardFile { get; init; } = string.Empty;

    public int RowIndex { get; init; }

    /// <summary>
    /// The diplomatic transcription. Abbreviations are left as the scribe
    /// wrote them - "ꝯcessisse", not "concessisse" - which is the whole
    /// reason this collection is worth having.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>SegmOnto zone: MainZone, MarginTextZone, NumberingZone.</summary>
    public string? Region { get; init; }

    /// <summary>SegmOnto line type: DefaultLine, HeadingLine, InterlinearLine.</summary>
    public string? LineType { get; init; }

    public long? ImageOffset { get; init; }
    public int? ImageLength { get; init; }

    public bool HasImage => ImageOffset.HasValue && ImageLength is > 0;

    /// <summary>Carried alongside the line so a search result can say which manuscript it is from.</summary>
    public string Shelfmark { get; init; } = string.Empty;

    public string? ScriptType { get; init; }
    public int? Century { get; init; }
    public string? ImagePack { get; init; }
}
