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
            max_tokens = 1024,
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

        return text.ToString().Trim();
    }
}
