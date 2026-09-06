using System.Reflection;
using ClassicaCodex.Data;
using ClassicaCodex.UI;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Tools.FreshInstallCheck;

/// <summary>
/// Puts the application in the state a person is in on the day they install
/// it: a database that does not exist yet, and then nothing in it.
///
/// <b>Why this exists.</b> Everything else is run against a real library, and
/// a real library hides a whole class of problem, because no query returns
/// nothing and no screen ever has to say "there is nothing here yet". The
/// main window shipped for months showing a new person three blank panels and
/// no explanation - the empty-library branch that would have said so was the
/// one case its own author had not written, and no test could see it because
/// every test had data.
///
/// <b>What it does not do.</b> It does not click through the setup wizard or
/// download anything. It creates the database the way a first run creates it,
/// runs every read query against the empty result, opens every window, and
/// photographs the two screens a new person actually sees first. Look at
/// those pictures: this tool reports what threw, and a window that comes up
/// blank and unexplained throws nothing at all.
///
/// <b>Safety.</b> The database goes in this tool's own output folder, and
/// DbConnectionFactory.Configure is called with remember:false so the real
/// installation's saved path is never written. Both real files are
/// fingerprinted before and after, and it says so if either moved.
///
/// <code>
/// dotnet run --project tools/FreshInstallCheck -c Release
/// </code>
/// </summary>
internal static class Program
{
    private static readonly List<string> Report = new();
    private static string _outputDirectory = ".";

    private static void W(string line)
    {
        Console.WriteLine(line);
        Report.Add(line);
    }

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        _outputDirectory = Path.Combine(AppContext.BaseDirectory, "fresh-install");
        Directory.CreateDirectory(_outputDirectory);

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassicaCodex");
        var realPreference = Path.Combine(appData, "database-path.txt");
        var realLibrary = Path.Combine(appData, "classicacodex.db");

        var preferenceBefore = Fingerprint(realPreference);
        var libraryBefore = Fingerprint(realLibrary);

        var freshDatabase = Path.Combine(_outputDirectory, "fresh-library", "classicacodex.db");
        var freshDirectory = Path.GetDirectoryName(freshDatabase)!;
        if (Directory.Exists(freshDirectory)) Directory.Delete(freshDirectory, true);
        Directory.CreateDirectory(freshDirectory);

        W("a database that does not exist yet");
        W(new string('-', 78));

        // remember:false is the whole reason running this is safe.
        DbConnectionFactory.Configure(freshDatabase, remember: false);

        var started = DateTime.UtcNow;
        SchemaInitializer.EnsureSchemaAsync().GetAwaiter().GetResult();
        W($"created in {(int)(DateTime.UtcNow - started).TotalMilliseconds}ms, "
          + $"{new FileInfo(freshDatabase).Length} bytes");

        DescribeSchema(freshDatabase);
        CompareWith(realLibrary, freshDatabase);
        RunEveryReadQuery();
        OpenEveryWindow();
        PhotographFirstRun();

        W(string.Empty);
        W("the real installation");
        W(new string('-', 78));
        var preferenceSame = Fingerprint(realPreference) == preferenceBefore;
        var librarySame = Fingerprint(realLibrary) == libraryBefore;
        W($"  saved database path : {(preferenceSame ? "unchanged" : "*** CHANGED ***")}");
        W($"  library file        : {(librarySame ? "unchanged" : "*** CHANGED ***")}");

        var reportPath = Path.Combine(_outputDirectory, "report.txt");
        File.WriteAllLines(reportPath, Report);
        Console.WriteLine();
        Console.WriteLine(reportPath);
    }

    private static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return "absent";
        var info = new FileInfo(path);
        return $"{info.Length}/{info.LastWriteTimeUtc:O}";
    }

    private static void DescribeSchema(string database)
    {
        using var connection = new SqliteConnection("Data Source=" + database);
        connection.Open();

        using var counts = connection.CreateCommand();
        counts.CommandText =
            "SELECT type, COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' GROUP BY type";
        using (var reader = counts.ExecuteReader())
            while (reader.Read())
            {
                var kind = reader.GetString(0);
                var plural = kind == "index" ? "indexes" : kind + "s";
                W($"  {plural}: {reader.GetInt32(1)}");
            }

        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        W($"  user_version: {version.ExecuteScalar()}");

        using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check";
        W($"  integrity_check: {integrity.ExecuteScalar()}");
    }

    /// <summary>
    /// The check that matters most: a person installing today must end up
    /// with the same database as someone who has been upgrading for months.
    /// Skipped when there is no real library to compare against, which is the
    /// case on a machine that has genuinely never run this.
    /// </summary>
    private static void CompareWith(string realLibrary, string freshDatabase)
    {
        W(string.Empty);
        W("against an existing library");
        W(new string('-', 78));

        if (!File.Exists(realLibrary))
        {
            W("  no existing library on this machine - nothing to compare");
            return;
        }

        var fresh = TableNames("Data Source=" + freshDatabase);
        var real = TableNames("Data Source=" + realLibrary + ";Mode=ReadOnly");
        W($"  fresh: {fresh.Count} tables, existing: {real.Count} tables");

        var missing = real.Except(fresh).ToList();
        var extra = fresh.Except(real).ToList();

        if (missing.Count == 0 && extra.Count == 0) W("  identical table sets");
        if (missing.Count > 0) W("  MISSING from a fresh database: " + string.Join(", ", missing));
        if (extra.Count > 0) W("  only in a fresh database: " + string.Join(", ", extra));
    }

    private static List<string> TableNames(string connectionString)
    {
        var names = new List<string>();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    private static readonly string[] ReadVerbs =
        { "Get", "Search", "Find", "List", "Count", "Load", "Has", "Is", "Fetch", "Read", "Suggest", "Summarize", "Describe", "Try" };

    private static readonly string[] WriteVerbs =
        { "Add", "Insert", "Update", "Delete", "Set", "Save", "Remove", "Clear", "Record", "Mark", "Toggle",
          "Ensure", "Migrate", "Create", "Backfill", "Compact", "Prune", "Rename", "Import", "Export", "Reset",
          "Apply", "Merge", "Store", "Write", "Put", "Move", "Rebuild", "Reindex", "Checkpoint", "Vacuum",
          "Archive", "Restore", "Register", "Increment", "Advance", "Grant", "Retire" };

    /// <summary>
    /// Every read query against a result set of nothing. Only read verbs are
    /// invoked, and any name carrying a write verb is excluded even if it
    /// starts with one.
    /// </summary>
    private static void RunEveryReadQuery()
    {
        W(string.Empty);
        W("every read query, against an empty library");
        W(new string('-', 78));

        var repositories = typeof(DbConnectionFactory).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                        && t.Namespace == "ClassicaCodex.Data.Repositories"
                        && t.Name.EndsWith("Repository", StringComparison.Ordinal))
            .OrderBy(t => t.Name);

        int ran = 0, threw = 0;

        foreach (var type in repositories)
        {
            var constructor = type.GetConstructor(Type.EmptyTypes);
            if (constructor == null) continue;
            var repository = constructor.Invoke(null);

            foreach (var method in type
                         .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && !m.IsGenericMethodDefinition)
                         .OrderBy(m => m.Name))
            {
                if (!ReadVerbs.Any(v => method.Name.StartsWith(v, StringComparison.Ordinal))) continue;
                if (WriteVerbs.Any(w => method.Name.Contains(w, StringComparison.Ordinal))) continue;

                var parameters = method.GetParameters();
                var arguments = new object?[parameters.Length];
                var usable = true;

                for (var i = 0; i < parameters.Length; i++)
                {
                    arguments[i] = Synthesize(parameters[i], out var ok);
                    if (!ok) { usable = false; break; }
                }
                if (!usable) continue;

                ran++;
                try
                {
                    if (method.Invoke(repository, arguments) is Task task)
                    {
                        task.Wait(TimeSpan.FromSeconds(60));
                        var result = task.GetType().GetProperty("Result");
                        if (result != null && result.PropertyType != typeof(void)) _ = result.GetValue(task);
                    }
                }
                catch (Exception ex)
                {
                    threw++;
                    var baseException = ex.GetBaseException();
                    W($"  THREW  {type.Name}.{method.Name}  "
                      + $"{baseException.GetType().Name}: {baseException.Message}");
                }
            }
        }

        W($"  {ran} queries, {threw} threw");
    }

    /// <summary>
    /// A plausible argument for a parameter. Nothing here has to match a row,
    /// because the point is the empty result - what matters is only that the
    /// call can be made at all.
    /// </summary>
    private static object? Synthesize(ParameterInfo parameter, out bool ok)
    {
        ok = true;
        var declared = parameter.ParameterType;
        var core = Nullable.GetUnderlyingType(declared) ?? declared;
        var name = (parameter.Name ?? string.Empty).ToLowerInvariant();

        if (declared == typeof(CancellationToken)) return CancellationToken.None;
        if (core == typeof(string)) return "Athena";
        if (core == typeof(int)) return name.Contains("limit") || name.Contains("max") ? 5 : 1;
        if (core == typeof(long)) return 1L;
        if (core == typeof(bool)) return false;
        if (core == typeof(double)) return 0.5d;
        if (core == typeof(DateTime)) return DateTime.UtcNow;
        if (core.IsEnum) return Enum.GetValues(core).GetValue(0);

        if (core.IsGenericType)
        {
            var definition = core.GetGenericTypeDefinition();
            if (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>)
                || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IList<>)
                || definition == typeof(ICollection<>) || definition == typeof(List<>))
            {
                var element = core.GetGenericArguments()[0];
                var list = (System.Collections.IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(element))!;

                if (element == typeof(string)) list.Add("Athena");
                else if (element == typeof(long)) list.Add(1L);
                else if (element == typeof(int)) list.Add(1);
                return list;
            }
        }

        if (parameter.HasDefaultValue) return parameter.DefaultValue;

        try
        {
            if (core.IsValueType) return Activator.CreateInstance(core);
            var constructor = core.GetConstructor(Type.EmptyTypes);
            if (constructor != null) return constructor.Invoke(null);
        }
        catch
        {
            // Falls through to reporting this one as unusable.
        }

        ok = false;
        return null;
    }

    private static void OpenEveryWindow()
    {
        W(string.Empty);
        W("every window, against an empty library");
        W(new string('-', 78));

        var forms = typeof(ListResultHelpers).Assembly.GetTypes()
            .Where(t => typeof(Form).IsAssignableFrom(t) && !t.IsAbstract
                        && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.Name);

        int opened = 0, failed = 0;

        foreach (var type in forms)
        {
            try
            {
                using var form = (Form)Activator.CreateInstance(type)!;
                form.ShowInTaskbar = false;
                form.Show();
                Settle(15);
                form.Close();
                Application.DoEvents();
                opened++;
            }
            catch (Exception ex)
            {
                failed++;
                var baseException = ex.GetBaseException();
                W($"  FAILED {type.Name}: {baseException.GetType().Name}: {baseException.Message}");
            }
        }

        W($"  {opened} opened, {failed} could not be");
    }

    /// <summary>
    /// The two screens a new person meets before they have done anything.
    /// Neither can fail a check - a window with nothing in it and nothing to
    /// say raises no exception - so these exist to be looked at.
    /// </summary>
    private static void PhotographFirstRun()
    {
        W(string.Empty);
        W("what a new person sees");
        W(new string('-', 78));

        Photograph("welcome", () => new GuidedSetupForm());
        Photograph("main-window", () => new MainForm());
    }

    private static void Photograph(string name, Func<Form> create)
    {
        try
        {
            using var form = create();
            form.ShowInTaskbar = false;
            form.Show();

            // Longer than the sweep above: these have to finish loading, not
            // merely survive being opened.
            Settle(40);

            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));

            var path = Path.Combine(_outputDirectory, name + ".png");
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            W($"  {path}");

            form.Close();
            Application.DoEvents();
        }
        catch (Exception ex)
        {
            W($"  FAILED {name}: {ex.GetBaseException().Message}");
        }
    }

    private static void Settle(int cycles)
    {
        for (var i = 0; i < cycles; i++)
        {
            Application.DoEvents();
            Thread.Sleep(40);
        }
    }
}
