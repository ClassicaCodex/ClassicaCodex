using System.Diagnostics;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public sealed class EvidenceSourcesForm : ScaledForm
{
    private readonly EvidenceItem _evidence;
    private readonly ResearchSourceRepository _repo = new();
    private readonly ListBox _files = new();
    private readonly Label _integrity = new();
    private readonly DataGridView _annotations = new();
    private readonly TextBox _page = new();
    private readonly TextBox _quote = new();
    private readonly TextBox _note = new();
    private readonly ComboBox _judgment = new();
    private readonly Label _status = new();
    private EvidencePageAnnotation? _editing;
    private EvidenceAttachment? CurrentFile => _files.SelectedItem as EvidenceAttachment;
    private EvidencePageAnnotation? CurrentAnnotation => (_annotations.SelectedRows.Count>0?_annotations.SelectedRows[0]:_annotations.CurrentRow)?.DataBoundItem as EvidencePageAnnotation;

    public EvidenceSourcesForm(EvidenceItem evidence)
    {
        _evidence=evidence; Text=$"Source Files & Page Notes — {evidence.Title}"; Width=1050; Height=760;
        MinimumSize=new Size(800,600); StartPosition=FormStartPosition.CenterParent; AppIcons.ApplyWindowIcon(this,"WordStudy");
        var top=new Panel{Dock=DockStyle.Top,Height=150,Padding=new Padding(10)};
        _files.SetBounds(10,10,500,96); _files.SelectedIndexChanged+=async(_,_)=>await FileChangedAsync();
        var attach=Btn("Attach PDF…",525,10,105); attach.Click+=async(_,_)=>await AttachAsync();
        var open=Btn("Open PDF",638,10,90); open.Click+=(_,_)=>OpenFile();
        var verify=Btn("Verify file",736,10,90); verify.Click+=async(_,_)=>await VerifyAsync();
        var removeFile=Btn("Remove link",834,10,100); removeFile.Click+=async(_,_)=>await RemoveFileAsync();
        _integrity.SetBounds(525,52,475,54); _integrity.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        var notice=new Label{Text="Files are referenced in place and are not copied into the database.",Left=10,Top=116,Width=700,Height=22};
        top.Controls.AddRange(new Control[]{_files,attach,open,verify,removeFile,_integrity,notice});

        _annotations.Dock=DockStyle.Fill; _annotations.AutoGenerateColumns=false; _annotations.AllowUserToAddRows=false;
        _annotations.AllowUserToDeleteRows=false; _annotations.ReadOnly=true; _annotations.MultiSelect=false;
        _annotations.SelectionMode=DataGridViewSelectionMode.FullRowSelect; _annotations.RowHeadersVisible=false;
        _annotations.Columns.Add(Col("PageNumber","Page",60)); _annotations.Columns.Add(Col("Judgment","Review",80));
        _annotations.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName="QuotedText",HeaderText="Quotation",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        _annotations.Columns.Add(Col("Note","Researcher note",300));
        _annotations.SelectionChanged+=(_,_)=>ShowAnnotation(CurrentAnnotation);

        var editor=new Panel{Dock=DockStyle.Bottom,Height=245,Padding=new Padding(10)};
        editor.Controls.Add(Label("PDF page number",10,8)); _page.SetBounds(10,29,100,26); editor.Controls.Add(_page);
        editor.Controls.Add(Label("Human review",125,8)); _judgment.SetBounds(125,29,180,26); _judgment.DropDownStyle=ComboBoxStyle.DropDownList;
        _judgment.DataSource=Enum.GetValues<EvidenceJudgment>(); editor.Controls.Add(_judgment);
        editor.Controls.Add(Label("Exact quotation",10,62)); _quote.SetBounds(10,83,990,52); _quote.Multiline=true; _quote.ScrollBars=ScrollBars.Vertical;
        _quote.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right; editor.Controls.Add(_quote);
        editor.Controls.Add(Label("Researcher note / interpretation",10,141)); _note.SetBounds(10,162,990,42); _note.Multiline=true; _note.ScrollBars=ScrollBars.Vertical;
        _note.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right; editor.Controls.Add(_note);
        var fresh=Btn("New note",320,25,90); fresh.Click+=(_,_)=>NewAnnotation();
        var save=Btn("Save note",418,25,90); save.Click+=async(_,_)=>await SaveAnnotationAsync();
        var remove=Btn("Remove note",516,25,100); remove.Click+=async(_,_)=>await RemoveAnnotationAsync();
        _status.SetBounds(630,30,370,22); _status.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        editor.Controls.AddRange(new Control[]{fresh,save,remove,_status});
        Controls.Add(_annotations);Controls.Add(editor);Controls.Add(top);
        ReadingTheme.AttachTo(this,()=>{notice.ForeColor=ReadingTheme.MutedText;_integrity.ForeColor=ReadingTheme.MutedText;_status.ForeColor=ReadingTheme.MutedText;});
        WindowShortcuts.CloseOnEscape(this); Shown+=async(_,_)=>await ReloadFilesAsync();
    }

    private async Task ReloadFilesAsync(long select=0)
    {
        var items=await _repo.GetAttachmentsAsync(_evidence.EvidenceItemId);_files.DataSource=null;_files.DataSource=items;
        if(select>0)_files.SelectedItem=items.FirstOrDefault(x=>x.EvidenceAttachmentId==select);
        if(items.Count==0){_annotations.DataSource=null;_integrity.Text="No local source file attached.";ShowAnnotation(null);}
    }
    private async Task AttachAsync()
    {
        using var d=new OpenFileDialog{Title="Attach local PDF",Filter="PDF files (*.pdf)|*.pdf|All files (*.*)|*.*"};if(d.ShowDialog(this)!=DialogResult.OK)return;
        if(_files.Items.Cast<EvidenceAttachment>().Any(x=>string.Equals(x.FilePath,Path.GetFullPath(d.FileName),StringComparison.OrdinalIgnoreCase))){MessageBox.Show(this,"That file is already attached.");return;}
        Enabled=false;_status.Text="Fingerprinting source file…";
        try{var f=await SourceFileFingerprinter.CreateAsync(d.FileName);var item=new EvidenceAttachment{EvidenceItemId=_evidence.EvidenceItemId,FilePath=f.FullPath,FileName=f.FileName,MediaType="application/pdf",Sha256=f.Sha256,FileSize=f.FileSize,FileModifiedUtc=f.ModifiedUtc};await _repo.SaveAttachmentAsync(item);await ReloadFilesAsync(item.EvidenceAttachmentId);_status.Text="PDF attached; its fingerprint is recorded.";}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not attach PDF",MessageBoxButtons.OK,MessageBoxIcon.Error);}finally{Enabled=true;}
    }
    private async Task FileChangedAsync(){var f=CurrentFile;if(f==null)return;_integrity.Text=File.Exists(f.FilePath)?$"Recorded SHA-256: {f.Sha256[..Math.Min(16,f.Sha256.Length)]}…\nClick Verify file to compare all bytes.":"Missing: the file is no longer at its recorded path.";var notes=await _repo.GetAnnotationsAsync(f.EvidenceAttachmentId);_annotations.DataSource=notes;if(notes.Count==0)ShowAnnotation(null);}
    private void OpenFile(){var f=CurrentFile;if(f==null)return;if(!File.Exists(f.FilePath)){MessageBox.Show(this,"The recorded file cannot be found.");return;}try{Process.Start(new ProcessStartInfo(f.FilePath){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message);}}
    private async Task VerifyAsync(){var f=CurrentFile;if(f==null)return;Enabled=false;try{var actual=await SourceFileFingerprinter.CreateAsync(f.FilePath);var same=actual.FileSize==f.FileSize&&string.Equals(actual.Sha256,f.Sha256,StringComparison.OrdinalIgnoreCase);_integrity.Text=same?"Verified: path, size, and SHA-256 match the attached source.":"Changed: this file no longer matches the recorded SHA-256 fingerprint.";}catch(Exception ex){_integrity.Text=$"Unavailable: {ex.Message}";}finally{Enabled=true;}}
    private async Task RemoveFileAsync(){var f=CurrentFile;if(f==null)return;if(MessageBox.Show(this,$"Remove the link to {f.FileName} and all of its page notes? The PDF itself will not be deleted.","Remove source link",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;await _repo.DeleteAttachmentAsync(f.EvidenceAttachmentId);await ReloadFilesAsync();}
    private void NewAnnotation(){var f=CurrentFile;if(f==null){MessageBox.Show(this,"Attach or select a source file first.");return;}_annotations.ClearSelection();ShowAnnotation(new EvidencePageAnnotation{EvidenceAttachmentId=f.EvidenceAttachmentId});_page.Focus();}
    private void ShowAnnotation(EvidencePageAnnotation? n){_editing=n;_page.Text=(n?.PageNumber??1).ToString();_quote.Text=n?.QuotedText??"";_note.Text=n?.Note??"";_judgment.SelectedItem=n?.Judgment??EvidenceJudgment.Uncertain;}
    private async Task SaveAnnotationAsync(){var f=CurrentFile;if(f==null)return;if(!int.TryParse(_page.Text,out var page)||page<1){MessageBox.Show(this,"Enter a PDF page number of 1 or greater.");return;}if(string.IsNullOrWhiteSpace(_quote.Text)&&string.IsNullOrWhiteSpace(_note.Text)){MessageBox.Show(this,"Enter an exact quotation or a researcher note.");return;}var n=_editing??new EvidencePageAnnotation{EvidenceAttachmentId=f.EvidenceAttachmentId};n.PageNumber=page;n.QuotedText=Empty(_quote.Text);n.Note=Empty(_note.Text);n.Judgment=(EvidenceJudgment)_judgment.SelectedItem!;await _repo.SaveAnnotationAsync(n);await FileChangedAsync();foreach(DataGridViewRow row in _annotations.Rows)if(row.DataBoundItem is EvidencePageAnnotation x&&x.EvidencePageAnnotationId==n.EvidencePageAnnotationId){_annotations.CurrentCell=row.Cells[0];row.Selected=true;ShowAnnotation(x);break;}_status.Text=$"Saved page {page} note.";}
    private async Task RemoveAnnotationAsync(){var n=CurrentAnnotation??_editing;if(n?.EvidencePageAnnotationId is not >0)return;if(MessageBox.Show(this,$"Remove the page {n.PageNumber} note?","Remove note",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;await _repo.DeleteAnnotationAsync(n.EvidencePageAnnotationId);await FileChangedAsync();}
    private static Button Btn(string t,int x,int y,int w)=>new(){Text=t,Left=x,Top=y,Width=w,Height=28};
    private static Label Label(string t,int x,int y)=>new(){Text=t,Left=x,Top=y,Width=300,Height=20};
    private static DataGridViewTextBoxColumn Col(string p,string h,int w)=>new(){DataPropertyName=p,HeaderText=h,Width=w};
    private static string? Empty(string s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim();
}
