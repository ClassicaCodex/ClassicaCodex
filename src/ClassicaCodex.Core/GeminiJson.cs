using System.Text.Json;

namespace ClassicaCodex.Core;

/// <summary>
/// Reading the JSON a model was asked for, as models actually return it.
///
/// Every research prompt ends "return ONLY JSON", and every parser used to read the
/// answer as if that instruction were a guarantee: a fence was stripped only when the
/// reply started with one, and each field was read with GetString, which throws on
/// anything that is not a string. The prompts name several fields in the plural -
/// verificationTasks, sharedFeatures, suggestedMotifs - and a model answering one of
/// them with a list, as models often do, threw away every candidate in the reply
/// rather than the one field. These readers keep the rest: a list becomes its items
/// joined, a number its digits, and anything else an empty field the caller already
/// knows how to skip.
/// </summary>
internal static class GeminiJson
{
    /// <summary>
    /// Parses the reply, looking past a code fence or a sentence of preamble if the
    /// whole of it is not JSON. Throws the original <see cref="JsonException"/> when
    /// no JSON can be found, so a reply that is not JSON at all still says so.
    /// </summary>
    public static JsonDocument Parse(string rawResponse)
    {
        JsonException? first = null;
        foreach (var candidate in Candidates(rawResponse.Trim()))
        {
            try { return JsonDocument.Parse(candidate); }
            catch (JsonException ex) { first ??= ex; }
        }
        throw first!;
    }

    // The reply as it stands first, so a fence quoted inside a JSON string is never
    // mistaken for one around it; then without the fence; then from the first bracket
    // to its last partner, for a sentence before or after.
    private static IEnumerable<string> Candidates(string text)
    {
        yield return text;
        var unfenced = StripFence(text);
        if (unfenced != text) yield return unfenced;
        var start = unfenced.IndexOfAny(['[', '{']);
        var end = start < 0 ? -1 : unfenced.LastIndexOf(unfenced[start] == '[' ? ']' : '}');
        if (end > start && (start > 0 || end < unfenced.Length - 1)) yield return unfenced[start..(end + 1)];
    }

    /// <summary>
    /// The array of items. A reply wrapped in an object - {"candidates": [...]} - is
    /// read through to the first array it holds.
    /// </summary>
    public static IEnumerable<JsonElement> Items(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    root = property.Value;
                    break;
                }
            }
        }
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The reply was not a list of items.");
        return root.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object);
    }

    /// <summary>A field as text, whatever JSON kind it arrived as; empty when absent.</summary>
    public static string Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) ? Text(value) : string.Empty;

    /// <summary>A field as a list of strings; a lone string is a list of one.</summary>
    public static List<string> List(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array)
            return Text(value) is { Length: > 0 } single ? [single] : [];
        return value.EnumerateArray().Select(Text).Where(s => s.Length > 0).ToList();
    }

    /// <summary>A whole number, written as a number or as a string of digits.</summary>
    public static int? Int(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString()?.Trim(),
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed : null;
    }

    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(Text).Where(s => s.Length > 0)),
        _ => string.Empty
    };

    private static string StripFence(string text)
    {
        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence < 0) return text;
        var bodyStart = text.IndexOf('\n', fence);
        if (bodyStart < 0) return text;
        var close = text.IndexOf("```", bodyStart, StringComparison.Ordinal);
        return (close < 0 ? text[(bodyStart + 1)..] : text[(bodyStart + 1)..close]).Trim();
    }
}
