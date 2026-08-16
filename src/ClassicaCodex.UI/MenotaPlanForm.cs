using System.Xml.Linq;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Reviews and confirms a Menota ingest plan.
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
/// </summary>
public class MenotaPlanForm : ScaledForm
{
    private readonly MenotaIngestPlan _plan;
    private readonly XElement? _body;
    private readonly bool _scribalHeadings;
    private readonly DataGridView _grid;
    private readonly Label _summaryLabel;

    private const string ColInclude = "Include";
    private const string ColTitle = "Title";
    private const string ColAuthor = "Author";
    private const string ColWords = "Words";
    private const string ColMatch = "Matched by";
    private const string ColParts = "Parts";
    private const string ColSection = "Section";

    /// <summary>The confirmed plan, valid only when DialogResult is OK.</summary>
    public MenotaIngestPlan Result => _plan;

    /// <param name="body">
    /// The manuscript's body element, used to recount words after a merge or
    /// split. Optional only so the dialog can be opened without the file.
    /// </param>
    public MenotaPlanForm(MenotaIngestPlan plan, XElement? body = null)
    {
        _plan = plan;
        _body = body;
        _scribalHeadings = body != null
            && body.Descendants(MenotaXmlLoader.Tei + "head")
                .Any(h => h.Descendants(MenotaXmlLoader.Tei + "w").Any());

        Text = $"Review: {plan.ManuscriptId}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.Sizable;
        ClientSize = new Size(880, 620);
        MinimumSize = new Size(700, 480);

        var heading = new Label
        {
            Left = 14,
            Top = 12,
            Width = 850,
            Height = 26,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            Text = plan.ManuscriptId
        };

        _summaryLabel = new Label
        {
            Left = 14,
            Top = 42,
            Width = 850,
            Height = 20,
            ForeColor = Color.DimGray
        };

        var explanation = new Label
        {
            Left = 14,
            Top = 66,
            Width = 850,
            Height = 52,
            Text =
                "This manuscript contains several works. Below is a proposal for where one ends and the " +
                "next begins, and what each is called. Correct anything wrong, untick anything you don't " +
                "want, then confirm. Nothing is imported until you do."
        };

        // The planner's own account of what it could not decide - which
        // titles it had to guess at, which catalogue entries it found no text
        // for, what other division depths were available. Read-only: these are
        // observations about the file, not settings.
        var notesBox = new TextBox
        {
            Left = 14,
            Top = 122,
            Width = 850,
            Height = 86,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, plan.Notes),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _grid = new DataGridView
        {
            Left = 14,
            Top = 218,
            Width = 850,
            Height = 300,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };

        BuildColumns();
        LoadRows();

        var mergeButton = new Button
        {
            Text = "Merge Selected",
            Left = 14,
            Top = 528,
            Width = 150,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        mergeButton.Click += (_, _) => MergeSelected();

        var splitButton = new Button
        {
            Text = "Split Selected",
            Left = 172,
            Top = 528,
            Width = 150,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        splitButton.Click += (_, _) => SplitSelected();

        var authorButton = new Button
        {
            Text = "Set Author...",
            Left = 330,
            Top = 528,
            Width = 130,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        authorButton.Click += (_, _) => SetAuthorForSelected();

        var selectSectionButton = new Button
        {
            Text = "Select Section",
            Left = 468,
            Top = 528,
            Width = 130,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        selectSectionButton.Click += (_, _) => SelectWholeSection();

        var editHint = new Label
        {
            Left = 606,
            Top = 534,
            Width = 258,
            Height = 20,
            ForeColor = Color.DimGray,
            Text = "Merge, split, or set an author on a selection.",
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        var confirmButton = new Button
        {
            Text = "Confirm && Import",
            Left = 664,
            Top = 572,
            Width = 200,
            Height = 32,
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        confirmButton.Click += (sender, _) =>
        {
            CommitGridEdits();

            if (!AuthorNamesAreConsistent())
            {
                // Keep the dialog open so the names can be corrected here.
                DialogResult = DialogResult.None;
                return;
            }

            Commit();
        };

        var skipButton = new Button
        {
            Text = "Skip This Manuscript",
            Left = 484,
            Top = 572,
            Width = 170,
            Height = 32,
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        Controls.Add(heading);
        Controls.Add(_summaryLabel);
        Controls.Add(explanation);
        Controls.Add(notesBox);
        Controls.Add(_grid);
        Controls.Add(mergeButton);
        Controls.Add(splitButton);
        Controls.Add(authorButton);
        Controls.Add(selectSectionButton);
        Controls.Add(editHint);
        Controls.Add(confirmButton);
        Controls.Add(skipButton);

        AcceptButton = confirmButton;
        CancelButton = skipButton;

        UpdateSummary();
        _grid.CellValueChanged += (_, _) => UpdateSummary();
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // Without this a checkbox change isn't committed until focus
            // leaves the cell, so the summary line lags one click behind.
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        ReadingTheme.AttachTo(this);
    }

    private void BuildColumns()
    {
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = ColInclude,
            HeaderText = "Import",
            FillWeight = 8
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColTitle,
            HeaderText = "Work title",
            FillWeight = 28
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColAuthor,
            HeaderText = "Author (blank = Anonymous)",
            FillWeight = 22
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColWords,
            HeaderText = "Words",
            ReadOnly = true,
            FillWeight = 10,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColMatch,
            HeaderText = "Matched by",
            ReadOnly = true,
            FillWeight = 14
        });

        // Between the match column and the parts count, because it is the
        // column that decides what the author should be.
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColSection,
            HeaderText = "In section",
            ReadOnly = true,
            FillWeight = 18
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColParts,
            HeaderText = "Parts",
            ReadOnly = true,
            FillWeight = 8,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
    }

    private void LoadRows()
    {
        _grid.Rows.Clear();

        foreach (var work in _plan.Works)
        {
            if (_body != null) work.WordCount = MenotaIngestService.CountWords(_body, work);

            var index = _grid.Rows.Add(
                work.Include,
                work.Title,
                work.Author,
                work.WordCount.ToString("N0"),
                DescribeMatch(work),
                work.Section,
                work.DivPaths.Count.ToString());

            _grid.Rows[index].Tag = work;

            // A title the planner had to invent is the one most likely to be
            // wrong, so it is the one flagged. Colour rather than a warning
            // dialog: there is nothing to acknowledge, only something to look
            // at before confirming.
            if (work.MatchBasis == "unmatched")
                _grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(160, 60, 20);
        }
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

    /// <summary>
    /// Folds the selected rows into the first of them - one work made of
    /// several divisions.
    ///
    /// This is the case the planner cannot get right on its own: Alcuin's De
    /// virtutibus et vitiis in AM 619 4to is twenty-four div type="part", and
    /// nothing distinguishes twenty-four chapters of one work from
    /// twenty-four works except knowing what the text is.
    /// </summary>
    private void MergeSelected()
    {
        var rows = _grid.SelectedRows.Cast<DataGridViewRow>()
            .OrderBy(r => r.Index)
            .ToList();

        if (rows.Count < 2)
        {
            MessageBox.Show(this,
                "Select two or more rows to merge. Click a row, then shift-click another.",
                "Merge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        CommitGridEdits();

        var works = rows.Select(r => (MenotaWorkPlan)r.Tag!).ToList();
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
    /// Breaks a work back into one work per division.
    ///
    /// The counterpart to merging, and needed for the same reason: the
    /// proposal groups AM 28 8vo's 233 chapters into five runs at the points
    /// where the chapter numbering restarts, and the manuscript holds nine
    /// works. Numbering restarts are real evidence but they are not the only
    /// place a work ends, so the guess errs in both directions and only one of
    /// them was repairable.
    /// </summary>
    private void SplitSelected()
    {
        var rows = _grid.SelectedRows.Cast<DataGridViewRow>()
            .OrderBy(r => r.Index)
            .ToList();

        if (rows.Count == 0)
        {
            MessageBox.Show(this, "Select a row to split.", "Split",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (rows.All(r => ((MenotaWorkPlan)r.Tag!).DivPaths.Count < 2))
        {
            MessageBox.Show(this,
                "These rows are single divisions already - there is nothing inside them to split.",
                "Split", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        CommitGridEdits();

        foreach (var row in rows)
        {
            var work = (MenotaWorkPlan)row.Tag!;
            if (work.DivPaths.Count < 2) continue;

            var at = _plan.Works.IndexOf(work);
            if (at < 0) continue;

            var pieces = work.DivPaths.Select((path, i) =>
            {
                // Each piece takes its own division's heading where it has one.
                //
                // A work is usually split because it is a collection: the
                // Codex Regius eddic poems arrive as one row, and splitting
                // should give Voluspa, Havamal and Grimnismal, not "Eddic
                // poems (1)" through "(29)". The headings are right there in
                // the manuscript and were being thrown away.
                //
                // Numbering remains the fallback, because two pieces sharing a
                // title would mint one URN and overwrite each other on import.
                var heading = _body == null
                    ? ""
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
    private void SetAuthorForSelected()
    {
        var rows = _grid.SelectedRows.Cast<DataGridViewRow>().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "Select the rows the author wrote.", "Set Author",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        CommitGridEdits();

        // Seeded with what the catalogue says where the row says nothing.
        // msContents named Alcuin for AM 619 4to and no row ever received it,
        // because no title matched; offering the name is the difference
        // between the plan mentioning an attribution and the library having
        // one.
        var seed = (rows[0].Cells[ColAuthor].Value as string ?? "").Trim();
        if (seed.Length == 0 && _plan.DeclaredAuthors.Count == 1)
            seed = _plan.DeclaredAuthors[0];
        var declared = _plan.DeclaredAuthors.Count > 0
            ? $"\n\nNamed in this manuscript's catalogue: {string.Join(", ", _plan.DeclaredAuthors)}."
            : "";

        var name = Prompt(
            $"Author for {rows.Count} selected row(s). Leave blank for Anonymous.{declared}", seed);
        if (name == null) return;

        foreach (var row in rows)
        {
            row.Cells[ColAuthor].Value = name;
            if (row.Tag is MenotaWorkPlan work) work.Author = name;
        }

        UpdateSummary();
    }

    /// <summary>A one-line text prompt; WinForms has no built-in.</summary>
    private string? Prompt(string message, string initial)
    {
        using var dialog = new Form
        {
            Text = "Set Author",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(440, 170)
        };

        var label = new Label { Left = 12, Top = 12, Width = 416, Height = 76, Text = message };
        var box = new TextBox { Left = 12, Top = 94, Width = 416, Text = initial };
        var ok = new Button { Text = "OK", Left = 262, Top = 128, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 348, Top = 128, Width = 80, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[] { label, box, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        ReadingTheme.AttachTo(dialog);

        return dialog.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
    }

    /// <summary>
    /// Extends the selection to every row in the same section as the current
    /// one.
    ///
    /// AM 619 4to's opening rows are Alcuin's and the ninety-odd after them
    /// are not. Finding that boundary by eye in a scrolling grid is how his
    /// name reached forty-two Old Norwegian homilies three times running. The
    /// manuscript records the boundary; this puts it one click away.
    /// </summary>
    private void SelectWholeSection()
    {
        if (_grid.CurrentRow?.Tag is not MenotaWorkPlan current) return;

        if (current.Section.Length == 0)
        {
            MessageBox.Show(this,
                "This manuscript does not divide itself into named sections, so there is nothing to select.",
                "Select Section", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _grid.ClearSelection();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is MenotaWorkPlan work && work.Section == current.Section)
                row.Selected = true;
        }
    }

    private void CommitGridEdits()
    {
        _grid.EndEdit();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not MenotaWorkPlan work) continue;

            work.Include = row.Cells[ColInclude].Value is true;
            work.Title = (row.Cells[ColTitle].Value as string ?? "").Trim();
            work.Author = (row.Cells[ColAuthor].Value as string ?? "").Trim();

            if (work.Title.Length == 0) work.Title = "Untitled";

            // Re-slugged from whatever the title now says, since the title is
            // what the URN is built from and it may have just been corrected.
            work.UrnSlug = MenotaIngestPlanner.Slug(work.Title);
        }
    }

    private void Commit()
    {
        CommitGridEdits();
        _plan.Confirmed = true;
    }

    /// <summary>
    /// Refuses a plan whose author names differ only by a qualifier.
    ///
    /// "Alcuin", "Alcuin (homilies)" and "Alcuin (liturgical)" are three
    /// author records in the library, not one man and two of his moods. It is
    /// an easy thing to do with a Set Author button and a Section column side
    /// by side, and it is invisible afterwards: the browser simply lists three
    /// authors and nothing says they were meant to be one.
    ///
    /// Compared on the part before any bracket, so a genuine second Olafr or
    /// Snorri is untouched.
    /// </summary>
    private bool AuthorNamesAreConsistent()
    {
        var authors = _grid.Rows.Cast<DataGridViewRow>()
            .Select(r => (r.Cells[ColAuthor].Value as string ?? "").Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var families = authors
            .GroupBy(RootName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (families.Count == 0) return true;

        var detail = string.Join(Environment.NewLine,
            families.Select(g => "    " + string.Join(" / ", g)));

        var answer = MessageBox.Show(this,
            "These author names differ only by a qualifier, and will import as separate authors:" +
            Environment.NewLine + Environment.NewLine + detail + Environment.NewLine + Environment.NewLine +
            "If they are the same person, give them one name and use the section column to tell the " +
            "parts apart." + Environment.NewLine + Environment.NewLine + "Import them as separate authors anyway?",
            "Author names", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        return answer == DialogResult.Yes;
    }

    private static string RootName(string author)
    {
        var cut = author.IndexOfAny(new[] { '(', '[', ',', '-' });
        var root = cut > 0 ? author[..cut] : author;
        return new string(root.Where(char.IsLetterOrDigit).ToArray());
    }

    private void UpdateSummary()
    {
        var included = _grid.Rows.Cast<DataGridViewRow>()
            .Count(r => r.Cells[ColInclude].Value is true);

        var level = _plan.Orthography == "normalised"
            ? "normalised text"
            : "diplomatic text - readable and searchable, excluded from stylometry";

        _summaryLabel.Text =
            $"{_plan.FileName}  ·  {_plan.Language}  ·  {level}  ·  " +
            $"{included} of {_grid.Rows.Count} work(s) selected";
    }
}
