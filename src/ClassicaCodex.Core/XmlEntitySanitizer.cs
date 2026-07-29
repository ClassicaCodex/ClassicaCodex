using System.Text.RegularExpressions;

namespace ClassicaCodex.Core;

/// <summary>
/// Resolves named XML entities that a document references but never
/// declares in a way the parser can see.
///
/// Perseus's TEI P4 files - both the texts and the lexica - declare their
/// entity set in an external DTD hosted on perseus.tufts.edu. .NET won't
/// fetch that (and shouldn't), so every &amp;iacute; or &amp;mdash; in the
/// file reads as an undeclared entity and the whole parse fails. Resolving
/// them to their actual characters up front sidesteps the DTD entirely.
///
/// Unrecognized entities are dropped rather than guessed at: losing an
/// obscure character beats failing to read a 77MB dictionary over one
/// unknown name.
/// </summary>
public static class XmlEntitySanitizer
{
    // Entity names allow '.', '-' and ':', not just \w - Perseus references
    // dotted metadata entities like &Perseus.publish;. Matching only \w left
    // those in place and the parser rejected the file as having an undeclared
    // entity; widening the pattern lets them be dropped like any other
    // unrecognized name.
    private static readonly Regex EntityPattern = new(@"&([A-Za-z_:][\w.:-]*);", RegexOptions.Compiled);

    // The five entities XML declares itself - these must survive untouched.
    private static readonly HashSet<string> StandardXmlEntities = new(StringComparer.Ordinal)
    {
        "amp", "lt", "gt", "quot", "apos"
    };

    private static readonly Dictionary<string, string> NamedEntities = new(StringComparer.Ordinal)
    {
        ["nbsp"] = "\u00A0", ["iexcl"] = "\u00A1", ["cent"] = "\u00A2", ["pound"] = "\u00A3",
        ["curren"] = "\u00A4", ["yen"] = "\u00A5", ["brvbar"] = "\u00A6", ["sect"] = "\u00A7",
        ["uml"] = "\u00A8", ["copy"] = "\u00A9", ["ordf"] = "\u00AA", ["laquo"] = "\u00AB",
        ["not"] = "\u00AC", ["shy"] = "\u00AD", ["reg"] = "\u00AE", ["macr"] = "\u00AF",
        ["deg"] = "\u00B0", ["plusmn"] = "\u00B1", ["sup2"] = "\u00B2", ["sup3"] = "\u00B3",
        ["acute"] = "\u00B4", ["micro"] = "\u00B5", ["para"] = "\u00B6", ["middot"] = "\u00B7",
        ["cedil"] = "\u00B8", ["sup1"] = "\u00B9", ["ordm"] = "\u00BA", ["raquo"] = "\u00BB",
        ["frac14"] = "\u00BC", ["frac12"] = "\u00BD", ["frac34"] = "\u00BE", ["iquest"] = "\u00BF",
        ["Agrave"] = "\u00C0", ["Aacute"] = "\u00C1", ["Acirc"] = "\u00C2", ["Atilde"] = "\u00C3",
        ["Auml"] = "\u00C4", ["Aring"] = "\u00C5", ["AElig"] = "\u00C6", ["Ccedil"] = "\u00C7",
        ["Egrave"] = "\u00C8", ["Eacute"] = "\u00C9", ["Ecirc"] = "\u00CA", ["Euml"] = "\u00CB",
        ["Igrave"] = "\u00CC", ["Iacute"] = "\u00CD", ["Icirc"] = "\u00CE", ["Iuml"] = "\u00CF",
        ["ETH"] = "\u00D0", ["Ntilde"] = "\u00D1", ["Ograve"] = "\u00D2", ["Oacute"] = "\u00D3",
        ["Ocirc"] = "\u00D4", ["Otilde"] = "\u00D5", ["Ouml"] = "\u00D6", ["times"] = "\u00D7",
        ["Oslash"] = "\u00D8", ["Ugrave"] = "\u00D9", ["Uacute"] = "\u00DA", ["Ucirc"] = "\u00DB",
        ["Uuml"] = "\u00DC", ["Yacute"] = "\u00DD", ["THORN"] = "\u00DE", ["szlig"] = "\u00DF",
        ["agrave"] = "\u00E0", ["aacute"] = "\u00E1", ["acirc"] = "\u00E2", ["atilde"] = "\u00E3",
        ["auml"] = "\u00E4", ["aring"] = "\u00E5", ["aelig"] = "\u00E6", ["ccedil"] = "\u00E7",
        ["egrave"] = "\u00E8", ["eacute"] = "\u00E9", ["ecirc"] = "\u00EA", ["euml"] = "\u00EB",
        ["igrave"] = "\u00EC", ["iacute"] = "\u00ED", ["icirc"] = "\u00EE", ["iuml"] = "\u00EF",
        ["eth"] = "\u00F0", ["ntilde"] = "\u00F1", ["ograve"] = "\u00F2", ["oacute"] = "\u00F3",
        ["ocirc"] = "\u00F4", ["otilde"] = "\u00F5", ["ouml"] = "\u00F6", ["divide"] = "\u00F7",
        ["oslash"] = "\u00F8", ["ugrave"] = "\u00F9", ["uacute"] = "\u00FA", ["ucirc"] = "\u00FB",
        ["uuml"] = "\u00FC", ["yacute"] = "\u00FD", ["thorn"] = "\u00FE", ["yuml"] = "\u00FF",

        ["ndash"] = "\u2013", ["mdash"] = "\u2014", ["lsquo"] = "\u2018", ["rsquo"] = "\u2019",
        ["sbquo"] = "\u201A", ["ldquo"] = "\u201C", ["rdquo"] = "\u201D", ["bdquo"] = "\u201E",
        ["dagger"] = "\u2020", ["Dagger"] = "\u2021", ["permil"] = "\u2030", ["lsaquo"] = "\u2039",
        ["rsaquo"] = "\u203A", ["euro"] = "\u20AC", ["trade"] = "\u2122", ["hellip"] = "\u2026",
        ["OElig"] = "\u0152", ["oelig"] = "\u0153", ["Scaron"] = "\u0160", ["scaron"] = "\u0161",
        ["Yuml"] = "\u0178", ["circ"] = "\u02C6", ["tilde"] = "\u02DC",

        // Length marks - these turn up constantly in dictionary headwords,
        // where vowel quantity is part of the entry (rāpi, rūpī).
        ["amacr"] = "\u0101", ["emacr"] = "\u0113", ["imacr"] = "\u012B",
        ["omacr"] = "\u014D", ["umacr"] = "\u016B", ["ymacr"] = "\u0233",
        ["Amacr"] = "\u0100", ["Emacr"] = "\u0112", ["Imacr"] = "\u012A",
        ["Omacr"] = "\u014C", ["Umacr"] = "\u016A",
        ["abreve"] = "\u0103", ["ebreve"] = "\u0115", ["ibreve"] = "\u012D",
        ["obreve"] = "\u014F", ["ubreve"] = "\u016D",
        ["Abreve"] = "\u0102", ["Ebreve"] = "\u0114", ["Ibreve"] = "\u012C",
        ["Obreve"] = "\u014E", ["Ubreve"] = "\u016C",

        // Parentheses and a few accented letters that turn up in the
        // Renaissance / early-modern English corpus (Holinshed's and
        // Hakluyt's old-spelling prose, foreign proper names) but weren't in
        // the classical set. lpar/rpar are literal parentheses - dropping
        // them silently mangled the text. sacute stands in for the long-s in
        // some old-spelling passages ("bicau&sacute;e" -> "bicauśe"); ś is the
        // standard expansion and at least preserves a visible character.
        ["lpar"] = "\u0028", ["rpar"] = "\u0029",
        ["cacute"] = "\u0107", ["nacute"] = "\u0144", ["racute"] = "\u0155",
        ["sacute"] = "\u015B", ["gacute"] = "\u01F5", ["uring"] = "\u016F",
        ["ecaron"] = "\u011B"
    };

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input) || input.IndexOf('&') < 0) return input;

        return EntityPattern.Replace(input, match =>
        {
            var name = match.Groups[1].Value;

            if (StandardXmlEntities.Contains(name)) return match.Value;

            return NamedEntities.TryGetValue(name, out var replacement) ? replacement : string.Empty;
        });
    }
}
