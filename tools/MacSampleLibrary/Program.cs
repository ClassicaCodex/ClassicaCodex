using ClassicaCodex.Data;
using ClassicaCodex.Ingestion;

// Builds a small library from sparse clones of Perseus and the lemma and
// dictionary repositories, using the same ingestion code the setup wizard
// runs - so the Mac front end can be developed and tested against real texts
// without a full two-hour setup, and without borrowing the library from a
// Windows machine.
//
//   dotnet run --project tools/MacSampleLibrary -- <library.db>
//       --greek <canonical-greekLit/data> --latin <canonical-latinLit/data>
//       [--lexicon <folder> <grc|lat> <source name>]...
//       [--lemmas <folder> <grc|lat>]...
//
// A sparse checkout of a few authors is plenty for the texts. The lexicon
// folders are PerseusDL/lexica's pdllex/lat/ls and pdllex/grc/lsj; the lemma
// folders are lascivaroma/latin-lemmatized-texts and
// gcelano/LemmatizedAncientGreekXML, sparse-checked-out to the same authors.
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: MacSampleLibrary <library.db> --greek <data> --latin <data> [--lexicon <folder> <lang> <source>]... [--lemmas <folder> <lang>]...");
    return 1;
}

var database = Path.GetFullPath(args[0]);
var corpora = new List<(string DataPath, string Namespace)>();
var lexicons = new List<(string Folder, string Language, string Source)>();
var lemmas = new List<(string Folder, string Language)>();

for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--greek": corpora.Add((args[++i], "greekLit")); break;
        case "--latin": corpora.Add((args[++i], "latinLit")); break;
        case "--lexicon": lexicons.Add((args[++i], args[++i], args[++i])); break;
        case "--lemmas": lemmas.Add((args[++i], args[++i])); break;
        default:
            Console.Error.WriteLine($"Unknown option {args[i]}");
            return 1;
    }
}

if (File.Exists(database)) File.Delete(database);

DbConnectionFactory.Configure(database, remember: false);
await SchemaInitializer.EnsureSchemaAsync();

await new PerseusIngestService().IngestAsync(
    corpora,
    new Progress<IngestProgress>(p => Console.WriteLine($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})")));

foreach (var (folder, language, source) in lexicons)
{
    await new LexiconIngestService().IngestAsync(folder, language, source,
        new Progress<LexiconIngestProgress>(p => Console.WriteLine($"Lexicon: {p.CurrentFile} ({p.EntriesLoaded:N0} entries)")));
}

foreach (var (folder, language) in lemmas)
{
    await new LemmaIngestService().IngestAsync(folder, language,
        new Progress<LemmaIngestProgress>(p => Console.WriteLine($"Lemmas: {p.CurrentFile} ({p.LemmasLoaded:N0} mappings)")));
}

await new WordIndexService().BuildAsync(
    new Progress<WordIndexProgress>(p => Console.WriteLine($"Index: {p.Phase} {p.NodesProcessed}/{p.TotalNodes}")));
await SchemaInitializer.UpdateQueryStatisticsAsync();

Console.WriteLine($"Library written to {database}");
return 0;
