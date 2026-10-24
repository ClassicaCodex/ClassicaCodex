using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Reviews and confirms a Menota import plan - the Mac counterpart of
/// MenotaPlanForm.
///
/// A Menota file is a manuscript containing several works, and nothing in it
/// links the catalogue's msItem entries to the body divisions that hold their
/// text - no corresp, no xml:id on any div, and the msItem titles are English
/// editorial titles where the divisions' heads are Old Norse. So the division
/// is proposed by MenotaIngestPlanner and a person confirms it. This is where
/// they do that.
///
/// It replaces editing the .plan.json by hand. The file is still written and
/// still readable, which is worth keeping - it is a record of what was
/// confirmed, and it survives a reinstall - but it is no longer the interface.
///
/// Closed with true when confirmed, false when the manuscript is skipped.
/// </summary>
public sealed class MenotaPlanWindow : FeatureWindow
{
    private readonly MenotaIngestPlan _plan;
    private readonly XElement? _body;
    private readonly bool _scribalHeadings;
    private readonly ObservableCollection<PlanRow> _rows = new();
    private readonly DataGrid _grid;
    private readonly TextBlock _summaryLabel = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

    /// <summary>The confirmed plan, valid only when the window closed with true.</summary>
    public MenotaIngestPlan Result => _plan;

    internal IReadOnlyList<PlanRow> Rows => _rows;

    internal string Summary => _summaryLabel.Text ?? string.Empty;

    /// <param name="body">
    /// The manuscript's body element, used to recount words after a merge or
    /// split, and to name the pieces of a split. Optional only so the window
    /// can be opened without the file.
    /// </param>
    public MenotaPlanWindow(MenotaIngestPlan plan, XElement? body = null)
    {
        _plan = plan;
        _body = body;
        _scribalHeadings = body != null
            && body.Descendants(MenotaXmlLoader.Tei + "head")
                .Any(h => h.Descendants(MenotaXmlLoader.Tei + "w").Any());

        Title = $"Review: {plan.ManuscriptId}";
        Width = 960;
        Height = 680;
        MinWidth = 760;
        MinHeight = 500;
        ShowInTaskbar = false;

        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var tile = Art.Tile("nordic", "Nordic", 44);
        tile.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(tile, Dock.Left);
        heading.Children.Add(tile);
        var headingText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        headingText.Children.Add(new TextBlock { Text = plan.ManuscriptId, Classes = { "h1" } });
        headingText.Children.Add(_summaryLabel);
        heading.Children.Add(headingText);

        var explanation = SetupLook.Text(
            "This manuscript contains several works. Below is a proposal for where one ends and the next begins, "
            + "and what each is called. Correct anything wrong, untick anything you don't want, then confirm. "
            + "Nothing is imported until you do.");

        // The planner's own account of what it could not decide - which titles
        // it had to guess at, which catalogue entries it found no text for,
        // what other division depths were available. Read-only: these are
        // observations about the file, not settings.
        var notes = SetupLook.LogBox(86);
        notes.Text = string.Join("\n", plan.Notes);

        _grid = new DataGrid
        {
            ItemsSource = _rows,
            IsReadOnly = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Extended,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            AutoGenerateColumns = false
        };
        BuildColumns();

        // A title the planner had to invent is the one most likely to be wrong,
        // so it is the one flagged. Colour rather than a warning: there is
        // nothing to acknowledge, only something to look at before confirming.
        // From the palette, so it reads on the dark page as well as the light -
        // the literal the Windows form first had drew at 2.66:1 in dark mode,
        // which made the row singled out for attention the hardest to read.
        //
        // A class rather than a value set on the row, because the table reuses
        // its row controls: a colour set on one stayed with the control when a
        // split handed it a row that was no longer unmatched.
        Styles.Add(new Style(x => x.OfType<DataGridRow>().Class("unmatched"))
        {
            Setters = { new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("Cc.Danger")) }
        });
        _grid.LoadingRow += (_, e) => e.Row.Classes.Set("unmatched", e.Row.DataContext is PlanRow { Unmatched: true });

        var merge = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Merge Selected" };
        merge.Click += async (_, _) => await MergeSelectedAsync();
        var split = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Split Selected" };
        split.Click += async (_, _) => await SplitSelectedAsync();
        var author = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Set Author…" };
        author.Click += async (_, _) => await SetAuthorForSelectedAsync();
        var section = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Select Section" };
        section.Click += async (_, _) => await SelectWholeSectionAsync();

        var editRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var b in new[] { merge, split, author, section })
        {
            b.Margin = new Thickness(0, 0, 8, 6);
            editRow.Children.Add(b);
        }

        editRow.Children.Add(new TextBlock
        {
            Text = "Merge, split, or set an author on a selection. ⇧-click or ⌘-click to select several rows.",
            Classes = { "caption" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 6)
        });

        var skip = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Skip This Manuscript", MinWidth = 160, IsCancel = true };
        skip.Click += (_, _) => Close(false);
        var confirm = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Confirm & Import", Classes = { "accent" }, MinWidth = 170, IsDefault = true };
        confirm.Click += async (_, _) => await ConfirmAsync();

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(skip);
        footer.Children.Add(confirm);

        var top = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(heading);
        top.Children.Add(explanation);
        top.Children.Add(notes);

        var bottom = new StackPanel();
        bottom.Children.Add(editRow);
        bottom.Children.Add(footer);

        var layout = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(bottom);
        layout.Children.Add(_grid);
        Content = layout;

        LoadRows();
        UpdateSummary();
    }

    private void BuildColumns()
    {
        var include = new DataGridTemplateColumn
        {
            Header = "Import",
            Width = new DataGridLength(88),
            CellTemplate = new FuncDataTemplate<PlanRow>((_, _) =>
            {
                var box = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 0 };
                box.Bind(CheckBox.IsCheckedProperty, new ReflectionBinding(nameof(PlanRow.Include)) { Mode = BindingMode.TwoWay });
                return box;
            })
        };

        _grid.Columns.Add(include);
        _grid.Columns.Add(Column("Work title", nameof(PlanRow.Title), 2.6, editable: true));
        _grid.Columns.Add(Column("Author (blank = Anonymous)", nameof(PlanRow.Author), 2.4, editable: true));
        _grid.Columns.Add(Fixed(Column("Words", nameof(PlanRow.Words), 0), 84));
        _grid.Columns.Add(Column("Matched by", nameof(PlanRow.MatchedBy), 1.5));

        // Between the match column and the parts count, because it is the
        // column that decides what the author should be.
        _grid.Columns.Add(Column("In section", nameof(PlanRow.Section), 1.6));
        _grid.Columns.Add(Fixed(Column("Parts", nameof(PlanRow.Parts), 0), 70));
    }

    private static DataGridColumn Fixed(DataGridColumn column, double width)
    {
        column.Width = new DataGridLength(width);
        return column;
    }

    private static DataGridTextColumn Column(string header, string property, double weight, bool editable = false) => new()
    {
        Header = header,
        Binding = new ReflectionBinding(property) { Mode = editable ? BindingMode.TwoWay : BindingMode.OneWay },
        IsReadOnly = !editable,
        Width = new DataGridLength(weight, DataGridLengthUnitType.Star)
    };

    private void LoadRows()
    {
        foreach (var row in _rows) row.PropertyChanged -= OnRowChanged;
        _rows.Clear();

        foreach (var work in _plan.Works)
        {
            if (_body != null) work.WordCount = MenotaIngestService.CountWords(_body, work);

            var row = new PlanRow(work, DescribeMatch(work));
            row.PropertyChanged += OnRowChanged;
            _rows.Add(row);
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlanRow.Include)) UpdateSummary();
    }

    private static string DescribeMatch(MenotaWorkPlan work) => work.MatchBasis switch
    {
        "head-text" => $"heading (item {work.MsItemN})",
        "position" => $"order (item {work.MsItemN})",
        "merged" => "merged by hand",
        "split by hand" => "split by hand",
        "heading (after split)" => "heading (after split)",
        _ => "not matched"
    };

    private List<PlanRow> SelectedRows() =>
        _grid.SelectedItems.OfType<PlanRow>().OrderBy(r => _rows.IndexOf(r)).ToList();

    /// <summary>Selects rows by position - for tests, which cannot shift-click.</summary>
    internal void Select(params int[] indexes)
    {
        _grid.SelectedItems.Clear();
        foreach (var i in indexes) _grid.SelectedItems.Add(_rows[i]);
    }

    /// <summary>
    /// Folds the selected rows into the first of them - one work made of
    /// several divisions.
    ///
    /// This is the case the planner cannot get right on its own: Alcuin's De
    /// virtutibus et vitiis in AM 619 4to is twenty-four div type="part", and
    /// nothing distinguishes twenty-four chapters of one work from twenty-four
    /// works except knowing what the text is.
    /// </summary>
    internal async Task MergeSelectedAsync()
    {
        var rows = SelectedRows();

        if (rows.Count < 2)
        {
            await Alert.Info(this, "Merge", "Select two or more rows to merge. Click a row, then ⇧-click another.");
            return;
        }

        CommitGridEdits();

        var works = rows.Select(r => r.Work).ToList();
        var target = works[0];

        foreach (var other in works.Skip(1))
        {
            target.DivPaths.AddRange(other.DivPaths);
            target.WordCount += other.WordCount;
            _plan.Works.Remove(other);
        }

        target.MatchBasis = "merged";
        LoadRows();
        UpdateSummary();
    }

    /// <summary>
    /// Breaks a work back into one work per division - the counterpart to
    /// merging, and needed for the same reason: the proposal groups AM 28 8vo's
    /// 233 chapters into five runs at the points where the chapter numbering
    /// restarts, and the manuscript holds nine works. Numbering restarts are
    /// real evidence but not the only place a work ends, so the guess errs in
    /// both directions, and only one of them was repairable.
    /// </summary>
    internal async Task SplitSelectedAsync()
    {
        var rows = SelectedRows();

        if (rows.Count == 0)
        {
            await Alert.Info(this, "Split", "Select a row to split.");
            return;
        }

        if (rows.All(r => r.Work.DivPaths.Count < 2))
        {
            await Alert.Info(this, "Split", "These rows are single divisions already - there is nothing inside them to split.");
            return;
        }

        CommitGridEdits();

        foreach (var work in rows.Select(r => r.Work))
        {
            if (work.DivPaths.Count < 2) continue;

            var at = _plan.Works.IndexOf(work);
            if (at < 0) continue;

            var pieces = work.DivPaths.Select((path, i) =>
            {
                // Each piece takes its own division's heading where it has one.
                // A work is usually split because it is a collection: the Codex
                // Regius eddic poems arrive as one row, and splitting should
                // give Voluspa, Havamal and Grimnismal, not "Eddic poems (1)"
                // through "(29)". Numbering is the fallback, because two pieces
                // sharing a title would mint one URN and overwrite each other.
                var heading = _body == null
                    ? string.Empty
                    : MenotaXmlLoader.FirstHeading(
                        MenotaIngestService.ResolvePath(_body, path) ?? _body,
                        _plan.ReadingLevel,
                        _scribalHeadings);

                var title = heading.Length > 0 ? heading : $"{work.Title} ({i + 1})";

                return new MenotaWorkPlan
                {
                    DivPaths = new List<string> { path },
                    Title = title,
                    Author = work.Author,
                    UrnSlug = MenotaIngestPlanner.Slug(title),
                    MsItemN = work.MsItemN,
                    MatchBasis = heading.Length > 0 ? "heading (after split)" : "split by hand",
                    WordCount = 0,
                    Include = work.Include
                };
            }).ToList();

            // Two divisions can carry the same heading. Disambiguate the slug
            // only, leaving the titles as the manuscript has them.
            for (var p = 0; p < pieces.Count; p++)
            {
                if (pieces.Take(p).Any(e => e.UrnSlug == pieces[p].UrnSlug))
                    pieces[p].UrnSlug = $"{pieces[p].UrnSlug}-{p + 1}";
            }

            _plan.Works.RemoveAt(at);
            _plan.Works.InsertRange(at, pieces);
        }

        LoadRows();
        UpdateSummary();
    }

    /// <summary>
    /// Puts one author on every selected row.
    ///
    /// The catalogue names an author on an msItem, and a work only receives it
    /// by matching that item - so a manuscript whose titles do not match
    /// imports entirely as Anonymous however clearly msContents says
    /// otherwise. The plan's notes name whoever the catalogue names; this is
    /// how that gets applied without typing it into twenty-four rows.
    /// </summary>
    private async Task SetAuthorForSelectedAsync()
    {
        var rows = SelectedRows();
        if (rows.Count == 0)
        {
            await Alert.Info(this, "Set Author", "Select the rows the author wrote.");
            return;
        }

        CommitGridEdits();

        // Seeded with what the catalogue says where the row says nothing.
        // msContents named Alcuin for AM 619 4to and no row ever received it,
        // because no title matched; offering the name is the difference between
        // the plan mentioning an attribution and the library having one.
        var seed = rows[0].Author.Trim();
        if (seed.Length == 0 && _plan.DeclaredAuthors.Count == 1) seed = _plan.DeclaredAuthors[0];
        var declared = _plan.DeclaredAuthors.Count > 0
            ? $"\n\nNamed in this manuscript's catalogue: {string.Join(", ", _plan.DeclaredAuthors)}."
            : string.Empty;

        var name = await TextPrompt.Ask(this, "Set Author",
            $"Author for {rows.Count} selected row(s). Leave blank for Anonymous.{declared}", seed, "Set Author");
        if (name == null) return;

        SetAuthor(rows, name.Trim());
    }

    internal void SetAuthor(IEnumerable<PlanRow> rows, string name)
    {
        foreach (var row in rows)
        {
            row.Author = name;
            row.Work.Author = name;
        }

        UpdateSummary();
    }

    /// <summary>
    /// Extends the selection to every row in the same section as the current
    /// one.
    ///
    /// AM 619 4to's opening rows are Alcuin's and the ninety-odd after them are
    /// not. Finding that boundary by eye in a scrolling table is how his name
    /// reached forty-two Old Norwegian homilies three times running. The
    /// manuscript records the boundary; this puts it one click away.
    /// </summary>
    private async Task SelectWholeSectionAsync()
    {
        if (_grid.SelectedItem is not PlanRow current) return;

        if (current.Section.Length == 0)
        {
            await Alert.Info(this, "Select Section",
                "This manuscript does not divide itself into named sections, so there is nothing to select.");
            return;
        }

        _grid.SelectedItems.Clear();
        foreach (var row in _rows.Where(r => r.Section == current.Section)) _grid.SelectedItems.Add(row);
    }

    private void CommitGridEdits()
    {
        _grid.CommitEdit();

        foreach (var row in _rows)
        {
            var work = row.Work;
            work.Include = row.Include;
            work.Title = row.Title.Trim();
            work.Author = row.Author.Trim();

            if (work.Title.Length == 0) work.Title = "Untitled";

            // Re-slugged from whatever the title now says, since the title is
            // what the URN is built from and it may have just been corrected.
            work.UrnSlug = MenotaIngestPlanner.Slug(work.Title);
        }
    }

    private async Task ConfirmAsync()
    {
        CommitGridEdits();

        // Kept open so the names can be corrected here.
        if (!await AuthorNamesAreConsistentAsync()) return;

        Commit();
        Close(true);
    }

    internal void Commit()
    {
        CommitGridEdits();
        _plan.Confirmed = true;
    }

    /// <summary>
    /// Refuses a plan whose author names differ only by a qualifier.
    ///
    /// "Alcuin", "Alcuin (homilies)" and "Alcuin (liturgical)" are three author
    /// records in the library, not one man and two of his moods. It is an easy
    /// thing to do with a Set Author button and a Section column side by side,
    /// and invisible afterwards: the library simply lists three authors and
    /// nothing says they were meant to be one. Compared on the part before any
    /// bracket, so a genuine second Olafr or Snorri is untouched.
    /// </summary>
    private async Task<bool> AuthorNamesAreConsistentAsync()
    {
        var families = AuthorFamilies(_rows.Select(r => r.Author));
        if (families.Count == 0) return true;

        var detail = string.Join("\n", families.Select(g => "    " + string.Join(" / ", g)));

        return await Alert.Confirm(this, "Author names",
            "These author names differ only by a qualifier, and will import as separate authors:\n\n"
            + detail + "\n\n"
            + "If they are the same person, give them one name and use the section column to tell the parts apart.\n\n"
            + "Import them as separate authors anyway?",
            confirm: "Import Separately", cancel: "Go Back", kind: AlertKind.Warning);
    }

    internal static List<List<string>> AuthorFamilies(IEnumerable<string> names)
    {
        var authors = names
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return authors
            .GroupBy(RootName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();
    }

    private static string RootName(string author)
    {
        var cut = author.IndexOfAny(new[] { '(', '[', ',', '-' });
        var root = cut > 0 ? author[..cut] : author;
        return new string(root.Where(char.IsLetterOrDigit).ToArray());
    }

    private void UpdateSummary()
    {
        var included = _rows.Count(r => r.Include);

        var level = _plan.Orthography == "normalised"
            ? "normalised text"
            : "diplomatic text - readable and searchable, excluded from stylometry";

        _summaryLabel.Text =
            $"{_plan.FileName}  ·  {_plan.Language}  ·  {level}  ·  {included} of {_rows.Count} work(s) selected";
    }

    /// <summary>One row of the review table: a proposed work, as the reader edits it.</summary>
    internal sealed class PlanRow : INotifyPropertyChanged
    {
        private bool _include;
        private string _title;
        private string _author;

        public PlanRow(MenotaWorkPlan work, string matchedBy)
        {
            Work = work;
            _include = work.Include;
            _title = work.Title;
            _author = work.Author;
            MatchedBy = matchedBy;
        }

        public MenotaWorkPlan Work { get; }

        public bool Include
        {
            get => _include;
            set => Set(ref _include, value);
        }

        public string Title
        {
            get => _title;
            set => Set(ref _title, value ?? string.Empty);
        }

        public string Author
        {
            get => _author;
            set => Set(ref _author, value ?? string.Empty);
        }

        public string Words => Work.WordCount.ToString("N0");

        public string MatchedBy { get; }

        public string Section => Work.Section;

        public string Parts => Work.DivPaths.Count.ToString();

        public bool Unmatched => Work.MatchBasis == "unmatched";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
