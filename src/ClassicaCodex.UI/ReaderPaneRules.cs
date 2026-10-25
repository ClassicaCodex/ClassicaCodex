using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// The reader's rules about what goes in each pane and what a pane's menu
/// offers, kept apart from MainForm so that the Mac reader compiles the same
/// ones rather than a copy: which edition opens opposite which, which tools
/// a text can answer, and which stored source paths are web pages. Plain
/// logic, no Windows types.
/// </summary>
internal static class ReaderPaneRules
{
    /// <summary>
    /// Which editions belong in which pane's dropdown.
    ///
    /// Everything that is not the original goes in the second pane's
    /// dropdown, including an edition whose Kind ingest could not settle.
    ///
    /// Asking for Translation exactly meant an Unknown edition appeared in
    /// neither dropdown and so could not be opened at all, while its text
    /// sat in the database being returned by searches - a result you could
    /// read in the results list and not in the reader. Whatever we failed
    /// to classify, it is still a text of this work, and the second pane is
    /// where a text that is not the original belongs.
    ///
    /// AND WHEN THERE IS NOTHING ELSE, THE OTHER ORIGINALS GO THERE. A work
    /// can have two originals and no translation, and then the second pane
    /// sat empty while both texts queued up in the first dropdown - the one
    /// arrangement where a reader cannot do the thing two panes exist for.
    ///
    /// Cuneiform is what surfaced it. Oracc gives every text twice, once in
    /// the signs and once in the transliteration Assyriology reads it by,
    /// and both are the original: a transliteration renders the script, it
    /// does not translate the language. Filing one as a Translation to make
    /// the panes fill would be the app claiming an English text it does not
    /// have.
    ///
    /// So this is the same principle as the paragraph above it, from the
    /// other side: the second pane holds the text you want BESIDE this one.
    /// A work carrying two recensions of one Greek text gains the same way.
    ///
    /// Pure and static so it can be tested without a form, a database or a
    /// message loop.
    /// </summary>
    internal static (List<Edition> Left, List<Edition> Right) SplitEditionsBetweenPanes(
        IReadOnlyList<Edition> editions)
    {
        var originals = editions.Where(ed => ed.Kind == EditionKind.Original).ToList();
        var others = editions.Where(ed => ed.Kind != EditionKind.Original).ToList();

        if (others.Count == 0 && originals.Count > 1)
        {
            // The first stays on the left, which for an Oracc work is the
            // transliteration - the side a reader reads - leaving the signs
            // to the right. That falls out of ingest order rather than being
            // asserted here, and either way round is legible.
            return (originals.Take(1).ToList(), originals.Skip(1).ToList());
        }

        return (originals, others);
    }

    /// <summary>
    /// A stored source path read as a web address, or nothing.
    ///
    /// Most editions record the file they were ingested from. A few record a
    /// web address instead, because that is where the text actually lives and
    /// a local path stops being true the moment the library moves - Oracc's
    /// editions carry the text's own page on oracc.org. Where that is so,
    /// the reader can be offered it.
    ///
    /// That matters most for cuneiform, where the published English
    /// translation exists on Oracc's site and is not in the open data, so
    /// this link is the only route to it that copies nothing.
    ///
    /// ONLY http AND https. SourcePath is ingested data, and for every other
    /// collection it is a path on disk. Handing an arbitrary stored string to
    /// the shell would turn a source field into a way to launch things, so
    /// anything that is not a web address is simply not a link.
    ///
    /// Pure and static because this is the security-relevant half and it
    /// should be pinned by test rather than reasoned about: it decides
    /// whether a string out of the database is handed to the shell.
    /// </summary>
    internal static Uri? WebPageFromSourcePath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        return Uri.TryCreate(sourcePath, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }
}

/// <summary>
/// What the text in one pane can actually support, so the right-click
/// menu can offer the tools that will do something and leave out the ones
/// that will open an empty window.
///
/// This is the rule BuildKindMenu already states - the menu describes the
/// document in front of the reader rather than everything the parser can
/// produce - applied to the tools rather than to the node kinds.
///
/// DELIBERATELY ABOUT DATA, NOT ABOUT LANGUAGE OR COLLECTION. Cuneiform
/// is what made this necessary: an Oracc edition has no lexicon and no
/// apparatus, so Word Study and Editor's Notes open on nothing. But
/// "hide these for Akkadian" would be the wrong rule twice over - it
/// would stay wrong for a Greek edition that happens to carry no
/// apparatus, and it would still be hiding Word Study on the day somebody
/// imports Oracc's own Akkadian glossaries. A gate on the data switches
/// itself back on when the data arrives; a gate on the collection has to
/// be remembered and edited.
/// </summary>
internal readonly record struct ReaderPaneCapabilities(bool HasLexicon, bool HasApparatus)
{
    /// <summary>An empty pane offers nothing, rather than everything.</summary>
    internal static ReaderPaneCapabilities Empty => new(false, false);

    /// <summary>
    /// Decided from counts so the rule is testable without a library.
    ///
    /// A null or blank language means no lexicon even where lemma rows
    /// exist, because a lookup has to know which language it is looking
    /// a word up in - WordStudyForm takes the language, and an English
    /// translation and a Latin original are both written in the Latin
    /// alphabet, so the word itself cannot say.
    /// </summary>
    internal static ReaderPaneCapabilities For(string? language, int lemmaCount, int apparatusCount) =>
        new(HasLexicon: !string.IsNullOrWhiteSpace(language) && lemmaCount > 0,
            HasApparatus: apparatusCount > 0);
}
