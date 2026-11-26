namespace ClassicaCodex.Core.Lexicon;

/// <summary>
/// How particular authors are cited, where it decides which of
/// <see cref="PassageIndex"/>'s steps can be trusted. Each was found by
/// checking citations against the passages they opened - whether the words
/// the dictionary quotes are there - across the whole library.
///
/// <b>Cicero's sections run straight through.</b> Outside the letters, his
/// works are cited by chapter and section, and the sections are numbered
/// continuously, so editions number by the section alone: "Brut. 24, 93" is
/// section 93. A reference that stops at the chapter - "Clu. 41", or
/// "de Or. 3, 45" for book 3, chapter 45 - names a chapter the library's
/// editions do not number, and read as a section it opens the wrong passage
/// four times in five. Those are opened only by the continuous-section step,
/// which needs the section; the rest stay plain text. Nobody else's sections
/// run through: tried on Sallust, the same step opened the wrong chapter.
/// </summary>
public static class CitationConventions
{
    private const string Cicero = "phi0474.";

    /// <summary>His four collections of letters, cited by book, letter and section like anything else.</summary>
    private static readonly string[] CiceroLetters = { "phi0474.phi056", "phi0474.phi057", "phi0474.phi058", "phi0474.phi059" };

    /// <summary>Whether a work's sections are numbered straight through, so a chapter and section can be read as the section alone.</summary>
    public static bool SectionsRunThrough(string workUrn) =>
        workUrn.StartsWith(Cicero, StringComparison.Ordinal) && !CiceroLetters.Contains(workUrn, StringComparer.Ordinal);

    /// <summary>
    /// Whether a reference into this work may stop at a chapter the library
    /// does not number, so that only a chapter-and-section reading of it can
    /// be trusted - see the remarks.
    /// </summary>
    public static bool MayStopAtChapter(string workUrn, IReadOnlyList<string> passage) =>
        passage.Count <= 2 && SectionsRunThrough(workUrn);
}
