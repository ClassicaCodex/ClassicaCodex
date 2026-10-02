using System.Text;

namespace ClassicaCodex.Ingestion.Egyptian;

/// <summary>One annotated word, as the treebank parses it.</summary>
public sealed record PyramidWord(string Form, string Headword, string? Tag);

/// <summary>
/// One sentence of the Pyramid Texts, in both of the ways it can be written:
/// the signs cut into the wall, and the transliteration an Egyptologist reads
/// them as.
/// </summary>
public sealed record PyramidUtterance(
    string Spell,
    string Section,
    string Citation,
    string Hieroglyphs,
    string Transliteration,
    IReadOnlyList<PyramidWord> Words);

/// <summary>
/// One pyramid, and the spells carved in it. A witness rather than a work:
/// these six monuments share spells and none of them is the complete corpus.
/// </summary>
public sealed record PyramidWitness(
    string King, string Title, int Order, IReadOnlyList<PyramidUtterance> Utterances);

/// <summary>
/// Reads the Pyramid Texts out of the Universal Dependencies Egyptian-PC
/// treebank.
///
/// <b>Two readings, and they are not two spellings.</b> Everywhere else in this
/// library a second reading is a second orthography - what the scribe wrote
/// against what an editor prints. Here it is a second <i>script</i>. The
/// hieroglyphs are the signs on the wall; the transliteration is the
/// Egyptological convention for saying them in Latin letters, with its own
/// brackets for what is restored and its own parentheses for what is
/// grammatically present but unwritten. Neither is a normalisation of the
/// other, and a reader needs both: the signs cannot be searched for and the
/// transliteration cannot be looked at.
///
/// <b>Where the text of a sentence comes from.</b> The transliteration is the
/// treebank's own <c># text</c> line, which is the editors' assembled reading
/// and not something to reconstruct from the token column. The hieroglyphs are
/// assembled from the <c>Hiero=</c> annotation on each token, because there is
/// no whole-sentence equivalent.
///
/// <b>Three things in the token column have to be got right.</b>
///
/// A row is not always a word. Multiword tokens appear as a range row carrying
/// the surface form - 1,224 of them - followed by one row per component. The
/// range row is the text and the components are its analysis, so the text
/// takes the range row and the dictionary takes the components. Both carry
/// their own <c>Hiero=</c>, and the range row's is the group as written.
///
/// A token can have no hieroglyphs at all. <c>Hiero=No</c> marks a word the
/// grammar requires and the wall does not show - a suffix pronoun the
/// Egyptians left off - and it must contribute nothing to the signs while
/// still contributing to the transliteration, where the editors bracket it.
///
/// Layout is not Unicode here. The treebank writes quadrats - the square
/// clusters hieroglyphic is actually set in - with parentheses and colons,
/// <c>(𓇋:𓈖)</c> for one sign above another. Unicode has controls for this and
/// Windows does not apply them: on GDI they are ignored, and on GDI+ they draw
/// as visible placeholder rings. So the signs are kept and everything else is
/// dropped, which is linear hieroglyphic - what most digital Egyptian shows.
/// </summary>
public static class PyramidTextLoader
{
    /// <summary>
    /// The six monuments, oldest first, with the names a reader would look for.
    ///
    /// The treebank gives the owner as a bare first name, which is ambiguous
    /// for two of them: "Pepi" is Pepi I and "Neferkare" is his son Pepi II,
    /// who is Neferkare Pepi. Neith is not a king at all - she is Pepi II's
    /// queen, and the only woman whose pyramid carries these texts.
    /// </summary>
    private static readonly (string King, string Name)[] Witnesses =
    {
        ("Unas", "Unas"),
        ("Teti", "Teti"),
        ("Pepi", "Pepi I"),
        ("Merenre", "Merenre"),
        ("Neferkare", "Pepi II"),
        ("Neith", "Queen Neith")
    };

    /// <summary>
    /// Reads and merges every CoNLL-U file given, returning one entry per
    /// pyramid with its spells in order.
    /// </summary>
    public static List<PyramidWitness> Load(IEnumerable<string> conlluPaths)
    {
        var sentences = new List<Sentence>();

        foreach (var path in conlluPaths)
        {
            ReadFile(path, sentences);
        }

        return Assemble(sentences);
    }

    /// <summary>
    /// Groups the sentences by pyramid and puts each pyramid's into reading
    /// order.
    ///
    /// <b>Ordered by spell and not by section.</b> Sethe's section numbers run
    /// continuously through his edition, so for his 2,817 sentences either key
    /// gives the same answer. The other 272 are numbered by Allen, whose
    /// sections count from 1 again inside each spell - sorting those by section
    /// would scatter spell 502C's four sentences to the very front of the
    /// pyramid, among the opening offering formulae. Spell is the unit both
    /// editions agree on.
    /// </summary>
    private static List<PyramidWitness> Assemble(List<Sentence> sentences)
    {
        var witnesses = new List<PyramidWitness>();

        for (var order = 0; order < Witnesses.Length; order++)
        {
            var (king, name) = Witnesses[order];

            var ordered = sentences
                .Where(s => string.Equals(s.King, king, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s, SentenceOrder.Instance)
                .ToList();

            if (ordered.Count == 0) continue;

            witnesses.Add(new PyramidWitness(
                king,
                $"Pyramid Texts {RomanNumeral(order + 1)}: {name}",
                order,
                Cite(ordered)));
        }

        return witnesses;
    }

    /// <summary>
    /// Gives each sentence its citation.
    ///
    /// A section is usually one sentence and sometimes four, so a bare
    /// "213.135a" would name four different passages in Unas. Where a section
    /// holds more than one, each gets its position appended; where it holds
    /// one, the citation stays as an Egyptologist would write it.
    /// </summary>
    private static List<PyramidUtterance> Cite(List<Sentence> ordered)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sentence in ordered)
        {
            var key = $"{sentence.Spell}.{sentence.Section}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var utterances = new List<PyramidUtterance>(ordered.Count);

        foreach (var sentence in ordered)
        {
            var key = $"{sentence.Spell}.{sentence.Section}";
            var position = seen[key] = seen.GetValueOrDefault(key) + 1;

            utterances.Add(new PyramidUtterance(
                sentence.Spell,
                sentence.Section,
                counts[key] > 1 ? $"{key}.{position}" : key,
                sentence.Hieroglyphs(),
                sentence.Transliteration,
                sentence.Words));
        }

        return utterances;
    }

    private static void ReadFile(string path, List<Sentence> sentences)
    {
        Sentence? current = null;
        var componentsLeft = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0)
            {
                Flush(sentences, ref current);
                componentsLeft = 0;
                continue;
            }

            if (line[0] == '#')
            {
                var (key, value) = Comment(line);
                if (key == null) continue;

                // sent_id opens a sentence. A file that ends without a blank
                // line, or one sentence running into the next, would otherwise
                // lose the last of them.
                if (key == "sent_id") Flush(sentences, ref current);

                current ??= new Sentence();
                switch (key)
                {
                    case "sent_id": current.SentId = value; break;
                    case "spell": current.Spell = value; break;
                    case "section": current.Section = value; break;
                    case "king": current.King = value; break;
                    case "text": current.Transliteration = value; break;
                }

                continue;
            }

            if (current == null) continue;

            var cols = line.Split('\t');
            if (cols.Length < 10) continue;

            var id = cols[0];

            // An empty node is reconstructed ellipsis rather than anything on
            // the wall. This treebank has none today; the guard costs nothing
            // and a later release that adds them would otherwise interpolate
            // words into the text.
            if (id.Contains('.', StringComparison.Ordinal)) continue;

            if (id.Contains('-', StringComparison.Ordinal))
            {
                // The surface form of a multiword token. It is the text; the
                // component rows that follow are its analysis.
                var bounds = id.Split('-');
                if (int.TryParse(bounds[0], out var from) && int.TryParse(bounds[1], out var to))
                {
                    componentsLeft = to - from + 1;
                }

                current.AddSigns(Misc(cols[9], "Hiero"));
                continue;
            }

            if (componentsLeft > 0)
            {
                // Inside a multiword token: the dictionary wants this row, the
                // text already has it from the range row above.
                componentsLeft--;
                current.AddWord(cols);
                continue;
            }

            current.AddSigns(Misc(cols[9], "Hiero"));
            current.AddWord(cols);
        }

        Flush(sentences, ref current);
    }

    private static void Flush(List<Sentence> sentences, ref Sentence? current)
    {
        if (current is { Spell.Length: > 0, Section.Length: > 0, King.Length: > 0 })
        {
            sentences.Add(current);
        }

        current = null;
    }

    /// <summary>Splits "# key = value" into its two halves.</summary>
    private static (string? Key, string Value) Comment(string line)
    {
        var body = line.TrimStart('#').Trim();
        var equals = body.IndexOf('=');
        if (equals < 0) return (null, string.Empty);

        return (body[..equals].Trim(), body[(equals + 1)..].Trim());
    }

    /// <summary>
    /// One MISC field by name.
    ///
    /// A handful of rows have a malformed MISC - a missing separator that
    /// leaves "Hiero(𓏅:𓏏)" with no equals sign, and one that reads
    /// "𓊹𓊹𓊹UC_No". There are three of them in 35,458 rows. They are read as
    /// absent rather than guessed at, which loses three groups of signs and
    /// cannot put the wrong ones anywhere.
    /// </summary>
    private static string? Misc(string misc, string key)
    {
        foreach (var part in misc.Split('|'))
        {
            if (part.Length > key.Length
                && part[key.Length] == '='
                && part.StartsWith(key, StringComparison.Ordinal))
            {
                return part[(key.Length + 1)..];
            }
        }

        return null;
    }

    /// <summary>
    /// The Egyptian hieroglyph blocks: the 1,072 signs of the original block,
    /// and the 4,000 of Extended-A added in Unicode 15.1.
    /// </summary>
    private const int HieroglyphFirst = 0x13000;
    private const int HieroglyphLast = 0x1342F;
    private const int HieroglyphExtendedFirst = 0x13460;
    private const int HieroglyphExtendedLast = 0x143FF;

    public static bool IsHieroglyph(int codepoint) =>
        (codepoint >= HieroglyphFirst && codepoint <= HieroglyphLast)
        || (codepoint >= HieroglyphExtendedFirst && codepoint <= HieroglyphExtendedLast);

    /// <summary>
    /// The signs out of one <c>Hiero=</c> value, with the layout dropped.
    ///
    /// Keeping what is a hieroglyph rather than removing what is not. The
    /// field mixes signs with quadrat parentheses, stacking colons,
    /// restoration brackets, the word "No" and the occasional malformed
    /// entry; a list of characters to strip would have to be complete to be
    /// right, and this cannot pass anything through that is not a sign.
    /// </summary>
    public static string SignsOnly(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var signs = new StringBuilder(value.Length);

        foreach (var rune in value.EnumerateRunes())
        {
            if (IsHieroglyph(rune.Value)) signs.Append(rune.ToString());
        }

        return signs.ToString();
    }

    private static string RomanNumeral(int n) => n switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        _ => n.ToString()
    };

    /// <summary>A sentence as it is being read, before it is cited.</summary>
    private sealed class Sentence
    {
        private readonly StringBuilder _signs = new();

        public string SentId { get; set; } = string.Empty;
        public string Spell { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string King { get; set; } = string.Empty;
        public string Transliteration { get; set; } = string.Empty;
        public List<PyramidWord> Words { get; } = new();

        /// <summary>
        /// One token's signs. A group that comes back empty - an unwritten
        /// word, or a token whose annotation is malformed - adds no separator
        /// either, so the line does not gain a gap where nothing was carved.
        /// </summary>
        public void AddSigns(string? hiero)
        {
            var signs = SignsOnly(hiero);
            if (signs.Length == 0) return;

            if (_signs.Length > 0) _signs.Append(' ');
            _signs.Append(signs);
        }

        public void AddWord(string[] cols)
        {
            var form = Value(cols[1]);
            var headword = Value(cols[2]);
            if (form == null || headword == null) return;

            var upos = Value(cols[3]);
            var feats = Value(cols[5]);

            Words.Add(new PyramidWord(form, headword, (upos, feats) switch
            {
                (not null, not null) => $"{upos} {feats}",
                (not null, null) => upos,
                (null, not null) => feats,
                _ => null
            }));
        }

        public string Hieroglyphs() => _signs.ToString();

        /// <summary>CoNLL-U writes an absent value as a single underscore.</summary>
        private static string? Value(string column) => column is "_" or "" ? null : column;
    }

    /// <summary>
    /// Reading order within one pyramid.
    ///
    /// Spell and section are both numbers with letters attached - spell "502C",
    /// section "1059a-b:5" - so neither sorts as a string ("10" before "2") nor
    /// parses as an integer. The comparison takes the leading number, then the
    /// letters, then whatever is left, and falls back to the sentence id so
    /// that two sentences the treebank numbers identically still come out in
    /// the same order on every run.
    /// </summary>
    private sealed class SentenceOrder : IComparer<Sentence>
    {
        public static readonly SentenceOrder Instance = new();

        public int Compare(Sentence? x, Sentence? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var spell = CompareLabels(x.Spell, y.Spell);
            if (spell != 0) return spell;

            var section = CompareLabels(x.Section, y.Section);
            if (section != 0) return section;

            return string.CompareOrdinal(x.SentId, y.SentId);
        }

        private static int CompareLabels(string a, string b)
        {
            var (numberA, restA) = Split(a);
            var (numberB, restB) = Split(b);

            return numberA != numberB
                ? numberA.CompareTo(numberB)
                : string.CompareOrdinal(restA, restB);
        }

        /// <summary>The leading integer, and everything after it.</summary>
        private static (long Number, string Suffix) Split(string label)
        {
            var i = 0;
            while (i < label.Length && char.IsAsciiDigit(label[i])) i++;

            return i == 0 || !long.TryParse(label[..i], out var number)
                ? (long.MaxValue, label)
                : (number, label[i..]);
        }
    }
}
