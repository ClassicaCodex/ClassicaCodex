using System.Text.Json;

namespace ClassicaCodex.Ingestion.ReM;

/// <summary>
/// One annotated token: the two spellings it appears under, the headword it
/// belongs to, and what grammar the corpus records for it.
/// </summary>
public sealed record ReMAnnotation(
    string DiplomaticForm,
    string NormalisedForm,
    string Headword,
    string? Tag,
    string? DictionaryId);

/// <summary>
/// Reads ReM's Tabular JSON, which is where the corpus keeps the annotation it
/// is famous for.
///
/// <b>Why a second format at all.</b> The TEI export the texts come from has no
/// lemma, no part of speech and no morphology - and its @lemma attribute is the
/// NORMALISED FORM, which is a trap rather than an absence. Taking it for a
/// headword would fill the Lemmas table with inflected words, and Word Study
/// would group forms under normalised spellings and look entirely convincing
/// doing it. The real annotation only exists here.
///
/// The difference, on one token:
///
/// <code>
/// TEI    &lt;w norm="stet" lemma="stêt"&gt;ſtet&lt;/w&gt;        lemma="stêt" is the FORM
/// JSON   "form":"ſtet" "norm":"stêt" "lemma":"stân"      lemma is the HEADWORD
///        "pos_hits":"VVFIN" "infl":"Ind.Pres.Sg.3" "lemma_idmwb":"157380000"
/// </code>
///
/// <b>Both spellings are recorded.</b> The library holds each ReM text twice,
/// as the scribe spelled it and normalised, so a lemma known only under one of
/// them would work in one pane and not the other. Where the two normalise to
/// the same string - which they do for 54.8% of tokens, most of the rest being
/// real differences like "div" against "diu" - only one mapping is kept.
/// </summary>
public static class ReMLemmaLoader
{
    /// <summary>
    /// What ReM writes where a value is absent. It is a value, not a gap, and
    /// punctuation carries it in the lemma field.
    /// </summary>
    private const string Placeholder = "--";

    private static bool IsMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == Placeholder;

    /// <summary>
    /// Every annotated token of one ReM text.
    ///
    /// Streamed from the JsonDocument rather than deserialised into objects:
    /// the corpus is 1.35 GB across 406 files and only five of each token's
    /// eighteen fields are wanted, so materialising the rest is work that would
    /// be thrown away 2.27 million times.
    /// </summary>
    public static IEnumerable<ReMAnnotation> Read(Stream json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("token", out var tokens)
            || tokens.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var token in tokens.EnumerateArray())
        {
            var headword = Text(token, "lemma");

            // No headword means punctuation, a gap, or a stretch that was
            // tokenised but never annotated - 19 of the 406 texts contain one.
            // The placeholder makes those detectable rather than invisible,
            // which is the only reason a vocabulary profile over this corpus
            // can be honest about its coverage.
            if (IsMissing(headword)) continue;

            // "--" is not the only way ReM says it could not give a lemma. It
            // also writes [!!] and [!] - 110,616 and 20,975 tokens, 5.7% of the
            // corpus between them, which would have made them far and away the
            // two commonest "headwords" in Middle High German. A reader
            // clicking a word would have been shown [!!] as its dictionary
            // form, and the Word Study forms pane would have offered a hundred
            // thousand spellings of it.
            //
            // Tested for by asking whether the headword contains a letter at
            // all, rather than by listing the markers: it catches [?] and any
            // future sibling, and the only real headwords it also drops are the
            // forty-odd bare numerals, which are not dictionary words either.
            if (!headword!.Any(char.IsLetter)) continue;

            var diplomatic = Text(token, "form");
            var normalised = Text(token, "norm");

            if (IsMissing(diplomatic) && IsMissing(normalised)) continue;

            yield return new ReMAnnotation(
                IsMissing(diplomatic) ? string.Empty : diplomatic!.Trim(),
                IsMissing(normalised) ? string.Empty : normalised!.Trim(),
                headword!.Trim(),
                Tag(token),
                IsMissing(Text(token, "lemma_idmwb")) ? null : Text(token, "lemma_idmwb")!.Trim());
        }
    }

    /// <summary>
    /// The grammar, as one string, because that is the column there is.
    ///
    /// ReM tags part of speech in HiTS (VVFIN, NA, DDART) and inflection
    /// separately (Ind.Pres.Sg.3, Fem.Nom.Sg). Joined with a space so the
    /// result reads as a parse rather than as two codes, and left as ReM wrote
    /// it: MorphologyDecoder has no HiTS branch and returns an undecoded tag
    /// with its raw text preserved, which its own comment says is the right
    /// failure - a wrong parse being worse than no parse.
    /// </summary>
    private static string? Tag(JsonElement token)
    {
        var pos = Text(token, "pos_hits");
        var infl = Text(token, "infl");

        var hasPos = !IsMissing(pos);
        var hasInfl = !IsMissing(infl);

        return (hasPos, hasInfl) switch
        {
            (true, true) => $"{pos!.Trim()} {infl!.Trim()}",
            (true, false) => pos!.Trim(),
            (false, true) => infl!.Trim(),
            _ => null
        };
    }

    private static string? Text(JsonElement token, string property) =>
        token.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
