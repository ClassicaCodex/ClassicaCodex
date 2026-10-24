using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.Research.Sources;

/// <summary>
/// The local files behind one evidence item - a PDF of an article, a scan of
/// an edition - fingerprinted where they lie, with page-addressed quotations
/// and notes on each.
/// </summary>
internal sealed class EvidenceSourcesWindow : ResearchWindow
{
    private static readonly FilePickerFileType AnyFile = new("All files") { Patterns = new[] { "*" } };

    private readonly EvidenceItem _evidence;
    private readonly ResearchSourceRepository _repo = new();
    private readonly ListBox _files;
    private readonly TextBlock _integrity = Ui.Caption(string.Empty);
    private readonly DataGrid _annotations = Ui.Table();
    private readonly TextBox _page = new() { Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _quote = Ui.Area(52);
    private readonly TextBox _note = Ui.Area(42);
    private readonly ComboBox _judgment = Ui.EnumCombo<EvidenceJudgment>();
    private readonly TextBlock _status = Ui.Status();
    private EvidencePageAnnotation? _editing;
    private bool _loading;

    public EvidenceSourcesWindow(EvidenceItem evidence) : base(null)
    {
        _evidence = evidence;
        Title = $"Source Files & Page Notes — {evidence.Title}";
        Width = 1050;
        Height = 760;
        MinWidth = 820;
        MinHeight = 600;

        _files = Ui.TextList<EvidenceAttachment>(f => f.FileName, f => f.FilePath);
        _files.Height = 110;
        _files.SelectionChanged += (_, _) => Run(FileChangedAsync);

        var attach = Ui.Button("Attach PDF…", "FilePdf");
        var open = Ui.Button("Open PDF", "OpenExternal");
        var verify = Ui.Button("Verify File", "Validation");
        var removeFile = Ui.Button("Remove Link", "Minus");
        attach.Click += (_, _) => Run(AttachAsync);
        open.Click += (_, _) => Run(OpenFileAsync);
        verify.Click += (_, _) => Run(VerifyAsync);
        removeFile.Click += (_, _) => Run(RemoveFileAsync);

        // Wrapping, so the four buttons stay on screen however narrow the window -
        // on Windows the window could not be made narrower than the row they made.
        var fileButtons = new WrapPanel();
        foreach (var button in new[] { attach, open, verify, removeFile })
        {
            button.Margin = new Thickness(0, 0, 8, 8);
            fileButtons.Children.Add(button);
        }

        var fileTools = new StackPanel { Spacing = 0 };
        fileTools.Children.Add(fileButtons);
        fileTools.Children.Add(_integrity);

        var filesRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        var fileList = new Border { Classes = { "panel" }, Child = _files };
        Grid.SetColumn(fileList, 0);
        Grid.SetColumn(fileTools, 2);
        filesRow.Children.Add(fileList);
        filesRow.Children.Add(fileTools);

        var top = new StackPanel { Margin = new Thickness(16, 14, 16, 10), Spacing = 8 };
        top.Children.Add(Ui.Section("Local source files"));
        top.Children.Add(filesRow);
        top.Children.Add(Ui.Muted("Files are referenced in place and are not copied into the database."));

        _annotations.Columns.Add(Ui.Column("Page", nameof(NoteRow.Page), 64));
        _annotations.Columns.Add(Ui.Column("Review", nameof(NoteRow.Review), 90));
        _annotations.Columns.Add(Ui.Column("Quotation", nameof(NoteRow.Quotation)));
        _annotations.Columns.Add(Ui.Column("Researcher note", nameof(NoteRow.Note), 300));
        _annotations.Margin = new Thickness(16, 0, 16, 10);
        _annotations.SelectionChanged += (_, _) =>
        {
            if (!_loading) ShowAnnotation(CurrentAnnotation);
        };

        var fresh = Ui.Button("New Note", "Plus", tip: "Start a new page note (" + Ui.Chord("N") + ")");
        var save = Ui.Button("Save Note", "Check", accent: true, tip: "Save the page note (" + Ui.Chord("S") + ")");
        var remove = Ui.Button("Remove Note", "Minus");
        fresh.Click += (_, _) => Run(NewAnnotationAsync);
        save.Click += (_, _) => Run(SaveAnnotationAsync);
        remove.Click += (_, _) => Run(RemoveAnnotationAsync);

        var pageRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,200,16,*") };
        var pageField = Ui.Field("PDF page number", _page, 0);
        var reviewField = Ui.Field("Human review", _judgment, 0);
        var buttons = Ui.Row(fresh, save, remove);
        buttons.VerticalAlignment = VerticalAlignment.Bottom;
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(pageField, 0);
        Grid.SetColumn(reviewField, 2);
        Grid.SetColumn(buttons, 4);
        pageRow.Children.Add(pageField);
        pageRow.Children.Add(reviewField);
        pageRow.Children.Add(buttons);

        var editor = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        editor.Children.Add(pageRow);
        editor.Children.Add(new Border { Height = 10 });
        editor.Children.Add(Ui.Field("Exact quotation", _quote));
        editor.Children.Add(Ui.Field("Researcher note / interpretation", _note, 0));
        var editorBorder = new Border { Child = editor, BorderThickness = new Thickness(0, 1, 0, 0) };
        editorBorder.Bind(Border.BorderBrushProperty, editorBorder.GetResourceObservable("Cc.Rule"));
        editorBorder.Bind(Border.BackgroundProperty, editorBorder.GetResourceObservable("Cc.PageHeader"));

        var status = Ui.StatusBar(_status);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(editorBorder, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(status);
        dock.Children.Add(editorBorder);
        dock.Children.Add(_annotations);
        Content = dock;

        Opened += (_, _) => Run(() => ReloadFilesAsync());
    }

    private EvidenceAttachment? CurrentFile => _files.SelectedItem as EvidenceAttachment;

    private EvidencePageAnnotation? CurrentAnnotation => (_annotations.SelectedItem as NoteRow)?.Annotation;

    protected override bool OnCommandKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.S:
                Run(SaveAnnotationAsync);
                return true;
            case Key.N:
                Run(NewAnnotationAsync);
                return true;
            default:
                return false;
        }
    }

    private async Task ReloadFilesAsync(long select = 0)
    {
        var items = await _repo.GetAttachmentsAsync(_evidence.EvidenceItemId);
        _files.ItemsSource = items;
        if (select > 0) _files.SelectedItem = items.FirstOrDefault(x => x.EvidenceAttachmentId == select);
        else if (items.Count > 0) _files.SelectedIndex = 0;

        if (items.Count == 0)
        {
            _annotations.ItemsSource = null;
            _integrity.Text = "No local source file attached.";
            ShowAnnotation(null);
        }
    }

    private async Task AttachAsync()
    {
        var path = await Files.OpenAsync(this, "Attach local PDF", Files.Pdf, AnyFile);
        if (path == null) return;
        await AttachFileAsync(path);
    }

    internal async Task AttachFileAsync(string path)
    {
        var existing = _files.ItemsSource as IEnumerable<EvidenceAttachment> ?? Array.Empty<EvidenceAttachment>();
        if (existing.Any(x => string.Equals(x.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
        {
            await Alert.Info(this, "Attach PDF", "That file is already attached.");
            return;
        }

        SetBusy(true);
        _status.Text = "Fingerprinting source file…";
        try
        {
            var f = await SourceFileFingerprinter.CreateAsync(path);
            var item = new EvidenceAttachment
            {
                EvidenceItemId = _evidence.EvidenceItemId,
                FilePath = f.FullPath,
                FileName = f.FileName,
                MediaType = "application/pdf",
                Sha256 = f.Sha256,
                FileSize = f.FileSize,
                FileModifiedUtc = f.ModifiedUtc
            };
            await _repo.SaveAttachmentAsync(item);
            await ReloadFilesAsync(item.EvidenceAttachmentId);
            _status.Text = "PDF attached; its fingerprint is recorded.";
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Could not attach PDF", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task FileChangedAsync()
    {
        var f = CurrentFile;
        if (f == null) return;

        _integrity.Text = File.Exists(f.FilePath)
            ? $"Recorded SHA-256: {f.Sha256[..Math.Min(16, f.Sha256.Length)]}…\nChoose Verify File to compare all bytes."
            : "Missing: the file is no longer at its recorded path.";
        await LoadAnnotationsAsync(f);
    }

    private async Task<List<EvidencePageAnnotation>> LoadAnnotationsAsync(EvidenceAttachment file, long select = 0)
    {
        var notes = await _repo.GetAnnotationsAsync(file.EvidenceAttachmentId);
        var rows = notes.Select(n => new NoteRow(n)).ToList();
        _loading = true;
        try
        {
            _annotations.ItemsSource = rows;
            Ui.Reveal(_annotations, (select > 0 ? rows.FirstOrDefault(r => r.Annotation.EvidencePageAnnotationId == select) : null) ?? rows.FirstOrDefault());
        }
        finally
        {
            _loading = false;
        }

        ShowAnnotation(CurrentAnnotation);
        return notes;
    }

    private async Task OpenFileAsync()
    {
        var f = CurrentFile;
        if (f == null) return;
        if (!File.Exists(f.FilePath))
        {
            await Alert.Info(this, "Open PDF", "The recorded file cannot be found.");
            return;
        }

        if (!MacShell.Open(f.FilePath)) await Alert.Warn(this, "Open PDF", "The file could not be opened.");
    }

    private async Task VerifyAsync()
    {
        var f = CurrentFile;
        if (f == null) return;

        SetBusy(true);
        try
        {
            var actual = await SourceFileFingerprinter.CreateAsync(f.FilePath);
            var same = actual.FileSize == f.FileSize && string.Equals(actual.Sha256, f.Sha256, StringComparison.OrdinalIgnoreCase);
            _integrity.Text = same
                ? "Verified: path, size, and SHA-256 match the attached source."
                : "Changed: this file no longer matches the recorded SHA-256 fingerprint.";
        }
        catch (Exception ex)
        {
            _integrity.Text = $"Unavailable: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RemoveFileAsync()
    {
        var f = CurrentFile;
        if (f == null) return;
        if (!await ConfirmAsync("Remove source link",
                $"Remove the link to {f.FileName} and all of its page notes? The PDF itself will not be deleted.", "Remove", AlertKind.Warning))
            return;

        await _repo.DeleteAttachmentAsync(f.EvidenceAttachmentId);
        await ReloadFilesAsync();
    }

    private async Task NewAnnotationAsync()
    {
        var f = CurrentFile;
        if (f == null)
        {
            await Alert.Info(this, "Page notes", "Attach or select a source file first.");
            return;
        }

        _loading = true;
        try
        {
            _annotations.SelectedItem = null;
        }
        finally
        {
            _loading = false;
        }

        ShowAnnotation(new EvidencePageAnnotation { EvidenceAttachmentId = f.EvidenceAttachmentId });
        _page.Focus();
    }

    private void ShowAnnotation(EvidencePageAnnotation? n)
    {
        _editing = n;
        _page.Text = (n?.PageNumber ?? 1).ToString();
        _quote.Text = n?.QuotedText ?? string.Empty;
        _note.Text = n?.Note ?? string.Empty;
        Ui.Select(_judgment, n?.Judgment ?? EvidenceJudgment.Uncertain);
    }

    internal async Task SaveAnnotationAsync()
    {
        var f = CurrentFile;
        if (f == null) return;

        if (!int.TryParse(_page.Text, out var page) || page < 1)
        {
            await Alert.Info(this, "Page notes", "Enter a PDF page number of 1 or greater.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_quote.Text) && string.IsNullOrWhiteSpace(_note.Text))
        {
            await Alert.Info(this, "Page notes", "Enter an exact quotation or a researcher note.");
            return;
        }

        var n = _editing ?? new EvidencePageAnnotation { EvidenceAttachmentId = f.EvidenceAttachmentId };
        n.PageNumber = page;
        n.QuotedText = Ui.Clean(_quote.Text);
        n.Note = Ui.Clean(_note.Text);
        n.Judgment = Ui.Value<EvidenceJudgment>(_judgment);
        await _repo.SaveAnnotationAsync(n);
        await LoadAnnotationsAsync(f, n.EvidencePageAnnotationId);
        _status.Text = $"Saved page {page} note.";
    }

    private async Task RemoveAnnotationAsync()
    {
        var n = CurrentAnnotation ?? _editing;
        if (n?.EvidencePageAnnotationId is not > 0) return;
        if (!await ConfirmAsync("Remove note", $"Remove the page {n.PageNumber} note?", "Remove", AlertKind.Warning)) return;

        await _repo.DeleteAnnotationAsync(n.EvidencePageAnnotationId);
        if (CurrentFile is { } f) await LoadAnnotationsAsync(f);
    }

    internal IReadOnlyList<EvidenceAttachment> Attachments => _files.ItemsSource as IReadOnlyList<EvidenceAttachment> ?? Array.Empty<EvidenceAttachment>();

    internal void DraftNote(int page, string quotation, string? note = null)
    {
        _page.Text = page.ToString();
        _quote.Text = quotation;
        _note.Text = note ?? string.Empty;
    }

    private sealed class NoteRow
    {
        public NoteRow(EvidencePageAnnotation note) => Annotation = note;

        public EvidencePageAnnotation Annotation { get; }
        public string Page => Annotation.PageNumber.ToString();
        public string Review => Words.Of(Annotation.Judgment);
        public string Quotation => Annotation.QuotedText ?? string.Empty;
        public string Note => Annotation.Note ?? string.Empty;
    }
}
