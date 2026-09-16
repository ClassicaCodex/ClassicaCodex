namespace ClassicaCodex.Core.Reactions;

/// <summary>
/// Whether a critic is someone who existed.
///
/// The distinction is the whole ethical basis of this feature. A composite is
/// an invented person, and inventing one is fine: no real person is being put
/// words in the mouth of. A historical critic is Plato, or Quintilian, or
/// Dionysius of Halicarnassus - and putting a made-up opinion in Plato's mouth
/// would be the one genuinely dishonest thing this feature could do, because a
/// reader has no way to tell a plausible invention from something Plato wrote.
///
/// So the rule that follows from it, enforced by the validator rather than by
/// anyone remembering: a Historical critic may only be given a view that is
/// attested, and every turn they speak carries the ancient reference for
/// where that view is found. The words are still paraphrase and still
/// fictional - it is the STANCE that has to be real, and the citation is what
/// lets a reader go and check.
/// </summary>
public enum CriticKind
{
    /// <summary>An invented person of a real time and place. Most speakers.</summary>
    Composite,

    /// <summary>
    /// Someone who existed and whose opinion on this work survives. Every
    /// turn needs <see cref="DebateTurn.SourceCitation"/>.
    /// </summary>
    Historical
}

/// <summary>
/// Whether a debate is a scene or a conversation that could never have
/// happened.
/// </summary>
public enum DebateKind
{
    /// <summary>
    /// One room, one evening. Every speaker was alive and everything said was
    /// knowable then - the validator checks both against the year.
    /// </summary>
    Scene,

    /// <summary>
    /// Voices from centuries apart, set beside each other in the order they
    /// spoke.
    ///
    /// Here because the kickoff spec asked for an Iliad debate with "critics
    /// spanning the Classical period to Late Antiquity" and, one rule above,
    /// for the setting year to fall inside every speaker's floruit. Those two
    /// cannot both hold: nobody was alive for both Herodotus and Augustine.
    ///
    /// Rather than quietly relax the anachronism rule - which is the rule
    /// most worth keeping - the impossibility is made explicit. Each turn
    /// carries its own year, the years only ever move forward, and the window
    /// labels it as centuries of reaction rather than an evening's
    /// conversation. Nobody is made to have heard anybody.
    /// </summary>
    AcrossTheCenturies
}

/// <summary>
/// One critic. Invented or real; see <see cref="CriticKind"/>.
///
/// Critics are shared between debates by id - the same retired chorus-trainer
/// can turn up on the Clouds and on the Frogs - so they are upserted rather
/// than owned by the pack that happens to declare them first.
/// </summary>
/// <param name="Id">Stable slug, e.g. "demeas-acharnae". What turns refer to.</param>
/// <param name="FloruitStart">Negative is BCE. A floruit, not a lifespan: the years this person was in a position to have opinions in public.</param>
/// <param name="Sketch">Two or three sentences. Shown on the persona card.</param>
/// <param name="Tastes">What they value and what they cannot stand.</param>
/// <param name="Avatar">
/// The portrait recipe - see AncientAvatars in the UI project. A short
/// semicolon-separated trait list ("skin:3;hair:grey;beard:full;hat:laurel")
/// rather than an image file, so the pictures cost nothing to ship, redraw
/// crisply at any display scaling, and a new critic needs no artwork.
/// </param>
public sealed record AncientCritic(
    string Id,
    string Name,
    CriticKind Kind,
    string Era,
    int FloruitStart,
    int FloruitEnd,
    string? Place,
    string? Role,
    string? Sketch,
    string? Tastes,
    string? Avatar)
{
    /// <summary>Whether this critic was alive and active in a given year.</summary>
    public bool WasActiveIn(int year) => year >= FloruitStart && year <= FloruitEnd;

    /// <summary>"c. 450-400 BCE", "AD 35-100" - as a card would print it.</summary>
    public string FloruitLabel => $"{Year(FloruitStart)}–{Year(FloruitEnd)}";

    /// <summary>
    /// A year as a reader writes it. Negative years are BCE, and there is no
    /// year zero, so -450 is 450 BCE and the arithmetic elsewhere in this file
    /// stays in astronomical years where subtraction works.
    /// </summary>
    public static string Year(int year) =>
        year < 0 ? $"{-year} BCE" : $"AD {year}";
}

/// <summary>
/// One thing one critic says.
/// </summary>
/// <param name="Seq">1-based, contiguous. The order of the conversation.</param>
/// <param name="Year">
/// When this was said, for <see cref="DebateKind.AcrossTheCenturies"/>, where
/// there is no shared setting year to fall back on. Null in a Scene.
/// </param>
/// <param name="PassageRef">
/// A citation in the work under discussion, written the way a classicist
/// writes it - "1.1", "225", "10.607". Resolved against whatever edition the
/// reader actually has at display time, and simply not shown as a link when
/// it does not resolve, because which corpora are installed varies.
/// </param>
/// <param name="SourceCitation">
/// Where the ancient world records this view. Required on every turn of a
/// <see cref="CriticKind.Historical"/> critic and forbidden nowhere - a
/// composite may cite too, and several do, because the stance they are voicing
/// is documented even though the person is not.
/// </param>
/// <param name="SourceWork">
/// The same reference as something this application can open, where it can:
/// an author key, title keys and a citation, matched against the library the
/// way StartingPoints matches its recommendations. Optional, and absent
/// wherever the source is not in any corpus this application ingests -
/// Dionysius of Halicarnassus and Servius are both in that position.
/// </param>
public sealed record DebateTurn(
    int Seq,
    string CriticId,
    string Text,
    int? Year = null,
    string? PassageRef = null,
    string? SourceCitation = null,
    WorkReference? SourceWork = null);

/// <summary>
/// How a pack names a work, and why it is not a CTS URN.
///
/// The same work carries different URNs across Perseus, Open Greek and Latin
/// and First1KGreek, and which of those a reader installed varies - the exact
/// reason StartingPoints matches on author and title text instead. A pack
/// keyed on one corpus's URN would silently have no debates for a reader who
/// installed another.
/// </summary>
/// <param name="AuthorKey">Matched case-insensitively inside the author's name. "vergil" finds "P. Vergilius Maro (Virgil)".</param>
/// <param name="TitleKeys">Any one of them matching the title is enough. Several because the same work arrives as "Metamorphoses" and "Metamorphoseon".</param>
/// <param name="CitationRef">Only on a source reference: where in that work.</param>
public sealed record WorkReference(
    string AuthorKey,
    IReadOnlyList<string> TitleKeys,
    string? CitationRef = null)
{
    /// <summary>
    /// Whether a library work is the one this reference names.
    ///
    /// The author test is deliberately the strict half. "caesar" sits inside
    /// "Caesarius Arelatensis Episcopus" and "pseudo-Lucian" contains
    /// "Lucian", and StartingPoints has the scars to prove what happens when a
    /// substring match is trusted: it opened a sixth-century bishop of Arles
    /// for a reader who asked for the Gallic War. A pseudonymous author is
    /// never what a pack means - no debate here is about a spurious work - so
    /// those are rejected outright rather than merely ranked lower.
    /// </summary>
    public bool Matches(string authorName, string workTitle)
    {
        if (authorName.Contains("pseudo", StringComparison.OrdinalIgnoreCase)) return false;
        if (!MatchesAuthor(authorName)) return false;

        return TitleKeys.Count == 0
               || TitleKeys.Any(k => k.Length == 0
                                     || workTitle.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesAuthor(string authorName)
    {
        // A whole word first - "homer" is the poet in "Homer" and a syllable
        // in "Homerus Latinus". Falling back to a substring is what "vergil"
        // inside "Vergilius" needs, and it is only reached when no author
        // matches as a word at all.
        foreach (var word in authorName.Split(
                     new[] { ' ', ',', '.', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(word, AuthorKey, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return authorName.Contains(AuthorKey, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// One staged argument about one work.
/// </summary>
/// <param name="CompositionYear">
/// Roughly when the work was written, negative for BCE. Not display material -
/// it exists so the validator can refuse a debate set before the thing being
/// debated existed, which is the anachronism that would be easiest to write by
/// accident and hardest to notice afterwards.
/// </param>
/// <param name="SettingYear">Null for <see cref="DebateKind.AcrossTheCenturies"/>, where each turn carries its own.</param>
public sealed record ReactionDebate(
    string Id,
    WorkReference Work,
    string Title,
    DebateKind Kind,
    int CompositionYear,
    int? SettingYear,
    string? SettingPlace,
    string? SettingNote,
    IReadOnlyList<DebateTurn> Turns)
{
    /// <summary>The critics who speak here, in the order they first speak.</summary>
    public IEnumerable<string> SpeakerIds => Turns.Select(t => t.CriticId).Distinct();

    /// <summary>
    /// The header line - "Athens, 423 BCE" - or, for a conversation that never
    /// happened, what it is instead.
    /// </summary>
    public string SettingLabel
    {
        get
        {
            if (Kind == DebateKind.AcrossTheCenturies)
            {
                var years = Turns.Where(t => t.Year.HasValue).Select(t => t.Year!.Value).ToList();
                return years.Count == 0
                    ? "Across the centuries"
                    : $"{AncientCritic.Year(years.Min())} to {AncientCritic.Year(years.Max())}";
            }

            var when = SettingYear.HasValue ? AncientCritic.Year(SettingYear.Value) : null;
            return string.Join(", ", new[] { SettingPlace, when }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }
}

/// <summary>
/// One authored file: the critics it introduces and the debate it holds.
/// </summary>
public sealed record ReactionPack(
    string Name,
    IReadOnlyList<AncientCritic> Critics,
    ReactionDebate Debate);
