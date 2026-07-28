using System.Xml.Linq;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion;

public record LemmaIngestProgress(string CurrentFile, int FilesProcessed, int TotalFiles, int LemmasLoaded);

/// <summary>
/// Loads form -> headword mappings from a locally-cloned lemmatized corpus
/// (e.g. gcelano/LemmatizedAncientGreekXML, which publishes Morpheus-derived
/// lemma data keyed to the same Perseus canonical-greekLit texts this app
/// already ingests).
///
/// IMPORTANT - read before first run: the parser below is deliberately
/// tolerant rather than precise. These published datasets vary in element
/// naming between repos and between releases, and this was written without
/// being able to inspect the actual files. So instead of assuming one
/// layout, it walks every element and accepts anything that exposes both a
/// form and a lemma under any of several common attribute/child names.
/// If a first run loads zero (or obviously wrong) mappings, the fix is
/// almost certainly to add the real attribute names to FormAttributeNames /
/// LemmaAttributeNames below after eyeballing one file - not to rewrite
/// this wholesale.
/// </summary>
public class LemmaIngestService
{
    private readonly LemmaRepository _lemmaRepo = new();

    // Morpheus-derived data commonly uses single-letter attributes: f = the
    // accented form, b/e = bare/stripped form, l = lemma headword,
    // p = morphological tag. Longer names are included for datasets that
    // spell them out.
    // Element names that hold a single token in the datasets this supports:
    // <t> in the Greek (gcelano) format, <w> in the Latin (lascivaroma) TEI.
    private static readonly string[] TokenElementNames = { "t", "token", "w" };

    // Where the surface form lives when it's a child element rather than the
    // token's own text.
    private static readonly string[] FormElementNames = { "f", "wordform" };

    // Greek format nests the actual headword text in <l1> (PerseusUnderPhilologic)
    // and/or <l2> (Morpheus) elements, inside an <l> wrapper whose @i is only a
    // database ID. A token can carry several of these - that's genuine lemma
    // ambiguity, so each becomes its own mapping.
    private static readonly string[] LemmaElementNames = { "l1", "l2" };

    private static readonly string[] FormAttributeNames = { "f", "form", "word", "token", "b", "e" };
    private static readonly string[] LemmaAttributeNames = { "lemma", "headword", "hw" };
    private static readonly string[] PosAttributeNames = { "p", "pos", "postag", "tag", "msd" };

    /// <summary>
    /// Strips this corpus's own annotation markers off a token. The Latin
    /// dataset wraps split enclitics in braces ({breuibusque}) and appends
    /// '?' to forms its tagger couldn't confidently disambiguate - neither
    /// belongs in a stored word form.
    /// </summary>
    /// <summary>
    /// Strips the editorial markup that critical editions wrap around
    /// supplied, doubtful, or restored text, so those markers don't end up
    /// baked into a headword. Angle brackets are the convention for text
    /// the editor supplied - the LSJ uses them the same way, in entries
    /// like "a)&lt;m&gt;farme/nh" - and without stripping them a word shows
    /// up in Word Study as a separate, useless headword like "&lt;ἦν&gt;"
    /// sitting alongside the real one.
    ///
    /// Both the ASCII brackets and the Unicode angle-bracket variants are
    /// covered, since which pair a given edition uses isn't consistent.
    /// </summary>
    private static string CleanToken(string token)
    {
        return token.Trim()
            .Trim('{', '}', '?', '[', ']', '<', '>', '\u27e8', '\u27e9', '\u2329', '\u232a', '\u3008', '\u3009')
            .Trim();
    }

    public async Task IngestAsync(
        string repoPath,
        string language,
        IProgress<LemmaIngestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Folder not found: {repoPath}");

        var files = Directory.GetFiles(repoPath, "*.xml", SearchOption.AllDirectories);
        if (files.Length == 0)
            throw new InvalidOperationException($"No .xml files found anywhere under {repoPath}.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var batch = new List<Lemma>();
        var totalLoaded = 0;

        for (var i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[i];

            progress?.Report(new LemmaIngestProgress(Path.GetFileName(file), i, files.Length, totalLoaded));

            try
            {
                foreach (var lemma in ExtractFromFile(file, language))
                {
                    // Dedupe on the triple - the same form/lemma pair recurs
                    // constantly across a corpus and there's no value in
                    // storing it thousands of times.
                    var key = $"{lemma.NormalizedForm}\u0001{lemma.Headword}\u0001{lemma.PartOfSpeech}";
                    if (!seen.Add(key)) continue;

                    batch.Add(lemma);

                    if (batch.Count >= 20000)
                    {
                        await _lemmaRepo.BulkInsertAsync(batch, cancellationToken);
                        totalLoaded += batch.Count;
                        batch.Clear();
                    }
                }
            }
            catch (Exception) when (true)
            {
                // Same policy as the text ingest: one unparseable file
                // shouldn't kill a long run.
            }
        }

        if (batch.Count > 0)
        {
            await _lemmaRepo.BulkInsertAsync(batch, cancellationToken);
            totalLoaded += batch.Count;
        }

        progress?.Report(new LemmaIngestProgress("Done", files.Length, files.Length, totalLoaded));
    }

    /// <summary>
    /// Walks only recognized token elements and pulls the form and headword
    /// from the position each format actually uses, rather than guessing by
    /// attribute name across every element in the document.
    ///
    /// Greek (gcelano):
    ///   &lt;t o="v-sppemn-"&gt;&lt;f&gt;ἀρχόμενος&lt;/f&gt;&lt;l i="1"&gt;&lt;l1 o="..."&gt;ἄρχω&lt;/l1&gt;&lt;/l&gt;&lt;/t&gt;
    ///   The headword is the TEXT of &lt;l1&gt;/&lt;l2&gt;; @i on &lt;l&gt; is only a database
    ///   ID. Tokens whose &lt;l&gt; is empty had no lemma found at all and are
    ///   skipped rather than stored under their ID.
    ///
    /// Latin (lascivaroma):
    ///   &lt;w pos="NOMcom" lemma="mercimonium"&gt;mercimoniis&lt;/w&gt;
    ///   The headword is @lemma and the form is the element's own text.
    /// </summary>
    private static IEnumerable<Lemma> ExtractFromFile(string path, string language)
    {
        var doc = XDocument.Load(path);

        foreach (var token in doc.Descendants()
                     .Where(e => TokenElementNames.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
        {
            var form = ExtractForm(token);
            if (string.IsNullOrWhiteSpace(form)) continue;

            form = CleanToken(form);
            if (form.Length == 0 || form.Length > 100) continue;

            var normalized = WordNormalizer.Normalize(form);
            if (normalized.Length == 0) continue;

            foreach (var (headword, pos) in ExtractHeadwords(token))
            {
                var cleanHeadword = CleanToken(headword);
                if (cleanHeadword.Length == 0 || cleanHeadword.Length > 100) continue;

                // A purely numeric "headword" is a database ID, not a
                // dictionary entry. Storing one produces an entry per token
                // rather than per word, which silently defeats the entire
                // point of lemmatizing since nothing groups into a paradigm.
                if (cleanHeadword.All(char.IsDigit)) continue;

                var cleanPos = pos;
                if (cleanPos != null && cleanPos.Length > 32) cleanPos = null;

                yield return new Lemma
                {
                    Form = form,
                    NormalizedForm = normalized,
                    Headword = cleanHeadword,
                    Language = language,
                    PartOfSpeech = cleanPos
                };
            }
        }
    }

    private static string? ExtractForm(XElement token)
    {
        // Greek: the form sits in a dedicated child element.
        var formElement = token.Elements()
            .FirstOrDefault(e => FormElementNames.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase));
        if (formElement != null && !string.IsNullOrWhiteSpace(formElement.Value)) return formElement.Value;

        var attrForm = ReadValue(token, FormAttributeNames);
        if (!string.IsNullOrWhiteSpace(attrForm)) return attrForm;

        // Latin: the form is the token element's own text. Guard on
        // HasElements so a container can never dump a whole passage in here.
        return token.HasElements ? null : token.Value;
    }

    private static IEnumerable<(string Headword, string? Pos)> ExtractHeadwords(XElement token)
    {
        // Greek: one or more <l1>/<l2> descendants, each its own candidate
        // lemma. Several means real ambiguity, so all of them are kept.
        var lemmaElements = token.Descendants()
            .Where(e => LemmaElementNames.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (lemmaElements.Count > 0)
        {
            foreach (var lemmaElement in lemmaElements)
            {
                if (string.IsNullOrWhiteSpace(lemmaElement.Value)) continue;

                // @o on the lemma element is its morphological analysis;
                // fall back to @o on the token (the tagger's own POS).
                var pos = lemmaElement.Attribute("o")?.Value
                          ?? token.Attribute("o")?.Value;

                yield return (lemmaElement.Value, pos);
            }
            yield break;
        }

        // Latin: headword in an attribute on the token itself.
        var attrHeadword = ReadValue(token, LemmaAttributeNames, preferNonNumeric: true);
        if (!string.IsNullOrWhiteSpace(attrHeadword))
        {
            yield return (attrHeadword, ReadValue(token, PosAttributeNames));
        }
    }

    /// <summary>
    /// Reads a value that might be an attribute or a child element, under
    /// any of the candidate names, namespace-insensitively.
    ///
    /// preferNonNumeric matters for lemma lookup: these datasets carry
    /// numeric ID fields under short names that can collide with the
    /// candidate list, so a purely numeric hit is treated as a last resort
    /// and any alphabetic candidate wins over it regardless of order.
    /// </summary>
    private static string? ReadValue(XElement element, string[] candidateNames, bool preferNonNumeric = false)
    {
        string? numericFallback = null;

        foreach (var name in candidateNames)
        {
            var attr = element.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            if (attr == null || string.IsNullOrWhiteSpace(attr.Value)) continue;

            if (preferNonNumeric && attr.Value.Trim().All(char.IsDigit))
            {
                numericFallback ??= attr.Value;
                continue;
            }
            return attr.Value;
        }

        foreach (var name in candidateNames)
        {
            var child = element.Elements()
                .FirstOrDefault(c => string.Equals(c.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            if (child == null || string.IsNullOrWhiteSpace(child.Value)) continue;

            if (preferNonNumeric && child.Value.Trim().All(char.IsDigit))
            {
                numericFallback ??= child.Value;
                continue;
            }
            return child.Value;
        }

        return numericFallback;
    }
}
