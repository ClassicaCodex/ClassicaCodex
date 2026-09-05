using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClassicaCodex.Core;

namespace ClassicaCodex.Data;

/// <summary>One atomic checkpoint and collection per library; never writes to the corpus.</summary>
public sealed class BronzeSaveStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string FilePath { get; }
    public bool RecoveredBackup { get; private set; }
    public BronzeSaveStore(string? library, string? directory = null)
    {
        var identity = library == null ? "PRACTICE" : Path.GetFullPath(library).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassicaCodex", "BronzeAndThunder");
        FilePath = Path.Combine(directory, key + ".json");
    }

    public BronzeChronicle Load()
    {
        if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak")) return new BronzeChronicle();
        try { return Read(FilePath); }
        catch (NotSupportedException) { throw; } // A future version must never be downgraded.
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            try { var backup = Read(FilePath + ".bak"); RecoveredBackup = true; return backup; }
            catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException)
            { throw new InvalidDataException("The adventure save could not be read. The existing files have been preserved.", ex); }
        }
    }

    private static BronzeChronicle Read(string path)
    {
        var value = JsonSerializer.Deserialize<BronzeChronicle>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Empty adventure save.");
        if (value.Version != 1) throw new NotSupportedException("This adventure save needs a different version of the game.");
        if (value.Bestiary == null || value.Trophies == null
            || value.Bestiary.Any(e => e == null || !Enum.IsDefined(e.Kind) || e.Defeats < 0 || e.Verses == null
                || e.Verses.Any(v => !ValidVerse(v)))
            || value.Trophies.Any(t => t == null || t.RunId == Guid.Empty || t.ArcKey == null || t.ArcTitle == null
                || t.Epithet == null || t.Premise == null || t.Payoff == null || t.Score < 0 || t.Verses == null
                || t.Verses.Any(v => !ValidVerse(v)) || t.Gifts == null || t.Gifts.Any(g => !Enum.IsDefined(g))))
            throw new InvalidDataException("Invalid chronicle.");
        return value;
    }

    private static bool ValidVerse(BronzeRecoveredVerse? verse) => verse != null && verse.ArcTitle != null && verse.Meaning != null
        && verse.Passage is { Author: not null, Title: not null, Citation: not null, Text: not null, Language: not null };

    public void Save(BronzeChronicle value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(value, Options);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(data); stream.Flush(true); }
            if (File.Exists(FilePath) && !RecoveredBackup) File.Copy(FilePath, FilePath + ".bak", true);
            File.Move(temp, FilePath, true);
            RecoveredBackup = false;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
