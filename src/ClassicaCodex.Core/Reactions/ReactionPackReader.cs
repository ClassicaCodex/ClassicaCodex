using System.Text.Json;

namespace ClassicaCodex.Core.Reactions;

/// <summary>
/// Turns an authored JSON pack into the records above.
///
/// Hand-read through JsonDocument rather than deserialised onto attributed
/// types, for one reason: what this has to produce when a pack is wrong is a
/// sentence a person can act on. System.Text.Json's own message for a missing
/// property is that a property is missing, with no idea which turn of which
/// debate, and these files are authored by hand - a mistyped criticId in turn
/// seven is the normal failure, not a rare one.
///
/// Everything it cannot read becomes a problem in the returned list rather
/// than an exception. A malformed pack must never be able to stop the
/// application opening: the packs ship inside the executable, but a reader may
/// also drop their own in a folder beside it, and a stray comma in someone
/// else's file is not this application's crash to have.
/// </summary>
public static class ReactionPackReader
{
    /// <summary>What a pack is allowed to be called in a problem message.</summary>
    public sealed record Result(ReactionPack? Pack, IReadOnlyList<string> Problems)
    {
        public bool Ok => Pack != null && Problems.Count == 0;
    }

    public static Result Read(string json, string packName)
    {
        var problems = new List<string>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException ex)
        {
            // The line and position are in the message already, and they are
            // the only part of a parse failure worth reading.
            return new Result(null, new[] { $"{packName}: not valid JSON - {ex.Message}" });
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return new Result(null, new[] { $"{packName}: expected an object at the top level." });

            var critics = ReadCritics(root, packName, problems);
            var debate = ReadDebate(root, packName, problems);

            if (debate == null) return new Result(null, problems);

            return new Result(new ReactionPack(packName, critics, debate), problems);
        }
    }

    private static List<AncientCritic> ReadCritics(
        JsonElement root, string packName, List<string> problems)
    {
        var critics = new List<AncientCritic>();

        if (!root.TryGetProperty("critics", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{packName}: no \"critics\" array.");
            return critics;
        }

        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            index++;
            var id = String(element, "id");

            if (string.IsNullOrWhiteSpace(id))
            {
                problems.Add($"{packName}: critic {index} has no id.");
                continue;
            }

            var kindText = String(element, "kind") ?? "Composite";
            if (!Enum.TryParse<CriticKind>(kindText, ignoreCase: true, out var kind))
            {
                problems.Add($"{packName}: critic \"{id}\" has kind \"{kindText}\"; "
                             + "expected Composite or Historical.");
                continue;
            }

            critics.Add(new AncientCritic(
                id,
                String(element, "name") ?? string.Empty,
                kind,
                String(element, "era") ?? string.Empty,
                Int(element, "floruitStart") ?? 0,
                Int(element, "floruitEnd") ?? 0,
                String(element, "place"),
                String(element, "role"),
                String(element, "sketch"),
                String(element, "tastes"),
                String(element, "avatar")));
        }

        return critics;
    }

    private static ReactionDebate? ReadDebate(
        JsonElement root, string packName, List<string> problems)
    {
        if (!root.TryGetProperty("debate", out var debate) || debate.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{packName}: no \"debate\" object.");
            return null;
        }

        var id = String(debate, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            problems.Add($"{packName}: the debate has no id.");
            return null;
        }

        var work = ReadWorkReference(debate, "work");
        if (work == null)
        {
            problems.Add($"{packName}: debate \"{id}\" has no \"work\" with an authorKey.");
            return null;
        }

        var kindText = String(debate, "kind") ?? nameof(DebateKind.Scene);
        if (!Enum.TryParse<DebateKind>(kindText, ignoreCase: true, out var kind))
        {
            problems.Add($"{packName}: debate \"{id}\" has kind \"{kindText}\"; "
                         + "expected Scene or AcrossTheCenturies.");
            return null;
        }

        var turns = ReadTurns(root, packName, id, problems);

        return new ReactionDebate(
            id,
            work,
            String(debate, "title") ?? string.Empty,
            kind,
            Int(debate, "compositionYear") ?? 0,
            Int(debate, "settingYear"),
            String(debate, "settingPlace"),
            String(debate, "settingNote"),
            turns);
    }

    private static List<DebateTurn> ReadTurns(
        JsonElement root, string packName, string debateId, List<string> problems)
    {
        var turns = new List<DebateTurn>();

        if (!root.TryGetProperty("turns", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{packName}: debate \"{debateId}\" has no \"turns\" array.");
            return turns;
        }

        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            index++;
            var seq = Int(element, "seq");

            if (seq == null)
            {
                problems.Add($"{packName}: turn {index} has no seq.");
                continue;
            }

            var criticId = String(element, "criticId");
            if (string.IsNullOrWhiteSpace(criticId))
            {
                problems.Add($"{packName}: turn {seq} has no criticId.");
                continue;
            }

            turns.Add(new DebateTurn(
                seq.Value,
                criticId,
                String(element, "text") ?? string.Empty,
                Int(element, "year"),
                String(element, "passageRef"),
                String(element, "sourceCitation"),
                ReadWorkReference(element, "sourceWork")));
        }

        return turns;
    }

    private static WorkReference? ReadWorkReference(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Object) return null;

        var authorKey = String(element, "authorKey");
        if (string.IsNullOrWhiteSpace(authorKey)) return null;

        var titleKeys = new List<string>();
        if (element.TryGetProperty("titleKeys", out var keys) && keys.ValueKind == JsonValueKind.Array)
        {
            foreach (var key in keys.EnumerateArray())
                if (key.ValueKind == JsonValueKind.String) titleKeys.Add(key.GetString() ?? string.Empty);
        }

        return new WorkReference(authorKey, titleKeys, String(element, "citationRef"));
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
