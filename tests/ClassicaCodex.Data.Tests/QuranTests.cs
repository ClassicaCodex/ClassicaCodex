using ClassicaCodex.Core;
using ClassicaCodex.Ingestion.Quran;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the Quran out of Tanzil's XML and Gutenberg's three translations,
/// and finding its words in either spelling.
///
/// The Arabic samples are copied from Tanzil's own files, marks and all: the
/// Uthmani text of 1.1, 2.1 and 9.1 and the simple text of the same verses.
/// </summary>
public class QuranTests
{
    private const string Tanzil = """
        <?xml version="1.0" encoding="utf-8" ?>
        <!--

        # PLEASE DO NOT REMOVE OR CHANGE THIS COPYRIGHT BLOCK
        #  Tanzil Quran Text (Uthmani, Version 1.1)
        #  Copyright (C) 2007-2026 Tanzil Project
        #  License: Creative Commons Attribution 3.0

        -->
        <quran>
        	<sura index="1" name="الفاتحة">
        		<aya index="1" text="بِسْمِ ٱللَّهِ ٱلرَّحْمَـٰنِ ٱلرَّحِيمِ" />
        		<aya index="2" text="ٱلْحَمْدُ لِلَّهِ رَبِّ ٱلْعَـٰلَمِينَ" />
        	</sura>
        	<sura index="2" name="البقرة">
        		<aya index="1" text="الٓمٓ" bismillah="بِسْمِ ٱللَّهِ ٱلرَّحْمَـٰنِ ٱلرَّحِيمِ" />
        		<aya index="2" text="ذَٰلِكَ ٱلْكِتَـٰبُ لَا رَيْبَ ۛ فِيهِ ۛ هُدًى لِّلْمُتَّقِينَ" />
        	</sura>
        </quran>
        """;

    [Fact]
    public void TanzilGivesTheSurasTheirVersesAndTheNotice()
    {
        var (suras, notice) = QuranLoader.ParseTanzil(Tanzil);

        Assert.Equal(new[] { 1, 2 }, suras.Select(s => s.Number));
        Assert.Equal("البقرة", suras[1].Name);
        Assert.Equal(new[] { 1, 2 }, suras[1].Verses.Select(v => v.Verse));
        Assert.Equal("الٓمٓ", suras[1].Verses[0].Text);

        Assert.StartsWith("# PLEASE DO NOT REMOVE OR CHANGE THIS COPYRIGHT BLOCK", notice);
        Assert.Contains("License: Creative Commons Attribution 3.0", notice);
    }

    /// <summary>
    /// The basmala heads the second sura and is not its first verse; in the
    /// first sura it is the first verse, and there is no heading.
    /// </summary>
    [Fact]
    public void TheBasmalaIsASurasHeadingAndNotItsFirstVerse()
    {
        var (suras, _) = QuranLoader.ParseTanzil(Tanzil);

        var opening = QuranIngestService.Arabic(suras[0], 1);
        var cow = QuranIngestService.Arabic(suras[1], 2);

        Assert.Equal(new[] { "1.1", "1.2" }, opening.Select(p => p.Citation));
        Assert.DoesNotContain(opening, p => p.IsHead);

        Assert.Equal(new[] { "2.head", "2.1", "2.2" }, cow.Select(p => p.Citation));
        Assert.True(cow[0].IsHead);
        Assert.Equal("بِسْمِ ٱللَّهِ ٱلرَّحْمَـٰنِ ٱلرَّحِيمِ", cow[0].Text);
        Assert.Equal("الٓمٓ", cow[1].Text);
    }

    [Fact]
    public void ASuraOutOfPlaceIsRefused()
    {
        var (suras, _) = QuranLoader.ParseTanzil(Tanzil);

        Assert.Throws<InvalidDataException>(() => QuranIngestService.Arabic(suras[1], 3));
    }

    [Fact]
    public void ThereAre114SurasInTheTable()
    {
        Assert.Equal(114, QuranIngestService.Suras.Count);
        Assert.Equal(("Al-Fatihah", "The Opening"), QuranIngestService.Suras[0]);
        Assert.Equal(("Al-Jinn", "The Jinn"), QuranIngestService.Suras[71]);
        Assert.Equal(("An-Nas", "Mankind"), QuranIngestService.Suras[113]);
    }

    private const string Gutenberg = """
        The Project Gutenberg EBook of Three Translations of The Koran (Al-Qur'an)

        *** START OF THIS PROJECT GUTENBERG EBOOK TRANSLATIONS OF THE KORAN ***

        Y: = Qur'an Translation by Abdullah Yusuf Ali

        P: = Qur'an Translation by Marmaduke Pickthall

        ---------------------------------------------------------------------

         Chapter 1:

                                  AL-FATIHA (THE OPENING)
         Total Verses: 2 Revealed At: MAKKA

        ---------------------------------------------------------------------

        001.001
        Y: In the name of Allah, the Most Beneficent, the Most Merciful.

        P: In the name of Allah, the Beneficent, the Merciful.

        S: In the name of Allah, the Beneficent, the Merciful.

        001.002
        Y: Praise be to Allah, the Cherisher and Sustainer of the worlds;

        P: Praise be to Allah, Lord of the Worlds,

        S: All praise is due to Allah, the Lord of the Worlds.

         Chapter 2:

        002.001
        Y: A. L. M.

        P: Alif. Lam. Mim.

        S: Alif Lam Mim.

        0.002
        Y: This is the Book; in it is guidance sure, without doubt, to those
        who fear Allah;

        P: This is the Scripture whereof there is no doubt, a guidance unto
        those who ward off (evil).

        S: This Book, there is no doubt in it, is a guide to those who guard
        (against evil).

        002.03
        Y: Who believe in the Unseen,

        P: Who believe in the Unseen,

        S: Those who believe in the unseen

        *** END OF THIS PROJECT GUTENBERG EBOOK TRANSLATIONS OF THE KORAN ***

        002.004
        P: Not part of the text.
        """;

    [Fact]
    public void OnlyPickthallIsTakenAndHisLinesAreRejoined()
    {
        var verses = QuranLoader.Pickthall(Gutenberg);

        Assert.Equal("Praise be to Allah, Lord of the Worlds,", verses[1].Text);
        Assert.Equal(
            "This is the Scripture whereof there is no doubt, a guidance unto those who ward off (evil).",
            verses[3].Text);
        Assert.DoesNotContain(verses, v => v.Text.Contains("Cherisher"));
    }

    /// <summary>
    /// The file prints 17.33 as "0.033" and 39.46 as "039.04". A damaged
    /// label is the next verse when it is that verse's label with characters
    /// missing, and the text after the end marker is not read.
    /// </summary>
    [Fact]
    public void DamagedVerseLabelsAreReadAsTheVerseThatComesNext()
    {
        var verses = QuranLoader.Pickthall(Gutenberg);

        Assert.Equal(
            new[] { (1, 1), (1, 2), (2, 1), (2, 2), (2, 3) },
            verses.Select(v => (v.Sura, v.Verse)));
    }

    [Fact]
    public void ALabelOutOfSequenceIsRefusedRatherThanGuessed()
    {
        var skipped = Gutenberg.Replace("001.002", "001.005");

        Assert.Throws<InvalidDataException>(() => QuranLoader.Pickthall(skipped));
    }

    /// <summary>
    /// The same words in the two spellings, and as someone would type them:
    /// the vowel signs, the tatweel, the small waw, alef wasla and the
    /// undotted final yeh all fall away.
    /// </summary>
    [Theory]
    [InlineData("ٱلرَّحْمَـٰنِ", "الرَّحْمَـٰنِ", "الرحمن")]
    [InlineData("فِى", "فِي", "في")]
    [InlineData("وَرَسُولِهِۦٓ", "وَرَسُولِهِ", "ورسوله")]
    [InlineData("بَرَآءَةٌ", "بَرَاءَةٌ", "براءة")]
    [InlineData("إِيَّاكَ", "إِيَّاكَ", "اياك")]
    public void BothSpellingsAndThePlainWordIndexAlike(string uthmani, string simple, string typed)
    {
        var expected = WordNormalizer.Normalize(typed);

        Assert.Equal(expected, WordNormalizer.Normalize(uthmani));
        Assert.Equal(expected, WordNormalizer.Normalize(simple));
    }

    /// <summary>Pause marks stand between words in the Tanzil text and are not words.</summary>
    [Fact]
    public void PauseMarksAreNotIndexed()
    {
        var words = WordNormalizer.TokenizeLine("رَيْبَ ۛ فِيهِ ۛ هُدًى").ToList();

        Assert.Equal(3, words.Count);
    }
}
