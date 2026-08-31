namespace ClassicaCodex.Core.Meter;

/// <summary>A foot of a hexameter: three syllables or two.</summary>
public enum Foot
{
    /// <summary>Long, short, short.</summary>
    Dactyl,

    /// <summary>Long, long.</summary>
    Spondee
}

/// <summary>Why a line did not scan, when it did not.</summary>
public enum ScansionFailure
{
    None = 0,

    /// <summary>No letters in it at all - a stray citation, a bare numeral.</summary>
    Empty,

    /// <summary>
    /// Fewer than thirteen syllables after elision, which no arrangement of
    /// six feet can reach. Usually a half-line rather than a fault: Virgil
    /// left dozens of the Aeneid's lines unfinished and they are printed as
    /// he left them.
    /// </summary>
    TooShort,

    /// <summary>More than seventeen syllables, which six feet cannot hold.</summary>
    TooLong,

    /// <summary>
    /// The right number of syllables, and no arrangement of feet that agrees
    /// with what the spelling forces. This is the interesting failure: the
    /// count is right, so it is a quantity that is wrong, and the fault is
    /// either in the text or in this scanner.
    /// </summary>
    Inconsistent
}

/// <summary>What the scanner made of one line.</summary>
public sealed class Scansion
{
    public bool Scans => ReadingCount > 0;

    /// <summary>
    /// How many arrangements of feet fit both the syllable count and every
    /// quantity the spelling forces. One is a solved line. More than one is a
    /// line the letters underdetermine, and the count says by how much.
    /// </summary>
    public int ReadingCount { get; init; }

    /// <summary>
    /// The six feet, with null wherever the surviving readings disagree.
    /// Empty when nothing scanned.
    ///
    /// A partly-null result is the normal outcome for an ambiguous line and
    /// is worth more than it looks: two readings that differ only in the
    /// third foot still agree about the other five, and a reader wants the
    /// five.
    /// </summary>
    public IReadOnlyList<Foot?> Feet { get; init; } = Array.Empty<Foot?>();

    /// <summary>Every syllable, elided ones included and marked.</summary>
    public IReadOnlyList<ProsodicSyllable> Syllables { get; init; }
        = Array.Empty<ProsodicSyllable>();

    /// <summary>Syllables the metre counts - the line after elision.</summary>
    public int MetricalSyllables { get; init; }

    public int Elisions { get; init; }

    public ScansionFailure Failure { get; init; }

    /// <summary>
    /// The feet as letters, for a table or a log: D, S, or a question mark
    /// where the surviving readings disagree. Empty when the line did not
    /// scan.
    /// </summary>
    public string Pattern
    {
        get
        {
            if (Feet.Count == 0) return string.Empty;

            var letters = new char[Feet.Count];
            for (var i = 0; i < Feet.Count; i++)
            {
                letters[i] = Feet[i] switch
                {
                    Foot.Dactyl => 'D',
                    Foot.Spondee => 'S',
                    _ => '?'
                };
            }

            return new string(letters);
        }
    }
}

/// <summary>
/// Scans a line of Latin dactylic hexameter by elimination.
///
/// The method is the point. There is no table of vowel quantities here and no
/// dictionary lookup: Perseus prints Latin without macrons, so the letters
/// alone leave about half of every line undecided, and any scanner that
/// resolves those from a word list is only as good as the list and silent
/// about where it failed.
///
/// A hexameter is instead treated as a constraint. Six feet; the first five
/// each a dactyl or a spondee; the sixth two syllables with the last of them
/// free. That is thirty-two possible shapes holding between thirteen and
/// seventeen syllables. <see cref="LatinProsody"/> says which syllables are
/// forced long and which forced short, the shapes that contradict a forced
/// quantity are struck out, and what remains is the answer - however many
/// readings that is.
///
/// So the output is not "the scansion" but "the readings that survive", and
/// the count matters as much as the reading. One is solved. Several means the
/// line is genuinely underdetermined by its spelling, and saying so is more
/// use than picking the likeliest and not mentioning it. None means the
/// syllable count fit and the quantities did not, which is a fact about the
/// text or about this code, and either way wants looking at rather than
/// papering over.
/// </summary>
public static class HexameterScanner
{
    /// <summary>Feet whose shape can vary: the first five.</summary>
    private const int VariableFeet = 5;

    /// <summary>Four spondees, a dactyl fifth, and the closing two.</summary>
    public const int MinimumSyllables = 13;

    /// <summary>Five dactyls and the closing two.</summary>
    public const int MaximumSyllables = 17;

    /// <summary>
    /// Scans a line, trying every reading of its spelling.
    ///
    /// The letters of a Latin line are themselves sometimes ambiguous - see
    /// <see cref="LatinProsody.Syllabifications"/> - so the search runs over
    /// readings as well as over shapes, and the surviving count covers both.
    /// A line whose two readings each admit one shape is as underdetermined
    /// as a line with one reading and two shapes, and is reported the same
    /// way.
    /// </summary>
    public static Scansion Scan(string line)
    {
        var spellings = LatinProsody.Syllabifications(line);

        Scansion? best = null;
        var readings = new List<Foot[]>();
        IReadOnlyList<ProsodicSyllable>? scanned = null;
        var scannedLive = 0;
        var scannedElisions = 0;

        foreach (var spelling in spellings)
        {
            var attempt = ScanOne(spelling, readings);

            // The failure worth reporting is the most informative one: a line
            // that is the right length under some reading and will not scan
            // is a different problem from one that is the wrong length under
            // every reading, and hides behind it if the first failure wins.
            if (attempt is not null && (best is null || Worse(best.Failure, attempt.Failure)))
            {
                best = attempt;
            }

            if (attempt is null && scanned is null)
            {
                scanned = spelling;
                scannedLive = spelling.Count(s => !s.Elided);
                scannedElisions = spelling.Count - scannedLive;
            }
        }

        if (readings.Count == 0) return best ?? new Scansion { Failure = ScansionFailure.Empty };

        var agreed = new Foot?[VariableFeet + 1];
        for (var f = 0; f <= VariableFeet; f++)
        {
            var first = readings[0][f];
            agreed[f] = readings.All(r => r[f] == first) ? first : null;
        }

        return new Scansion
        {
            ReadingCount = readings.Count,
            Feet = agreed,
            Syllables = scanned ?? Array.Empty<ProsodicSyllable>(),
            MetricalSyllables = scannedLive,
            Elisions = scannedElisions,
            Failure = ScansionFailure.None
        };
    }

    /// <summary>Which of two failures says more about the line.</summary>
    private static bool Worse(ScansionFailure held, ScansionFailure candidate) =>
        Rank(candidate) > Rank(held);

    private static int Rank(ScansionFailure failure) => failure switch
    {
        ScansionFailure.Inconsistent => 3,
        ScansionFailure.TooLong => 2,
        ScansionFailure.TooShort => 1,
        _ => 0
    };

    /// <summary>
    /// Tries one reading of the spelling against all thirty-two shapes,
    /// adding whatever fits to <paramref name="readings"/>. Returns the
    /// failure if nothing fitted, and null if something did.
    /// </summary>
    private static Scansion? ScanOne(
        IReadOnlyList<ProsodicSyllable> syllables, List<Foot[]> readings)
    {
        var live = syllables.Where(s => !s.Elided).ToList();
        var elisions = syllables.Count - live.Count;

        Scansion Failed(ScansionFailure why) => new()
        {
            ReadingCount = 0,
            Syllables = syllables,
            MetricalSyllables = live.Count,
            Elisions = elisions,
            Failure = why
        };

        if (live.Count == 0) return Failed(ScansionFailure.Empty);
        if (live.Count < MinimumSyllables) return Failed(ScansionFailure.TooShort);
        if (live.Count > MaximumSyllables) return Failed(ScansionFailure.TooLong);

        var before = readings.Count;

        // Thirty-two shapes, tried in full. A spondaic fifth foot is rare
        // enough to have a name - versus spondaicus - and is included rather
        // than assumed away: fixing the fifth foot as a dactyl would resolve
        // some ambiguous lines by fiat and mis-scan the handful that really
        // are spondaic, which is the trade this scanner exists to avoid.
        for (var mask = 0; mask < 1 << VariableFeet; mask++)
        {
            var feet = new Foot[VariableFeet + 1];
            var syllableCount = 2;   // the closing foot is always two

            for (var f = 0; f < VariableFeet; f++)
            {
                var dactyl = (mask & (1 << f)) != 0;
                feet[f] = dactyl ? Foot.Dactyl : Foot.Spondee;
                syllableCount += dactyl ? 3 : 2;
            }

            feet[VariableFeet] = Foot.Spondee;
            if (syllableCount != live.Count) continue;
            if (Fits(feet, live)) readings.Add(feet);
        }

        return readings.Count == before ? Failed(ScansionFailure.Inconsistent) : null;
    }

    /// <summary>
    /// Whether a shape contradicts anything the spelling forces.
    ///
    /// Only a contradiction rules a shape out. An Unknown syllable fits
    /// wherever it is put, which is the whole reason more than one reading
    /// can survive.
    /// </summary>
    private static bool Fits(IReadOnlyList<Foot> feet, IReadOnlyList<ProsodicSyllable> live)
    {
        var at = 0;

        for (var f = 0; f < feet.Count; f++)
        {
            // Every foot opens long.
            if (live[at].Quantity == Quantity.Short) return false;
            at++;

            if (f == feet.Count - 1)
            {
                // The last syllable of the line is free - brevis in longo, a
                // short syllable standing where a long one belongs because
                // the line ends there. Nothing about it can rule a shape out.
                at++;
                continue;
            }

            if (feet[f] == Foot.Dactyl)
            {
                if (live[at].Quantity == Quantity.Long) return false;
                at++;
                if (live[at].Quantity == Quantity.Long) return false;
                at++;
            }
            else
            {
                if (live[at].Quantity == Quantity.Short) return false;
                at++;
            }
        }

        return true;
    }
}
