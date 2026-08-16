using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>Chronological, append-only history for one research project.</summary>
public sealed class ResearchLogForm : ScaledForm
{
    private readonly ResearchProject _project;
    private readonly ResearchRepository _repo = new();
    private readonly DataGridView _entries = new();
    private readonly TextBox _summary = new();
    private readonly TextBox _details = new();
    private readonly Label _status = new();

    public ResearchLogForm(ResearchProject project)
    {
        _project = project;
        Text = $"Research Log — {project.Name}";
        Width = 1000;
        Height = 680;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "WordStudy");

        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(10, 12, 8, 0),
            Text = "Project history — newest first",
            Font = new Font(Font, FontStyle.Bold)
        };

        _entries.Dock = DockStyle.Fill;
        _entries.AutoGenerateColumns = false;
        _entries.AllowUserToAddRows = false;
        _entries.AllowUserToDeleteRows = false;
        _entries.ReadOnly = true;
        _entries.MultiSelect = false;
        _entries.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _entries.RowHeadersVisible = false;
        _entries.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "CreatedUtc", HeaderText = "When", Width = 145,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "g" }
        });
        _entries.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Kind", HeaderText = "Kind", Width = 125
        });
        _entries.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Summary", HeaderText = "Entry",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _entries.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Details", HeaderText = "Details", Width = 240
        });

        var addPanel = new Panel
        {
            Dock = DockStyle.Bottom, Height = 150, Width = ClientSize.Width, Padding = new Padding(10)
        };
        var noteLabel = new Label { Text = "Add a dated research note", Left = 10, Top = 8, Width = 250, Height = 20 };
        _summary.SetBounds(10, 30, 690, 26);
        _summary.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _summary.PlaceholderText = "Decision, observation, next step, or unresolved concern";
        _details.SetBounds(10, 62, 850, 52);
        _details.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.PlaceholderText = "Optional context or rationale";
        var add = new Button { Text = "Add note", Width = 95, Height = 28, Top = 29, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        add.Left = addPanel.ClientSize.Width - add.Width - 10;
        add.Click += async (_, _) => await AddNoteAsync();
        _status.SetBounds(10, 119, 850, 20);
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        addPanel.Controls.AddRange(new Control[] { noteLabel, _summary, _details, add, _status });

        Controls.Add(_entries);
        Controls.Add(addPanel);
        Controls.Add(heading);
        ReadingTheme.AttachTo(this, () => _status.ForeColor = ReadingTheme.MutedText);
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) => await ReloadAsync();
    }

    private async Task AddNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(_summary.Text))
        {
            MessageBox.Show(this, "Enter a research note.");
            return;
        }

        await _repo.AddResearchLogEntryAsync(new ResearchLogEntry
        {
            ResearchProjectId = _project.ResearchProjectId,
            Summary = _summary.Text.Trim(),
            Details = string.IsNullOrWhiteSpace(_details.Text) ? null : _details.Text.Trim()
        });
        _summary.Clear();
        _details.Clear();
        await ReloadAsync();
        _status.Text = "Research note added. Log entries are retained with the project.";
        _summary.Focus();
    }

    private async Task ReloadAsync()
    {
        var entries = await _repo.GetResearchLogAsync(_project.ResearchProjectId);
        foreach (var entry in entries)
            entry.CreatedUtc = entry.CreatedUtc.ToLocalTime();
        _entries.DataSource = entries;
        _status.Text = $"{entries.Count} log entr{(entries.Count == 1 ? "y" : "ies")}.";
    }
}
