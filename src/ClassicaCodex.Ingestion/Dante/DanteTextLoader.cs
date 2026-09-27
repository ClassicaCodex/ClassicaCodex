using System.Text;

namespace ClassicaCodex.Ingestion.Dante;

/// <summary>One annotated word of the poem.</summary>
public sealed record DanteWord(string Form, string Headword, string? Tag);

/// <summary>One verse, cited as Dante is cited: canto and line within a canticle.</summary>
public sealed record DanteVerse(
    int Canto, int Verso, string Text, IReadOnlyList<DanteWord> Words);

/// <summary>One canticle - Inferno, Purgatorio or Paradiso - and its verses in order.</summary>
public sealed record DanteCanticle(string Name, IReadOnlyList<DanteVerse> Verses);

/// <summary>
/// Reads the Commedia out of the Universal Dependencies Old Italian treebank.
///
/// <b>What this is, and what it is not.</b> The text is Petrocchi's 1994 critical
/// edition by way of DanteSearch at Pisa. It is NOT a manuscript transcription:
/// unlike Menota, ReM and Geste, there is no scribe's spelling here and no
/// second reading to switch to. Anything shown to a reader about this collection
/// has to say so, because a library that files a critical edition among
/// manuscript transcriptions is quietly wrong about all of them.
///
/// What it does bring is complete annotation - a real headword, a part of speech
/// and full morphology on every word - and Dante's own citation. The Commedia is
/// cited by canto and line and has been for seven centuries; Inf. 5.142 is a
/// reference a reader can take to any edition ever printed. Nothing else in this
/// library arrives with a citation that stable.
///
/// <b>The file is a treebank, so three things have to be got right.</b>
///
/// The poem is split across train, dev and test for machine-learning purposes,
/// and the split cuts through cantos. All three files have to be read and merged
/// or the text has holes in it - reading only the training file would give a
/// Commedia missing roughly a fifth of its verses, in pieces.
///
/// A CoNLL-U row is not always a word. Multiword tokens appear three times over:
/// a range row carrying the surface form ("1-2 Nel") and one row per component
/// ("1 in", "2 il"). The components are the analysis, not the text. And enhanced
/// dependencies add EMPTY NODES with decimal ids ("2.1 dissi") which are
/// reconstructed ellipsis - words an editor supplies to complete a clause that
/// Dante left elliptical. 306 of them. Treated as text they would interpolate
/// words into the poem that are not in it.
///
/// Spacing is in the annotation, not in the text. Without SpaceAfter=No the
/// result is "selva oscura ," and "voi ch' intrate", with spaces the poem does
/// not have.
///
/// Verified after all three: 14,233 verses over 100 cantos, matching the
/// canonical count of every printed Commedia exactly - Inferno 4,720,
/// Purgatorio 4,755, Paradiso 4,758 - with no token left unplaced.
/// </summary>
public static class DanteTextLoader
{
    /// <summary>
    /// Reads and merges every CoNLL-U file given, returning one entry per
    /// canticle with its verses in order.
    /// </summary>
    public static List<DanteCanticle> Load(IEnumerable<string> conlluPaths)
    {
        // canticle -> canto -> verso, kept sorted so the poem comes out in its
        // own order rather than in the order the treebank's splits happen to
        // present it.
        var poem = new Dictionary<string, SortedDictionary<int, SortedDictionary<int, VerseBuilder>>>(
            StringComparer.Ordinal);

        foreach (var path in conlluPaths)
            ReadFile(path, poem);

        return poem
            .Select(canticle => new DanteCanticle(
                canticle.Key,
                canticle.Value
                    .SelectMany(canto => canto.Value.Select(verso =>
                        new DanteVerse(canto.Key, verso.Key, verso.Value.Text(), verso.Value.Words)))
                    .ToList()))
            .OrderBy(c => CanticleOrder(c.Name))
            .ToList();
    }

    /// <summary>
    /// Inferno, Purgatorio, Paradiso - the order of the poem, not of the
    /// alphabet, which would open the library on Paradiso.
    /// </summary>
    private static int CanticleOrder(string name) => name switch
    {
        "Inferno" => 0,
        "Purgatorio" => 1,
        "Paradiso" => 2,
        _ => 3
    };

    private sealed class VerseBuilder
    {
        private readonly StringBuilder _text = new();
        public List<DanteWord> Words { get; } = new();

        public void Append(string form, bool spaceAfter)
        {
            _text.Append(form);
            if (spaceAfter) _text.Append(' ');
        }

        public string Text() => _text.ToString().Trim();
    }

    private static void ReadFile(
        string path,
        Dictionary<string, SortedDictionary<int, SortedDictionary<int, VerseBuilder>>> poem)
    {
        var canticle = "Commedia";
        var componentsLeft = 0;
        int? lastCanto = null, lastVerso = null;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("# sent_id", StringComparison.Ordinal))
            {
                // OldItalian_Dante_Inferno-1 -> Inferno
                var id = line.Split('=', 2)[1].Trim();
                var parts = id.Split('_');
                if (parts.Length >= 3) canticle = parts[2].Split('-')[0];
                continue;
            }

            if (line.Length == 0 || line[0] == '#') continue;

            var cols = line.Split('\t');
            if (cols.Length < 10) continue;

            var id2 = cols[0];

            // An empty node - reconstructed ellipsis, not a word Dante wrote.
            if (id2.Contains('.', StringComparison.Ordinal)) continue;

            if (id2.Contains('-', StringComparison.Ordinal))
            {
                // The surface form of a multiword token. Its components follow
                // and are the analysis of it, so they are skipped for the text -
                // but they carry the lemmas, so they are not skipped entirely.
                var bounds = id2.Split('-');
                if (int.TryParse(bounds[0], out var from) && int.TryParse(bounds[1], out var to))
                    componentsLeft = to - from + 1;
            }
            else if (componentsLeft > 0)
            {
                componentsLeft--;
                AddWord(poem, canticle, lastCanto, lastVerso, cols);
                continue;
            }

            var misc = cols[9];
            var canto = IntField(misc, "Canto") ?? lastCanto;

            // Punctuation carries no Verso - 18,670 rows - and belongs to the
            // verse it follows. Carrying the last one forward is right for it
            // because a line's punctuation is always at the line's end; the rows
            // that would break that rule are the empty nodes, and those are
            // already gone.
            var verso = IntField(misc, "Verso") ?? lastVerso;

            lastCanto = canto;
            lastVerso = verso;

            if (canto == null || verso == null) continue;

            var builder = Builder(poem, canticle, canto.Value, verso.Value);
            builder.Append(cols[1], !misc.Contains("SpaceAfter=No", StringComparison.Ordinal));

            // A range row's own lemma column is empty; its components carry the
            // annotation and are handled above.
            if (!id2.Contains('-', StringComparison.Ordinal))
                AddWord(poem, canticle, canto, verso, cols);
        }
    }

    private static void AddWord(
        Dictionary<string, SortedDictionary<int, SortedDictionary<int, VerseBuilder>>> poem,
        string canticle, int? canto, int? verso, string[] cols)
    {
        if (canto == null || verso == null) return;

        var headword = Value(cols[2]);
        if (headword == null) return;

        var form = Value(cols[1]);
        if (form == null) return;

        var upos = Value(cols[3]);
        var feats = Value(cols[5]);

        Builder(poem, canticle, canto.Value, verso.Value).Words.Add(new DanteWord(
            form, headword, (upos, feats) switch
            {
                (not null, not null) => $"{upos} {feats}",
                (not null, null) => upos,
                (null, not null) => feats,
                _ => null
            }));
    }

    private static VerseBuilder Builder(
        Dictionary<string, SortedDictionary<int, SortedDictionary<int, VerseBuilder>>> poem,
        string canticle, int canto, int verso)
    {
        if (!poem.TryGetValue(canticle, out var cantos))
            poem[canticle] = cantos = new SortedDictionary<int, SortedDictionary<int, VerseBuilder>>();

        if (!cantos.TryGetValue(canto, out var versi))
            cantos[canto] = versi = new SortedDictionary<int, VerseBuilder>();

        if (!versi.TryGetValue(verso, out var builder))
            versi[verso] = builder = new VerseBuilder();

        return builder;
    }

    /// <summary>CoNLL-U writes an absent value as a single underscore.</summary>
    private static string? Value(string column) =>
        column is "_" or "" ? null : column;

    private static int? IntField(string misc, string key)
    {
        foreach (var part in misc.Split('|'))
        {
            if (!part.StartsWith(key + "=", StringComparison.Ordinal)) continue;
            return int.TryParse(part[(key.Length + 1)..], out var value) ? value : null;
        }

        return null;
    }
}
