using ClassicaCodex.Core.Stylometry;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// The Delta calculation moved out of StylometryForm into Core, unchanged.
///
/// It had to move: leave-one-out validation, parameter grids and perturbation
/// series all need to run it hundreds to thousands of times with no window
/// open, and a private method on a WinForms class cannot be called that way.
///
/// These are characterisation tests. They exist because "unchanged" is a claim
/// about a calculation nobody can check by reading, and because several of the
/// decisions inside it are the kind that get undone by a well-meaning
/// refactor: excluding the target's own chunks, seeding the shuffle from the
/// work id, dropping the remainder rather than keeping a short final sample.
/// Each of those was made for a documented reason and each has a test here.
/// </summary>
public class DeltaEngineTests
{
    /// <summary>
    /// A pool where authorship is not in question: two authors with strongly
    /// different function-word habits, built long enough to chunk.
    /// </summary>
    private static List<WorkTokens> Pool(int chunkTokens = 300)
    {
        var works = new List<WorkTokens>();

        // Author A leans on "men" and "de"; author B on "gar" and "oun".
        var a = new[] { "men", "de", "kai", "ho", "men", "de", "to", "kai" };
        var b = new[] { "gar", "oun", "kai", "ho", "gar", "oun", "to", "kai" };

        for (var i = 0; i < 3; i++)
            works.Add(new WorkTokens(10 + i, "Alpha", $"A{i}", Repeat(a, chunkTokens * 2)));
        for (var i = 0; i < 3; i++)
            works.Add(new WorkTokens(20 + i, "Beta", $"B{i}", Repeat(b, chunkTokens * 2)));

        return works;
    }

    private static List<string> Repeat(string[] pattern, int count)
    {
        var list = new List<string>(count);
        for (var i = 0; i < count; i++) list.Add(pattern[i % pattern.Length]);
        return list;
    }

    /// <summary>
    /// A pool of non-periodic works: the same twelve function words in every
    /// work, drawn at per-work frequencies from a fixed seed.
    ///
    /// Pool's works repeat an eight-token pattern, which is convenient for
    /// asserting who is nearest to whom and useless for anything that depends
    /// on two bags of one work differing - drawn from a periodic stream they
    /// hold the same counts. This is what real text is like in the one respect
    /// that matters here: a bag is a sample, and two samples are not identical.
    /// </summary>
    private static List<WorkTokens> VariedPool()
    {
        var vocab = new[] { "men", "de", "kai", "ho", "to", "gar", "oun", "te", "alla", "hos", "ei", "ou" };

        List<string> Draw(int seed, int count)
        {
            var rng = new Random(seed);
            var weights = vocab.Select(_ => rng.NextDouble() + 0.1).ToArray();
            var total = weights.Sum();
            var tokens = new List<string>(count);

            for (var i = 0; i < count; i++)
            {
                var pick = rng.NextDouble() * total;
                var w = 0;
                while (w < vocab.Length - 1 && pick > weights[w]) pick -= weights[w++];
                tokens.Add(vocab[w]);
            }

            return tokens;
        }

        var works = new List<WorkTokens>();
        for (var i = 0; i < 3; i++) works.Add(new WorkTokens(10 + i, "Alpha", $"A{i}", Draw(100 + i, 900)));
        for (var i = 0; i < 3; i++) works.Add(new WorkTokens(20 + i, "Beta", $"B{i}", Draw(200 + i, 900)));
        return works;
    }

    // ------------------------------------------------------- determinism

    /// <summary>
    /// The shuffle is seeded from the work id so a given work always yields the
    /// same bags. A run that cannot be repeated cannot be compared against, and
    /// every saved run in the database was produced under that assumption.
    /// </summary>
    [Fact]
    public void TheSameInputGivesTheSameResultEveryTime()
    {
        var pool = Pool();
        var first = DeltaEngine.Compute(pool, 10, new DeltaSettings(50, 300));
        var second = DeltaEngine.Compute(pool, 10, new DeltaSettings(50, 300));

        Assert.Equal(
            first.Neighbors.Select(n => (n.Label, n.Delta)),
            second.Neighbors.Select(n => (n.Label, n.Delta)));
    }

    /// <summary>
    /// Bag membership is a function of (tokens, chunk size, seed) alone, which
    /// is what makes runs comparable - and what means a perturbation experiment
    /// has to get its variation from the injection's own seed rather than from
    /// re-running.
    /// </summary>
    [Fact]
    public void ChunksAreAFunctionOfTheSeedAlone()
    {
        var tokens = Repeat(new[] { "a", "b", "c", "d", "e" }, 1000);

        var one = DeltaEngine.SplitIntoChunks(tokens, 300, seed: 42);
        var two = DeltaEngine.SplitIntoChunks(tokens, 300, seed: 42);
        var other = DeltaEngine.SplitIntoChunks(tokens, 300, seed: 43);

        Assert.Equal(one.Select(c => string.Join(",", c)), two.Select(c => string.Join(",", c)));
        Assert.NotEqual(one[0], other[0]);
    }

    // ------------------------------------------------------------ sampling

    /// <summary>
    /// Every unit holds exactly the sample size, and the remainder is dropped
    /// rather than kept as a short final chunk. A noisier short unit in a pool
    /// of full ones is the length effect that chunking exists to remove.
    /// </summary>
    [Fact]
    public void RemainderTokensAreDiscardedRatherThanFormingAShortChunk()
    {
        var tokens = Repeat(new[] { "a", "b" }, 1000);

        var chunks = DeltaEngine.SplitIntoChunks(tokens, 300, seed: 1);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(300, c.Count));
    }

    /// <summary>
    /// Discarded tokens are reported rather than silently dropped. Rhesus at
    /// 5,431 tokens yields one 3,000-token sample and 2,431 tokens go unused;
    /// that is a real cost and it has to be visible.
    /// </summary>
    [Fact]
    public void DiscardedTokensAreReported()
    {
        var pool = new List<WorkTokens>
        {
            new(1, "Alpha", "Long", Repeat(new[] { "men", "de", "kai" }, 1000)),
            new(2, "Beta", "Also", Repeat(new[] { "gar", "oun", "kai" }, 1000))
        };

        var result = DeltaEngine.Compute(pool, 1, new DeltaSettings(20, 300));

        Assert.Equal(6, result.SampleCount);          // 3 each
        Assert.Equal(200, result.DiscardedTokens);    // 100 each
    }

    /// <summary>
    /// A work shorter than one sample is dropped from the pool and named, not
    /// padded and not kept whole.
    /// </summary>
    [Fact]
    public void AWorkTooShortToSampleIsDroppedAndNamed()
    {
        var pool = Pool();
        pool.Add(new WorkTokens(99, "Gamma", "Fragment", Repeat(new[] { "kai", "de" }, 40)));

        var result = DeltaEngine.Compute(pool, 10, new DeltaSettings(50, 300));

        Assert.Equal("Fragment", Assert.Single(result.WorksTooShort));
        Assert.DoesNotContain(result.Neighbors, n => n.WorkId == 99);
    }

    /// <summary>
    /// And a TARGET too short to sample is an error rather than a silent empty
    /// result, because the alternative is a run that looks like it worked.
    /// </summary>
    [Fact]
    public void ATargetTooShortToSampleThrows()
    {
        var pool = Pool();
        pool.Add(new WorkTokens(99, "Gamma", "Fragment", Repeat(new[] { "kai", "de" }, 40)));

        var ex = Assert.Throws<InvalidOperationException>(
            () => DeltaEngine.Compute(pool, 99, new DeltaSettings(50, 300)));

        Assert.Contains("Fragment", ex.Message);
    }

    // ------------------------------------------------------ what it measures

    /// <summary>
    /// The floor of the sanity check: with two authors whose function-word
    /// habits differ, a work's nearest neighbours are its own author's.
    /// </summary>
    [Fact]
    public void AWorkIsNearestToItsOwnAuthor()
    {
        var result = DeltaEngine.Compute(Pool(), 10, new DeltaSettings(50, 300));

        Assert.Equal("Alpha", result.Neighbors[0].AuthorName);
    }

    /// <summary>
    /// The target's own chunks never appear among its neighbours. Two samples
    /// of one text are about as close as two samples get, so leaving them in
    /// would push the first outsider down by however many chunks the target
    /// happens to have - which is the length effect arriving by another route.
    /// </summary>
    [Fact]
    public void TheTargetsOwnChunksAreExcluded()
    {
        var result = DeltaEngine.Compute(Pool(), 10, new DeltaSettings(50, 300));

        Assert.DoesNotContain(result.Neighbors, n => n.WorkId == 10);
    }

    /// <summary>
    /// One entry per WORK, not per edition. A work carrying three editions
    /// would otherwise contribute its values three times to every feature's
    /// mean and standard deviation, and the multi-edition works cluster in
    /// Aeschylus and Sophocles - exactly the authors a Euripides comparison is
    /// measured against.
    /// </summary>
    [Fact]
    public void DuplicateEditionsOfOneWorkCountOnce()
    {
        var pool = Pool();
        var duplicate = pool.First(w => w.WorkId == 20);
        pool.Add(new WorkTokens(20, "Beta", "B0 (another edition)", duplicate.Tokens));

        var result = DeltaEngine.Compute(pool, 10, new DeltaSettings(50, 300));

        Assert.DoesNotContain(result.Neighbors, n => n.Label.Contains("another edition"));
    }

    /// <summary>
    /// Reported token count is the work's full length, not the sample's. At
    /// fixed sample size the sample length is a constant and says nothing; the
    /// full length is what the length-confound analysis needs.
    /// </summary>
    [Fact]
    public void TokenCountIsTheWholeWorkNotTheSample()
    {
        var pool = Pool(chunkTokens: 300);
        var result = DeltaEngine.Compute(pool, 10, new DeltaSettings(50, 300));

        Assert.Equal(pool.First(w => w.WorkId == 10).Tokens.Count, result.TargetTokenCount);
        Assert.NotEqual(300, result.TargetTokenCount);
    }

    // ------------------------------------------- measuring from other samples

    /// <summary>
    /// The form has always measured from the target's FIRST sample. Validation
    /// should not judge a three-sample work on one of its samples and discard
    /// the other two, so the sample is selectable - and reading the same work
    /// from each of its samples in turn is the cheapest estimate available of
    /// how much one run's answer depends on which bag it happened to draw.
    ///
    /// This uses VariedPool rather than Pool. The two-word-pattern works in
    /// Pool are perfectly periodic, so any two bags drawn from one of them hold
    /// almost exactly the same counts and the two runs come out equal to within
    /// floating-point noise - measured at 8e-17, which would make an inequality
    /// assertion a coin flip. Real text is not periodic; VariedPool is not
    /// either, and the same comparison there differs by 6e-2.
    /// </summary>
    [Fact]
    public void MeasuringFromADifferentSampleGivesADifferentResult()
    {
        var pool = VariedPool();

        var first = DeltaEngine.Compute(pool, 10, new DeltaSettings(50, 300), targetChunkIndex: 0);
        var second = DeltaEngine.Compute(pool, 10, new DeltaSettings(50, 300), targetChunkIndex: 1);

        // Same neighbours, materially different distances - the pool has not
        // changed, only which sample of the target is being measured from.
        Assert.Equal(
            first.Neighbors.Select(n => n.WorkId).OrderBy(x => x),
            second.Neighbors.Select(n => n.WorkId).OrderBy(x => x));

        var shift = Math.Abs(first.Neighbors[0].Delta - second.Neighbors[0].Delta);
        Assert.True(shift > 1e-3, $"expected the sample choice to move the result, moved by {shift:E2}");
    }

    /// <summary>
    /// Asking for a sample the target does not have is an error naming the
    /// count, not an off-by-one that quietly measures the wrong text.
    /// </summary>
    [Fact]
    public void AskingForASampleTheTargetDoesNotHaveThrows()
    {
        var pool = Pool();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => DeltaEngine.Compute(pool, 10, new DeltaSettings(50, 300), targetChunkIndex: 99));
    }

    // ------------------------------------------------------------ tokenizer

    /// <summary>
    /// Elision marks are stripped uniformly across all four codepoints. Whether
    /// an elided form is written with U+02BC, U+2019, U+1FBD or an ASCII
    /// apostrophe is an editorial decision, not an authorial one, and left in
    /// place it becomes the highest-weighted feature in a Greek run - i.e. the
    /// analysis measures which editor prepared the text.
    /// </summary>
    [Theory]
    [InlineData("δ\u02BC")]
    [InlineData("δ\u2019")]
    [InlineData("δ\u1FBD")]
    [InlineData("δ'")]
    public void EveryElisionCodepointTokenizesTheSameWay(string elided)
    {
        var tokens = StylometryTokenizer.Tokenize(elided, foldAccents: true);

        Assert.Equal("δ", Assert.Single(tokens));
    }

    /// <summary>
    /// A token that normalises away entirely is not counted. It would otherwise
    /// inflate the denominator with nothing.
    /// </summary>
    [Fact]
    public void TokensThatNormaliseToNothingAreDropped()
    {
        Assert.Empty(StylometryTokenizer.Tokenize("' \u2019 ,,, 123", foldAccents: true));
    }
}
