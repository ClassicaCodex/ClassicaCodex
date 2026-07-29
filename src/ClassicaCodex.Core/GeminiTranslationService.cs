using ClassicaCodex.Core;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ClassicaCodex.UI;

/// <summary>
/// Sends one passage to Google's Gemini API for translation - into English
/// from the original pane, or into the work's original language (Ancient
/// Greek or Latin) from the translation pane, whichever TranslateForm asks
/// for. The free alternative to ClaudeTranslationService. Same disclosure
/// obligations apply: only called after an explicit confirmation, same as
/// the Claude path.
///
/// Worth being honest with yourself about before reaching for this over
/// Claude: Gemini is a genuine LLM, not a classical statistical translator,
/// so unlike Google Translate proper it should actually attempt Ancient
/// Greek and Latin rather than fumbling them - but it's still the free-tier
/// model (Flash, not Pro), and the free tier's own terms allow Google to use
/// what you send it to improve their models. Free and private aren't the
/// same thing; TranslateApiSettingsForm says so before anyone sends
/// anything.
///
/// Three Google-specific failure modes get handled here, deliberately
/// differently, because they aren't the same kind of problem:
///
///  - A model ID gets retired (Google does this far more often than
///    Anthropic - three names in under a year for roughly the same tier).
///    Permanent, so retrying the same model is pointless; falls straight
///    through to FallbackModel once.
///  - A model is "experiencing high demand" (a 503, especially common on
///    models that are new or still ramping up capacity). Transient, so
///    it's worth a couple of short retries before giving up on that model.
///  - The free tier's usage limit is hit (a 429). Quotas are tracked
///    separately per model, so the fallback may still have room even
///    though the one just tried doesn't - worth trying immediately, with
///    no retry delay, since waiting a few seconds on the very same
///    exhausted bucket accomplishes nothing (this is also the failure mode
///    behind an actively-reported Google-side bug where gemini-3.5-flash's
///    free-tier quota check has returned false positives for some keys, so
///    a "limit reached" message here isn't necessarily proof real usage
///    was that high).
/// </summary>
public static class GeminiTranslationService
{
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    private const string PrimaryModel = "gemini-3.5-flash";
    private const string FallbackModel = "gemini-3.1-flash-lite";
    private const string ApiUrlTemplate = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    // Short waits, not a long production-grade backoff schedule - this is a
    // synchronous dialog the person is watching, so the whole retry budget
    // needs to resolve in a few seconds, not minutes. Only used for the
    // "overloaded" case - a quota limit won't clear on a short wait, so
    // that one skips straight to the fallback model instead.
    private static readonly TimeSpan[] OverloadRetryDelays =
        { TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3) };

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

        try
        {
            return await TranslateWithModelAsync(PrimaryModel, allowFallback: true, prompt, apiKey, cancellationToken);
        }
        catch (QuotaExceededException)
        {
            // Only reaches here if the fallback model was already tried too
            // (see the catch below) and it hit its own separate limit as
            // well - a clearer message than either model's raw wording.
            throw new InvalidOperationException(
                "Both Gemini models have hit today's free-tier usage limit. Try again after the daily " +
                "reset, or use Claude for now.");
        }
    }

    /// <summary>
    /// Reads a whole comparison work (pre-formatted, citation-tagged text -
    /// see CrossLanguageEchoForm for how that's built and truncated) looking
    /// for passages that echo the same theme or image as a source passage in
    /// a different language, rather than the same literal words - the thing
    /// Find Echoes' rare-word matching structurally can't do across
    /// languages. Deliberately Gemini-only: this is a new, more speculative
    /// tool than Translate, and there's no reason a free feature needs a
    /// paid fallback bolted on.
    ///
    /// Returns raw candidates exactly as the model reported them - it's
    /// CrossLanguageEchoForm's job to verify each CitationRef against real
    /// TextNodes before showing anything as a genuine result, the same
    /// "don't just trust what the AI said" principle Translate's own
    /// ingested-passage lookup already follows.
    /// </summary>
    public static async Task<List<EchoCandidate>> FindEchoesAsync(
        string sourcePassageText,
        string? sourceLanguage,
        string sourceAuthorName,
        string sourceWorkTitle,
        string sourceCitationRef,
        string comparisonAuthorName,
        string comparisonWorkTitle,
        string? comparisonLanguage,
        string taggedComparisonText,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var sourceLanguageName = TranslationLanguageNames.DisplayName(sourceLanguage);
        var comparisonLanguageName = TranslationLanguageNames.DisplayName(comparisonLanguage);

        var prompt =
            "You are looking for thematic or imagistic echoes - a shared idea, image, or motif, not shared " +
            "exact wording - between one passage and a whole other work, in a different language.\n\n" +
            $"SOURCE PASSAGE ({sourceLanguageName}), from {sourceAuthorName}, {sourceWorkTitle}, at " +
            $"{sourceCitationRef}:\n{sourcePassageText}\n\n" +
            $"COMPARISON WORK ({comparisonLanguageName}), {comparisonAuthorName}, {comparisonWorkTitle}. Each " +
            $"line is tagged with its citation reference in square brackets:\n\n{taggedComparisonText}\n\n" +
            "Identify passages in the comparison work that genuinely seem to echo the source passage's central " +
            "image, theme, or idea - not passages that merely share common words. For each real candidate, " +
            "report its citation reference exactly as tagged above, a confidence level, and a one-sentence " +
            "rationale naming the shared image or idea. If there are no genuine echoes, return an empty list - " +
            "don't strain to find something. Never invent a citation reference that isn't tagged above.\n\n" +
            "Respond with ONLY a JSON array, no other text and no markdown code fences, in this exact shape: " +
            "[{\"citationRef\": \"...\", \"confidence\": \"high|medium|low\", \"rationale\": \"...\"}]";

        string rawResponse;
        try
        {
            rawResponse = await TranslateWithModelAsync(PrimaryModel, allowFallback: true, prompt, apiKey, cancellationToken);
        }
        catch (QuotaExceededException)
        {
            throw new InvalidOperationException(
                "Both Gemini models have hit today's free-tier usage limit. Try again after the daily reset.");
        }

        return ParseEchoCandidates(rawResponse);
    }

    /// <summary>
    /// Defensive JSON parsing - a model asked for "only JSON" still
    /// sometimes wraps the answer in a markdown code fence anyway, so that
    /// gets stripped before parsing rather than letting it fail outright.
    /// A candidate missing its citation ref is dropped silently here (it's
    /// unusable, not a hallucination in the sense CrossLanguageEchoForm
    /// checks for); a response that isn't valid JSON at all surfaces as a
    /// clear error instead of a confusing empty result list.
    /// </summary>
    private static List<EchoCandidate> ParseEchoCandidates(string rawResponse)
    {
        var cleaned = rawResponse.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline >= 0) cleaned = cleaned[(firstNewline + 1)..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var results = new List<EchoCandidate>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var citationRef = item.TryGetProperty("citationRef", out var refProp) ? refProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(citationRef)) continue;

                var confidence = item.TryGetProperty("confidence", out var confProp)
                    ? confProp.GetString() ?? "unspecified" : "unspecified";
                var rationale = item.TryGetProperty("rationale", out var ratProp)
                    ? ratProp.GetString() ?? string.Empty : string.Empty;

                results.Add(new EchoCandidate(citationRef, confidence, rationale));
            }

            return results;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Gemini's response wasn't in the expected format, so no candidates could be read from it. ({ex.Message})");
        }
    }

    private static async Task<string> TranslateWithModelAsync(
        string model, bool allowFallback, string prompt, string apiKey, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await SendAsync(model, prompt, apiKey, cancellationToken);
            }
            catch (ModelUnavailableException) when (allowFallback)
            {
                // Retired model ID - permanent, so no point retrying this
                // one. The fallback gets its own fresh retry budget, but
                // allowFallback:false caps this at one hop, not a chain.
                return await TranslateWithModelAsync(FallbackModel, allowFallback: false, prompt, apiKey, cancellationToken);
            }
            catch (QuotaExceededException) when (allowFallback)
            {
                // Quotas are per-model - no reason to wait on this one's
                // exhausted bucket when the other has its own separate one.
                return await TranslateWithModelAsync(FallbackModel, allowFallback: false, prompt, apiKey, cancellationToken);
            }
            catch (ModelOverloadedException) when (attempt < OverloadRetryDelays.Length)
            {
                await Task.Delay(OverloadRetryDelays[attempt], cancellationToken);
            }
            catch (ModelOverloadedException) when (allowFallback)
            {
                // Retries on this model exhausted - try the other one, since
                // an overloaded Flash doesn't necessarily mean Flash-Lite is
                // also overloaded; different models, different capacity.
                return await TranslateWithModelAsync(FallbackModel, allowFallback: false, prompt, apiKey, cancellationToken);
            }
            // Anything else (bad key, malformed request, or any of the above
            // with no retries/fallback left) isn't caught here and
            // propagates as-is - QuotaExceededException specifically is
            // caught one level up, in TranslateAsync, once it's clear both
            // models were tried.
        }
    }

    private static async Task<string> SendAsync(
        string model, string prompt, string apiKey, CancellationToken cancellationToken)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        });

        var url = string.Format(ApiUrlTemplate, model);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await s_httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = ExtractErrorMessage(responseText, response.StatusCode);

            // Status code first where Google's own signal is unambiguous -
            // 429 always means a rate/quota limit regardless of wording.
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new QuotaExceededException(message);

            if (message.Contains("no longer available", StringComparison.OrdinalIgnoreCase))
                throw new ModelUnavailableException(message);

            if (message.Contains("high demand", StringComparison.OrdinalIgnoreCase)
                || message.Contains("overloaded", StringComparison.OrdinalIgnoreCase))
                throw new ModelOverloadedException(message);

            throw new InvalidOperationException(message);
        }

        return ExtractTranslatedText(responseText);
    }

    /// <summary>Marks specifically the "this model ID has been retired" failure, to drive the one-shot fallback above.</summary>
    private class ModelUnavailableException(string message) : Exception(message);

    /// <summary>Marks specifically the transient "high demand" / "overloaded" 503, to drive the retry-then-fallback above.</summary>
    private class ModelOverloadedException(string message) : Exception(message);

    /// <summary>Marks specifically a 429 rate/quota limit, to drive the immediate (no-delay) fallback above.</summary>
    private class QuotaExceededException(string message) : Exception(message);

    /// <summary>
    /// Google's error responses carry a nested {"error":{"message":...}}
    /// body, same general shape as Anthropic's - surfacing it beats a bare
    /// status code for telling "bad key" apart from "rate limited".
    /// </summary>
    private static string ExtractErrorMessage(string responseBody, HttpStatusCode statusCode)
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

        return $"Gemini API returned {(int)statusCode} ({statusCode}).";
    }

    private static string ExtractTranslatedText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);

        // candidates[0].content.parts[*].text, joined - a response can carry
        // more than one part, though a plain text prompt like this one
        // almost always returns exactly one.
        var candidates = doc.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "Gemini returned no candidates - the passage may have tripped a safety filter.");
        }

        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        var text = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textProp))
            {
                text.Append(textProp.GetString());
            }
        }

        return text.ToString().Trim();
    }
}
