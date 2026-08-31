using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ClassicaCodex.Core;

/// <summary>
/// Sends one passage to the Anthropic API for translation - into English
/// from the original pane, or into the work's original language (Ancient
/// Greek or Latin) from the translation pane, whichever TranslateForm asks
/// for. This is the only outbound network call in Classica Codex that isn't
/// disclosed by Setup itself (a git clone of a named repo, or a Perseus IIIF
/// image fetch for Art &amp; Archaeology) - so TranslateForm gives it its own
/// explicit confirmation step rather than relying on someone having read
/// that far back into Setup. Never called without an API key configured
/// and, unless the person has turned it off, confirmation given for this
/// specific use.
/// </summary>
public static class ClaudeTranslationService
{
    // One shared instance for the process's lifetime - the standard .NET
    // guidance, and the same pattern ArtifactBrowserControl already uses for
    // its own live network fetches (Perseus IIIF images).
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    private const string Model = "claude-sonnet-5";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    /// <summary>
    /// Room for the translation to finish in.
    ///
    /// This was 1024, which is roughly 4,000 characters of English and is not
    /// enough. A passage here is whatever the TEI made a passage - usually a
    /// line of verse, but in prose editions a whole chapter. Measured against a
    /// full Perseus library: 1,050 passages run past 4,000 characters, 125 past
    /// 8,000, and Apuleius Metamorphoses 5.2 is 41,475 - about twelve thousand
    /// tokens of English. At the old cap a reader asking for that got the first
    /// twelfth of it, cut mid-sentence, presented beside the original as a
    /// finished translation.
    ///
    /// Sonnet 5 will take far more than this. The number is a backstop against
    /// a runaway response rather than a budget, and is paired with the
    /// stop_reason check below - a cap on its own only moves the point at which
    /// the same silent truncation happens.
    /// </summary>
    private const int MaxOutputTokens = 16000;

    public static async Task<string> TranslateAsync(
        string passageText,
        string? sourceLanguage,
        string? targetLanguage,
        string authorName,
        string workTitle,
        string citationRef,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var sourceLanguageName = TranslationLanguageNames.DisplayName(sourceLanguage);
        var targetLanguageName = TranslationLanguageNames.DisplayName(targetLanguage);

        var prompt =
            $"Translate the following {sourceLanguageName} passage into clear, readable " +
            $"{targetLanguageName} prose. It is from {authorName}, {workTitle}, at {citationRef}. " +
            "Return only the translation itself - no preamble, no notes, no repeating the original.\n\n" +
            passageText;

        var requestBody = JsonSerializer.Serialize(new
        {
            model = Model,
            max_tokens = MaxOutputTokens,
            messages = new[] { new { role = "user", content = prompt } }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await s_httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractErrorMessage(responseText, response.StatusCode));
        }

        return ExtractTranslatedText(responseText);
    }

    /// <summary>
    /// Anthropic's error responses carry a nested {"error":{"message":...}}
    /// body - surfacing that instead of a bare status code is the difference
    /// between "your API key looks wrong" and a generic "401".
    /// </summary>
    private static string ExtractErrorMessage(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var message = doc.RootElement.GetProperty("error").GetProperty("message").GetString();
            if (!string.IsNullOrWhiteSpace(message)) return message;
        }
        catch
        {
            // Not the shape expected - fall through to the generic message.
        }

        return $"Anthropic API returned {(int)statusCode} ({statusCode}).";
    }

    /// <summary>
    /// The translation, or an error if the model ran out of room before
    /// finishing it.
    ///
    /// stop_reason is the only thing that distinguishes a translation that
    /// ended because it was done from one that ended because the token cap
    /// arrived, and the two look identical in the response body. Not reading it
    /// meant a truncated rendering went into the workbench beside the original
    /// as though it were complete - and a translation that stops mid-sentence
    /// is the kind of wrong that gets quoted, because the part that IS there
    /// reads perfectly well.
    ///
    /// Thrown rather than returned with a warning attached: every caller here
    /// puts the returned string in front of a reader as the translation, and a
    /// caveat travelling beside it would have to be carried through all of them
    /// to be worth anything. An error they can act on is the honest answer.
    /// </summary>
    private static string ExtractTranslatedText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var content = doc.RootElement.GetProperty("content");

        var text = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "text"
                && block.TryGetProperty("text", out var textProp))
            {
                text.Append(textProp.GetString());
            }
        }

        var stopReason = doc.RootElement.TryGetProperty("stop_reason", out var stop)
            ? stop.GetString()
            : null;

        if (string.Equals(stopReason, "max_tokens", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The translation was cut off before it finished - this passage is longer than one " +
                "request can render. Translate it in smaller pieces, or use the workbench, which " +
                "works through a text one passage at a time.");
        }

        return text.ToString().Trim();
    }
}
