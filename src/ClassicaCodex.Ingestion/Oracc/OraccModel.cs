namespace ClassicaCodex.Ingestion.Oracc;

/// <summary>
/// What one Oracc project download actually contains, in the shape the
/// importer wants it rather than the shape the files are in.
///
/// Oracc publishes per-project ZIPs of JSON at
/// https://oracc.museum.upenn.edu/json/&lt;project&gt;.zip, where a subproject
/// path becomes a dash: rinap/rinap1 is rinap-rinap1.zip. Inside, the tree is
/// &lt;project&gt;/&lt;subproject&gt;/ holding metadata.json, catalogue.json,
/// corpus.json, a corpusjson/ folder with one file per text, glossaries
/// (gloss-akk.json, gloss-qpn.json) and search indexes (index-*.json).
/// </summary>
public sealed class OraccProject
{
    /// <summary>"rinap/rinap1" - the project path as Oracc writes it.</summary>
    public string ProjectPath { get; init; } = string.Empty;

    /// <summary>"Tiglath-pileser III and Shalmaneser V".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>"RINAP 1".</summary>
    public string Abbreviation { get; init; } = string.Empty;

    /// <summary>
    /// The licence sentence, verbatim from the data.
    ///
    /// Read rather than hardcoded on purpose. Oracc's default is CC BY-SA 3.0
    /// but individual projects differ - RINAP 1 ships as CC0 - and both of
    /// those carry obligations we can only discharge if we know which one we
    /// took. A per-project licence typed into our own catalogue would be a
    /// copy that can go stale against the data it describes.
    /// </summary>
    public string License { get; init; } = string.Empty;

    public string LicenseUrl { get; init; } = string.Empty;

    /// <summary>Where the data says it came from, for the About page.</summary>
    public string Source { get; init; } = string.Empty;

    public List<OraccText> Texts { get; init; } = new();

    /// <summary>
    /// Texts the download names but does not contain.
    ///
    /// Real projects ship empty files: 3 of rinap/rinap1's 96 corpusjson
    /// files are zero bytes, while saao/saa01's 265 are all present. Reading
    /// one throws, so an import that does not expect them dies partway
    /// through a download the reader cannot repair.
    ///
    /// They are collected rather than ignored. A count of what did not come
    /// through belongs in front of whoever ran the import - silently
    /// importing 93 of 96 texts and reporting success is the failure this
    /// codebase keeps meeting.
    /// </summary>
    public List<OraccSkippedText> Skipped { get; init; } = new();
}

/// <summary>A text named in the download that could not be read, and why.</summary>
public sealed record OraccSkippedText(string TextId, string Reason);

/// <summary>
/// One text - an Oracc "textid" such as Q003414, which is the unit a
/// catalogue entry describes and a corpusjson file holds.
/// </summary>
public sealed class OraccText
{
    /// <summary>"Q003414". Q numbers are composite texts, P numbers physical objects.</summary>
    public string TextId { get; init; } = string.Empty;

    /// <summary>"Tiglath-pileser III 01", from the catalogue's designation.</summary>
    public string Designation { get; init; } = string.Empty;

    /// <summary>
    /// The king whose inscription this is - "Tiglath-pileser III".
    ///
    /// Used as the author, which is a convention rather than a claim: a royal
    /// inscription is composed by scribes and speaks in the king's voice, and
    /// Assyriology cites it under his name. Where the catalogue gives no
    /// ruler, the importer falls back to the project rather than inventing
    /// one.
    /// </summary>
    public string? Ruler { get; init; }

    /// <summary>"Royal Inscription".</summary>
    public string? Genre { get; init; }

    /// <summary>"Neo-Assyrian".</summary>
    public string? Period { get; init; }

    /// <summary>Where it was dug up - "Nimrud (Kalhu)".</summary>
    public string? Provenience { get; init; }

    /// <summary>"Akkadian", as the catalogue names it.</summary>
    public string? LanguageName { get; init; }

    /// <summary>
    /// The catalogue's credits line, which names the editors and states this
    /// text's own licence.
    ///
    /// Kept verbatim and per text, because it does not always agree with the
    /// project. rinap/rinap1's metadata.json says the data is CC0 while every
    /// text's credits say "The annotated edition is released under the
    /// Creative Commons Attribution Share-Alike license 3.0". Those are
    /// different obligations. Flattening them to whichever we read first
    /// would be choosing a licence on the reader's behalf, so both are
    /// carried and the stricter one governs.
    /// </summary>
    public string? Credits { get; init; }

    /// <summary>
    /// The languages the catalogue says a translation exists in - "en".
    ///
    /// It says "en" for RINAP and the download still contains no English.
    /// The translation is on Oracc's website; this field records that one
    /// exists, not that we have it.
    /// </summary>
    public string? TranslationLanguages { get; init; }

    public List<OraccLine> Lines { get; init; } = new();
}

/// <summary>
/// One line of text, which is what the reader shows as a row.
/// </summary>
public sealed class OraccLine
{
    /// <summary>The line number as the edition prints it - "1", "2'", "i 14".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>"Q003414.2" - the reference Oracc uses to link back to this line.</summary>
    public string Reference { get; init; } = string.Empty;

    public List<OraccWord> Words { get; init; } = new();

    /// <summary>
    /// The line in transliteration, which is how Assyriology is normally read
    /// and written - "NUNUZ bal-til{KI} šu-qu-ru na-ram".
    /// </summary>
    public string Transliteration => string.Join(" ", Words.Select(w => w.Form));

    /// <summary>
    /// The line in cuneiform, assembled from the signs Oracc records for each
    /// word. Empty for words whose signs it does not carry, which is normal:
    /// a broken or unidentified sign has a transliteration and no glyph.
    /// </summary>
    public string Cuneiform => string.Join(" ", Words.Select(w => w.Cuneiform).Where(s => s.Length > 0));
}

/// <summary>
/// One word, with everything Oracc knows about it.
///
/// The lemmatisation is the reason this is worth importing rather than
/// scraping: every word arrives already tied to a dictionary entry, a sense
/// and a part of speech. That is the layer Word Study needs, and it is here
/// for Akkadian in a way it is emphatically not here for most languages.
/// </summary>
public sealed class OraccWord
{
    /// <summary>The transliterated form as the edition writes it: "bal-til{KI}".</summary>
    public string Form { get; init; } = string.Empty;

    /// <summary>
    /// The word in cuneiform signs, or empty where Oracc records none.
    ///
    /// Every one of these characters is outside the Basic Multilingual Plane,
    /// so each sign is a surrogate pair in a .NET string. Anything that walks
    /// this by char rather than by rune will see no letters at all - see
    /// LetterRuns.
    /// </summary>
    public string Cuneiform { get; init; } = string.Empty;

    /// <summary>The dictionary headword - "perʾu".</summary>
    public string? CitationForm { get; init; }

    /// <summary>
    /// The sense this instance carries - "offspring". Oracc's f.sense.
    /// </summary>
    public string? Sense { get; init; }

    /// <summary>
    /// The dictionary entry's guide word - "bud". Oracc's f.gw.
    ///
    /// Neither this nor Sense is a translation of the line. Both gloss the
    /// lexicon entry the word was tied to, which is a different and weaker
    /// claim, and the reader has to be able to tell them apart.
    /// </summary>
    public string? GuideWord { get; init; }

    /// <summary>
    /// Part of speech, and the interesting ones are not grammatical: DN is a
    /// divine name, PN a personal name, GN and QN geographic. Oracc types
    /// these rather than leaving them to be guessed from capitalisation, so a
    /// search for a god is a lookup rather than a string match.
    /// </summary>
    public string? PartOfSpeech { get; init; }

    /// <summary>The normalised Akkadian - "perʾi".</summary>
    public string? Normalized { get; init; }

    /// <summary>Which language this word is in: "akk", "sux" for Sumerian.</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>True when Oracc types this as a divine name.</summary>
    public bool IsDivineName => string.Equals(PartOfSpeech, "DN", StringComparison.Ordinal);
}
