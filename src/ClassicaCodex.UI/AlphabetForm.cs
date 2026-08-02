using System.Text;

namespace ClassicaCodex.UI;

/// <summary>
/// The alphabet of the language being translated from, plus the marks that
/// sit on it.
///
/// Not a translation aid - it doesn't help with meaning at all. It's for the
/// step before that: someone who can't yet read the script can't look a word
/// up, can't tell which letters they're seeing, and can't type a headword
/// into a search box. Every other tool in the workbench assumes you're past
/// that point, and this is the one thing that gets you there.
///
/// The diacritics matter more than the letters for actually reading Greek,
/// which is why they get as much room here. Breathings change how a word is
/// pronounced and are easy to miss entirely at reading size; the iota
/// subscript is invisible to anyone who doesn't know to look for it and
/// changes the grammatical case when it's there.
/// </summary>
public class AlphabetForm : Form
{
    public AlphabetForm(string? languageCode)
    {
        var language = (languageCode ?? string.Empty).ToLowerInvariant();

        Text = language switch
        {
            "grc" or "greek" => "The Greek Alphabet",
            "lat" or "latin" => "The Latin Alphabet",
            _ => "Alphabet"
        };

        AppIcons.ApplyWindowIcon(this, "WordStudy");
        ClientSize = new Size(640, 620);
        MinimumSize = new Size(520, 420);
        StartPosition = FormStartPosition.CenterParent;

        var body = new TextBox
        {
            Left = 12,
            Top = 12,
            Width = 616,
            Height = 556,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = new Font("Palatino Linotype", 11F),
            Text = BuildReference(language)
        };

        var closeButton = new Button
        {
            Text = "Close",
            Left = 552,
            Top = 578,
            Width = 76,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        CancelButton = closeButton;

        Controls.Add(body);
        Controls.Add(closeButton);

        // Opens unselected, like the work details view - this is something
        // to read, and a wall of highlighted text is not.
        Shown += (_, _) =>
        {
            body.SelectionStart = 0;
            body.SelectionLength = 0;
            ActiveControl = closeButton;
        };

        ReadingTheme.AttachTo(this);
    }

    private static string BuildReference(string language) => language switch
    {
        "grc" or "greek" => GreekReference(),
        "lat" or "latin" => LatinReference(),
        _ => "No alphabet reference for this language.\r\n\r\n" +
             "This is here for scripts you may not read yet - Greek especially. " +
             "A text already in the Latin alphabet needs no chart."
    };

    private static string GreekReference()
    {
        var text = new StringBuilder();

        text.AppendLine("THE LETTERS");
        text.AppendLine();
        text.AppendLine("  Upper  Lower  Name       Roughly");
        text.AppendLine("  -----  -----  ---------  -------------------------");

        var letters = new (string Upper, string Lower, string Name, string Sound)[]
        {
            ("Α", "α", "alpha", "a, as in father"),
            ("Β", "β", "beta", "b"),
            ("Γ", "γ", "gamma", "g, hard as in go"),
            ("Δ", "δ", "delta", "d"),
            ("Ε", "ε", "epsilon", "e, short as in pet"),
            ("Ζ", "ζ", "zeta", "zd, or z"),
            ("Η", "η", "eta", "e, long as in they"),
            ("Θ", "θ", "theta", "th"),
            ("Ι", "ι", "iota", "i"),
            ("Κ", "κ", "kappa", "k"),
            ("Λ", "λ", "lambda", "l"),
            ("Μ", "μ", "mu", "m"),
            ("Ν", "ν", "nu", "n"),
            ("Ξ", "ξ", "xi", "x, as in axe"),
            ("Ο", "ο", "omicron", "o, short as in pot"),
            ("Π", "π", "pi", "p"),
            ("Ρ", "ρ", "rho", "r"),
            ("Σ", "σ / ς", "sigma", "s   (ς only at a word's end)"),
            ("Τ", "τ", "tau", "t"),
            ("Υ", "υ", "upsilon", "u, French tu"),
            ("Φ", "φ", "phi", "ph, as in phone"),
            ("Χ", "χ", "chi", "ch, as in Scottish loch"),
            ("Ψ", "ψ", "psi", "ps, as in lapse"),
            ("Ω", "ω", "omega", "o, long as in bone")
        };

        foreach (var (upper, lower, name, sound) in letters)
        {
            text.AppendLine($"  {upper,-6} {lower,-6} {name,-10} {sound}");
        }

        text.AppendLine();
        text.AppendLine();
        text.AppendLine("THE MARKS ABOVE AND BELOW");
        text.AppendLine();
        text.AppendLine("These are not decoration. They are part of the word, and the app's");
        text.AppendLine("search ignores them so you can type without hunting for the right key.");
        text.AppendLine();
        text.AppendLine("  Breathings - every word starting with a vowel has one");
        text.AppendLine("     ἀ   smooth   no extra sound");
        text.AppendLine("     ἁ   rough    an h before the vowel: ἁ = ha");
        text.AppendLine("     ῥ   rho at the start of a word always takes the rough breathing");
        text.AppendLine();
        text.AppendLine("  Accents - which syllable is raised");
        text.AppendLine("     ά   acute");
        text.AppendLine("     ὰ   grave    an acute pushed down by the word that follows");
        text.AppendLine("     ᾶ   circumflex");
        text.AppendLine();
        text.AppendLine("  Iota subscript - the tiny hook underneath");
        text.AppendLine("     ᾳ ῃ ῳ");
        text.AppendLine("     Easy to miss, and it usually marks the dative case - so it often");
        text.AppendLine("     carries the difference between \"the goddess\" and \"to the goddess\".");
        text.AppendLine();
        text.AppendLine("  Diaeresis - two dots");
        text.AppendLine("     ϊ ϋ     the vowel is its own syllable, not part of a pair");
        text.AppendLine();
        text.AppendLine();
        text.AppendLine("PAIRS THAT ARE ONE SOUND");
        text.AppendLine();
        text.AppendLine("     αι  as in aisle        ει  as in eight");
        text.AppendLine("     οι  as in boil         ου  as in soup");
        text.AppendLine("     αυ  as in how          ευ  eh-oo, run together");
        text.AppendLine();
        text.AppendLine("     γγ γκ γχ  the first gamma is pronounced n: ἄγγελος = angelos");

        return text.ToString();
    }

    private static string LatinReference()
    {
        var text = new StringBuilder();

        text.AppendLine("THE LETTERS");
        text.AppendLine();
        text.AppendLine("The same 26 letters you already read, with a few differences worth");
        text.AppendLine("knowing - they are why a word can look unfamiliar even when it isn't.");
        text.AppendLine();
        text.AppendLine("  C   always hard, as in cat - never as in city");
        text.AppendLine("  G   always hard, as in go");
        text.AppendLine("  V   written for both u and v: VENIT is venit");
        text.AppendLine("  I   written for both i and j: IAM is iam");
        text.AppendLine("  Y Z only in words borrowed from Greek");
        text.AppendLine("  W   does not exist");
        text.AppendLine();
        text.AppendLine();
        text.AppendLine("VOWEL LENGTH");
        text.AppendLine();
        text.AppendLine("  A macron marks a long vowel: ā ē ī ō ū");
        text.AppendLine();
        text.AppendLine("  Printed texts often leave it out entirely, and length can be the");
        text.AppendLine("  only difference between two forms - rosa \"rose\" against rosā");
        text.AppendLine("  \"by a rose\". If a form looks wrong, this is often why.");
        text.AppendLine();
        text.AppendLine();
        text.AppendLine("PAIRS THAT ARE ONE SOUND");
        text.AppendLine();
        text.AppendLine("     ae  as in aisle       oe  as in oil");
        text.AppendLine("     au  as in how         ei  as in eight");

        return text.ToString();
    }
}
