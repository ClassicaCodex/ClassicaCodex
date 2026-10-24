using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.WorkInfo;

/// <summary>
/// Everything the library knows about one work: its own catalogue entry,
/// each edition of it that is loaded, and the publication details those
/// editions' TEI headers carry - the Windows WorkDetailsForm.
///
/// That last part is the reason this exists. Which printed edition a digital
/// text was made from, and who edited it, is exactly what a reader needs to
/// know before quoting it.
///
/// Plain, fixed-pitch, selectable text rather than a grid: this is reference
/// text someone reads and often pastes into their own notes, and aligned
/// labels stay aligned when it is pasted.
/// </summary>
internal sealed class WorkDetailsWindow : FeatureWindow
{
    private readonly Work _work;
    private readonly SelectableTextBlock _details;
    private readonly TextBlock _status;

    public WorkDetailsWindow(Work work)
    {
        _work = work;

        Title = "Work Details";
        Width = 760;
        Height = 660;
        MinWidth = 560;
        MinHeight = 420;

        var heading = new TextBlock
        {
            Text = work.Title,
            Classes = { "h1" },
            FontFamily = Tags.PassageResults.FontFor(work.Title),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 12)
        };

        // The face set here rather than by the "mono" class: a style written
        // for TextBlock does not reach a SelectableTextBlock, and without the
        // fixed pitch the aligned labels are not aligned.
        _details = new SelectableTextBlock
        {
            Text = "Loading…",
            FontFamily = Application.Current?.FindResource("Cc.MonoFont") as FontFamily ?? FontFamily.Default,
            FontSize = 12,
            LineHeight = 18,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(16, 14)
        };

        var page = new Border
        {
            Classes = { "page" },
            Child = new ScrollViewer
            {
                Content = _details,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }
        };

        _status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

        var copy = new Button { Content = Tags.TagBrowserWindow.Labelled("Copy to Clipboard", "Copy") };
        copy.Click += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_details.Text)) await ClipboardText.SetAsync(this, _details.Text);
        };

        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 84 };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0) };
        buttons.Children.Add(copy);
        buttons.Children.Add(close);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 18) };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(heading);
        root.Children.Add(footer);
        root.Children.Add(page);
        Content = root;

        Opened += async (_, _) =>
        {
            // Close takes the focus, so nothing opens selected and Escape and
            // Return both do the obvious thing.
            close.Focus();
            await LoadDetailsAsync();
        };
    }

    internal string Report => _details.Text ?? string.Empty;

    internal string Status => _status.Text ?? string.Empty;

    internal async Task LoadDetailsAsync()
    {
        try
        {
            // Off the window's thread: the repositories' async methods run
            // synchronously, and a header read falls back to parsing the
            // source file, which for a long work is not instant.
            var (report, status) = await Task.Run(() => BuildReportAsync(_work));
            _details.Text = report;
            _status.Text = status;
        }
        catch (Exception ex)
        {
            _details.Text = $"Couldn't load details: {ex.Message}";
            _status.Text = string.Empty;
        }
    }

    internal static async Task<(string Report, string Status)> BuildReportAsync(Work work)
    {
        var authorRepo = new AuthorRepository();
        var editionRepo = new EditionRepository();
        var textNodeRepo = new TextNodeRepository();
        var editionHeaderRepo = new EditionHeaderRepository();
        var workRepo = new WorkRepository();

        var report = new StringBuilder();
        var headersRead = 0;
        var headersMissing = 0;
        var staleHeaders = 0;

        var author = await authorRepo.GetByIdAsync(work.AuthorId);
        var editions = await editionRepo.GetByWorkAsync(work.WorkId);
        var attribution = await workRepo.GetAttributionAsync(work.WorkId);

        report.AppendLine("WORK");
        Append(report, "Title", work.Title);
        Append(report, "CTS URN", work.CtsUrn);
        Append(report, "Citation scheme", work.CitationScheme);

        // The attribution dialog says its note is shown here, so it is. Only
        // when there is something to say: a securely attributed work nobody
        // has given a view on is the ordinary case, and a line saying so on
        // every work would be noise.
        if (attribution.Status != AttributionStatus.Accepted || attribution.SetByUser || !string.IsNullOrWhiteSpace(attribution.Note))
        {
            Append(report, "Attribution", AttributionWindow.Describe(attribution.Status)
                                          + (attribution.SetByUser ? " (your judgement)" : " (the library's catalog)"));
            Append(report, "Why", attribution.Note);
        }

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
            var lineCount = await textNodeRepo.CountByEditionAsync(edition.EditionId);

            report.AppendLine($"EDITION - {DescribeKind(edition)}");
            Append(report, "CTS URN", edition.CtsUrn);
            Append(report, "Language", edition.Language);
            Append(report, "Translator", edition.Translator);
            Append(report, "Lines", lineCount.ToString("N0"));
            Append(report, "Source file", edition.SourcePath);

            // The library first. Falling back to the source file only covers
            // editions ingested before headers were stored - it stops being
            // reached once a corpus has been re-ingested, and until then it
            // means an existing library doesn't lose this view while waiting.
            var header = await editionHeaderRepo.GetAsync(edition.EditionId);
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

                // Three genuinely different situations, worth distinguishing:
                // nothing recorded, a file that says nothing about itself, and
                // a file that has since been deleted.
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
                    Append(report, "  Responsibility", responsibility);

                Append(report, "  Edition", header.EditionStatement);
                Append(report, "  Publisher", header.Publisher);
                Append(report, "  Published", header.PublicationDate);
                Append(report, "  Place", header.PublicationPlace);
                Append(report, "  Availability", header.Availability);
                Append(report, "  Printed source", header.SourceDescription);
            }

            report.AppendLine();
        }

        var status = (headersMissing, staleHeaders) switch
        {
            (0, 0) => $"{editions.Count} edition(s).",
            (0, _) => $"{editions.Count} edition(s); {staleHeaders} still read from the source " +
                      "files. Re-ingesting that corpus stores them in the library.",
            _ => $"{editions.Count} edition(s); publication details available for {headersRead}. " +
                 "The rest were ingested without them - re-ingesting that corpus adds them."
        };

        return (report.ToString().TrimEnd() + Environment.NewLine, status);
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
