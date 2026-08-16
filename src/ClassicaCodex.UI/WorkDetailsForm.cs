using System.Text;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Everything the library knows about one work: its own catalogue entry, each
/// edition of it that's loaded, and - read live from the source files - the
/// publication metadata those files carry in their TEI headers.
///
/// That last part is the reason this exists. Which printed edition a digital
/// text was made from, and who edited it, is exactly what a reader needs to
/// know before quoting it, and the app has never shown it: the ingest reads
/// the body of each file and ignores the header entirely. The information was
/// always sitting on disk, just never surfaced.
/// </summary>
public class WorkDetailsForm : ScaledForm
{
    private readonly Work _work;
    private readonly TextBox _detailsBox;
    private readonly Label _statusLabel;

    private readonly AuthorRepository _authorRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _editionHeaderRepo = new();

    public WorkDetailsForm(Work work)
    {
        _work = work;

        Text = "Work Details";
        AppIcons.ApplyWindowIcon(this, "Help");
        Width = 720;
        Height = 640;
        MinimumSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;

        var headerLabel = new Label
        {
            Text = work.Title,
            Left = 16,
            Top = 12,
            Width = 672,
            Height = 24,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold)
        };

        // Read-only rather than a grid or a tree: this is reference text
        // someone reads and often wants to paste into their own notes, and a
        // text box gives selection and copy for free. Fixed-pitch so the
        // aligned labels stay aligned.
        _detailsBox = new TextBox
        {
            Left = 16,
            Top = 44,
            Width = 672,
            Height = 500,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9.5F),
            Text = "Loading..."
        };

        _statusLabel = new Label
        {
            Left = 16,
            Top = 554,
            Width = 480,
            Height = 32,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray
        };

        var copyButton = new Button
        {
            Text = "Copy to Clipboard",
            Left = 504,
            Top = 556,
            Width = 100,
            Height = 28,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_detailsBox.Text)) Clipboard.SetText(_detailsBox.Text);
        };
        AppIcons.Apply(copyButton, "CopyToClipboard", 16);

        var closeButton = new Button
        {
            Text = "Close",
            Left = 612,
            Top = 556,
            Width = 76,
            Height = 28,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };
        CancelButton = closeButton;

        Controls.Add(headerLabel);
        Controls.Add(_detailsBox);
        Controls.Add(_statusLabel);
        Controls.Add(copyButton);
        Controls.Add(closeButton);

        Load += async (_, _) =>
        {
            await LoadDetailsAsync();

            // A TextBox selects its entire contents the first time it takes
            // focus, and this one is the first control in the tab order - so
            // the whole report opened highlighted. Handing initial focus to
            // Close instead leaves the text unselected and still fully
            // selectable by hand, which is the point of it being a TextBox.
            _detailsBox.SelectionStart = 0;
            _detailsBox.SelectionLength = 0;
            ActiveControl = closeButton;
        };

        ReadingTheme.AttachTo(this);
    }

    private async Task LoadDetailsAsync()
    {
        var report = new StringBuilder();
        var headersRead = 0;
        var headersMissing = 0;
        var staleHeaders = 0;

        try
        {
            var author = await _authorRepo.GetByIdAsync(_work.AuthorId);
            var editions = await _editionRepo.GetByWorkAsync(_work.WorkId);

            report.AppendLine("WORK");
            Append(report, "Title", _work.Title);
            Append(report, "CTS URN", _work.CtsUrn);
            Append(report, "Citation scheme", _work.CitationScheme);
            report.AppendLine();

            report.AppendLine("AUTHOR");
            Append(report, "Name", author?.Name);
            Append(report, "CTS URN", author?.CtsUrn);
            Append(report, "Corpus", author?.Namespace);
            Append(report, "Language", author?.Language);
            report.AppendLine();

            if (editions.Count == 0)
            {
                report.AppendLine("EDITIONS");
                report.AppendLine("  (none loaded)");
            }

            foreach (var edition in editions)
            {
                var lineCount = await _textNodeRepo.CountByEditionAsync(edition.EditionId);

                report.AppendLine($"EDITION - {DescribeKind(edition)}");
                Append(report, "CTS URN", edition.CtsUrn);
                Append(report, "Language", edition.Language);
                Append(report, "Translator", edition.Translator);
                Append(report, "Lines", lineCount.ToString("N0"));
                Append(report, "Source file", edition.SourcePath);

                // The library first. Falling back to the source file only
                // covers editions ingested before headers were stored - it
                // stops being reached once a corpus has been re-ingested,
                // and until then it means an existing library doesn't lose
                // this view while waiting.
                var header = await _editionHeaderRepo.GetAsync(edition.EditionId);
                var fromFile = false;

                if (header == null)
                {
                    header = TeiHeaderReader.TryRead(edition.SourcePath);
                    fromFile = header != null;
                    if (fromFile) staleHeaders++;
                }

                if (header == null)
                {
                    headersMissing++;

                    // Three genuinely different situations, worth
                    // distinguishing: nothing recorded, a file that says
                    // nothing about itself, and a file that has since been
                    // deleted.
                    report.AppendLine(string.IsNullOrWhiteSpace(edition.SourcePath)
                        ? "  (no publication details recorded for this edition)"
                        : File.Exists(edition.SourcePath)
                            ? "  (source file has no TEI header details)"
                            : "  (not recorded, and the source file is no longer on disk)");
                }
                else
                {
                    headersRead++;
                    report.AppendLine(fromFile
                        ? "  Publication details (read from the source file - re-ingest to store these):"
                        : "  Publication details:");
                    Append(report, "  Title", header.Title);
                    Append(report, "  Author", header.Author);

                    foreach (var responsibility in header.Responsibilities)
                    {
                        Append(report, "  Responsibility", responsibility);
                    }

                    Append(report, "  Edition", header.EditionStatement);
                    Append(report, "  Publisher", header.Publisher);
                    Append(report, "  Published", header.PublicationDate);
                    Append(report, "  Place", header.PublicationPlace);
                    Append(report, "  Availability", header.Availability);
                    Append(report, "  Printed source", header.SourceDescription);
                }

                report.AppendLine();
            }

            _detailsBox.Text = report.ToString().Replace("\n", Environment.NewLine);

            _statusLabel.Text = (headersMissing, staleHeaders) switch
            {
                (0, 0) => $"{editions.Count} edition(s).",
                (0, _) => $"{editions.Count} edition(s); {staleHeaders} still read from the source " +
                          "files. Re-ingesting that corpus stores them in the library.",
                _ => $"{editions.Count} edition(s); publication details available for {headersRead}. " +
                     "The rest were ingested without them - re-ingesting that corpus adds them."
            };
        }
        catch (Exception ex)
        {
            _detailsBox.Text = $"Couldn't load details: {ex.Message}";
            _statusLabel.Text = string.Empty;
        }
    }

    /// <summary>
    /// Kind is stored as an enum that can legitimately be Unknown - plenty of
    /// editions are ingested without anything in the file saying which they
    /// are - so this reads it out as words rather than showing "Unknown",
    /// which looks like a fault rather than an absence.
    /// </summary>
    private static string DescribeKind(Edition edition) => edition.Kind switch
    {
        EditionKind.Original => "original language",
        EditionKind.Translation => "translation",
        _ => "kind not recorded"
    };

    /// <summary>
    /// Skips a field entirely when the source has nothing for it. A column of
    /// empty labels would suggest the data is missing in some meaningful
    /// sense, when usually the file simply never carried that element.
    /// </summary>
    private static void Append(StringBuilder report, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        report.AppendLine($"  {label,-18} {value}");
    }
}
