namespace ClassicaCodex.Core.Quotations;

/// <summary>
/// A passage by its durable address: the edition's CTS URN and the citation
/// reference, both exactly as the library stores them. Node ids are handed
/// out at ingest and renumber when a corpus is rebuilt, so a link that named
/// them would point at the wrong lines after the next re-ingest; tags,
/// bookmarks and inquiries key on the same pair for the same reason.
/// </summary>
public readonly record struct PassageKey(string EditionUrn, string CitationRef);

/// <summary>Which machine search found a link.</summary>
public enum QuotationFinder
{
    /// <summary>
    /// Testimonia: an author named beside a citing word ("ut ait Vergilius",
    /// ὡς Πλάτων φησίν), and the named author's passage that shares at least
    /// two four-word runs with the words around the name.
    /// </summary>
    Testimonia,

    /// <summary>
    /// Afterlives: Latin quotations of the classics found by shared rare
    /// words and by runs of consecutive words, whether or not the source is
    /// named. Only the rows it rated high are shipped.
    /// </summary>
    Afterlives
}

/// <summary>
/// One machine-found quotation: a passage in a later text whose words match a
/// passage in an earlier one. Never an established fact - the reader is shown
/// which search found it, how strongly, and how often that search was right
/// when a sample was read.
///
/// Each side carries a fingerprint of the text it was found in, because the
/// character positions are only good for that text. A library ingested by a
/// different release can print the same passage slightly differently, and
/// positions laid over a changed text would underline the wrong words; where
/// the fingerprint does not match, the words are found again or not shown.
/// </summary>
/// <param name="Quoting">The passage that quotes.</param>
/// <param name="QuotingFingerprint">See <see cref="TextFingerprint"/>; the quoting passage's text when the link was found.</param>
/// <param name="QuotingStart">Where the quoted words start in the quoting passage, in UTF-16 code units.</param>
/// <param name="QuotingEnd">Where they end, exclusive.</param>
/// <param name="Source">The passage quoted.</param>
/// <param name="SourceFingerprint">The quoted passage's text when the link was found.</param>
/// <param name="SourceStart">Where the matched words start in the quoted passage.</param>
/// <param name="SourceEnd">Where they end, exclusive.</param>
/// <param name="Finder">Which search found it.</param>
/// <param name="Kind">
/// Testimonia: "text", or "fragment" when the match is in a modern fragment
/// collection. Afterlives: "main", "short" or "both" - which of its two paths
/// found the quotation.
/// </param>
/// <param name="Score">
/// Testimonia: how many four-word runs the two passages share. Afterlives: its
/// matcher's score. The two scales have nothing in common.
/// </param>
/// <param name="Flag">
/// Afterlives only: "cento" for a poem built out of Virgil's lines, which is a
/// genre rather than citation, or "early-modern" for early-modern Latin that
/// Migne prints under an ancient name. Empty otherwise.
/// </param>
public sealed record QuotationLink(
    PassageKey Quoting, string QuotingFingerprint, int QuotingStart, int QuotingEnd,
    PassageKey Source, string SourceFingerprint, int SourceStart, int SourceEnd,
    QuotationFinder Finder, string Kind, double Score, string Flag);
