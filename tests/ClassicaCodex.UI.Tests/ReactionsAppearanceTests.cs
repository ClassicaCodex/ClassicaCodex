using ClassicaCodex.Core.Reactions;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// The parts of Fictional Ancient Reactions that are a picture rather than a
/// sentence, checked as numbers.
///
/// Drawing cannot be unit tested and is not tried here. What CAN be tested is
/// everything the drawing depends on being true: that six people talking in
/// one window look like six different people, that the colours the bubbles are
/// tinted with stay readable in both themes, and that a portrait comes out the
/// size it was asked for.
///
/// Every one of these guards something that was actually wrong at some point
/// while this was being built. The first version of the portraits drew every
/// critic as a bald man in ear-muffs with no beard, and no test could have
/// caught that - it took looking. These catch the ones that can be counted.
/// </summary>
public class ReactionsAppearanceTests
{
    /// <summary>
    /// Nobody in a debate may look like anybody else in it.
    ///
    /// The portrait is how a reader follows who is speaking, especially after
    /// the second turn by the same person, where the name is deliberately
    /// dropped. Two identical recipes in one conversation would make two
    /// speakers indistinguishable - and the content is hand written, so this
    /// is a copy-paste away at any time.
    /// </summary>
    [Fact]
    public void NoTwoSpeakersInOneDebateLookAlike()
    {
        var clashes = new List<string>();

        foreach (var pack in ReactionLibrary.Packs)
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in pack.Debate.SpeakerIds)
            {
                var critic = ReactionLibrary.Critic(id);
                if (critic?.Avatar == null) continue;

                if (seen.TryGetValue(critic.Avatar, out var other))
                    clashes.Add($"{pack.Name}: {other} and {critic.Id} have the same portrait");
                else
                    seen[critic.Avatar] = critic.Id;
            }
        }

        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }

    /// <summary>
    /// And no two speakers in one debate may carry the same bubble colour,
    /// which is a weaker condition than the one above - two critics can differ
    /// only in hair and still tint their bubbles identically.
    /// </summary>
    [Fact]
    public void NoTwoSpeakersInOneDebateShareABubbleColour()
    {
        var clashes = new List<string>();

        foreach (var pack in ReactionLibrary.Packs)
        {
            var seen = new Dictionary<Color, string>();

            foreach (var id in pack.Debate.SpeakerIds)
            {
                var critic = ReactionLibrary.Critic(id);
                if (critic == null) continue;

                var colour = AncientAvatars.Signature(critic.Avatar);

                if (seen.TryGetValue(colour, out var other))
                    clashes.Add($"{pack.Name}: {other} and {critic.Id} tint the same colour");
                else
                    seen[colour] = critic.Id;
            }
        }

        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }

    /// <summary>
    /// Every bubble has to stay readable in both themes.
    ///
    /// The tint is the speaker's own colour mixed toward the window's surface,
    /// so one set of recipes serves light and dark - which is only safe if the
    /// result actually contrasts with the text drawn on it. Dark mode cannot
    /// be photographed from a test (switching it writes to the reader's own
    /// theme file), so the two mixes are computed here against the two
    /// surfaces and the contrast is measured.
    ///
    /// 4.5:1 is the WCAG AA threshold for body text, and this is body text:
    /// several hundred words of it, which is the whole point of the window.
    /// </summary>
    [Fact]
    public void EveryBubbleStaysReadableInBothThemes()
    {
        // The two themes' surface and text colours, as ReadingTheme defines
        // them. Copied rather than read, because reading them means switching
        // theme, and switching theme writes to a file in the reader's own
        // settings folder.
        var lightSurface = Color.FromArgb(250, 247, 240);
        var lightText = Color.Black;
        var darkSurface = Color.FromArgb(24, 24, 26);
        var darkText = Color.FromArgb(232, 228, 218);

        var failures = new List<string>();

        foreach (var critic in ReactionLibrary.Packs.SelectMany(p => p.Critics).DistinctBy(c => c.Id))
        {
            var signature = AncientAvatars.Signature(critic.Avatar);

            // The same two mixes ReactionsCanvas uses.
            var light = AncientAvatars.Mix(signature, lightSurface, 0.86f);
            var dark = AncientAvatars.Mix(signature, darkSurface, 0.74f);

            var lightRatio = Contrast(light, lightText);
            var darkRatio = Contrast(dark, darkText);

            if (lightRatio < 4.5) failures.Add($"{critic.Id}: light mode contrast {lightRatio:F1}:1");
            if (darkRatio < 4.5) failures.Add($"{critic.Id}: dark mode contrast {darkRatio:F1}:1");
        }

        Assert.True(failures.Count == 0,
            "bubble text would be hard to read:\n  " + string.Join("\n  ", failures));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(44)]
    [InlineData(88)]
    [InlineData(192)]
    public void APortraitComesOutTheSizeItWasAskedFor(int size)
    {
        var portrait = AncientAvatars.Portrait("skin:tan;hair:grey;beard:full;hat:laurel", size);

        Assert.Equal(size, portrait.Width);
        Assert.Equal(size, portrait.Height);
    }

    /// <summary>
    /// A recipe with nonsense in it must still produce a face. The packs are
    /// hand written and a reader may supply their own, so a mistyped hairstyle
    /// has to cost a hairstyle rather than throw inside a paint handler, where
    /// the exception would take the window down.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("skin:chartreuse;hair:magenta;beard:luxuriant;hat:sombrero")]
    [InlineData("cloth:not-a-colour;accent:#gggggg")]
    [InlineData("::::;;;;")]
    public void ANonsenseRecipeStillDraws(string? recipe)
    {
        var portrait = AncientAvatars.Portrait(recipe, 44);

        Assert.Equal(44, portrait.Width);
    }

    /// <summary>Relative luminance contrast, as WCAG defines it.</summary>
    private static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color colour) =>
        0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);

    private static double Channel(int value)
    {
        var v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
