namespace ClassicaCodex.UI;

/// <summary>
/// Turns an edition's language code into the phrase an AI translation
/// prompt should use. Shared by both ClaudeTranslationService and
/// GeminiTranslationService rather than duplicated in each - the same
/// reasoning as PassageAligner: two copies of a small mapping is exactly how
/// they quietly drift out of sync with each other.
/// </summary>
public static class TranslationLanguageNames
{
    public static string DisplayName(string? languageCode) => languageCode?.ToUpperInvariant() switch
    {
        "GRC" => "Ancient Greek",
        "LAT" => "Latin",
        "ENG" => "English",
        null => "the source language",
        _ => languageCode
    };
}
