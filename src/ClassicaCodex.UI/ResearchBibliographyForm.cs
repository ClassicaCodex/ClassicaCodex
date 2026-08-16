using System.Text;
using System.Text.RegularExpressions;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>Project bibliography, citekey editor, and offline Zotero-compatible export.</summary>
public sealed partial class ResearchBibliographyForm : ScaledForm
{
    private readonly ResearchProject _project;
    private readonly ResearchBibliographyRepository _repo = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _preview = new();
    private readonly Label _status = new();
    private List<Row> _rows = [];

    public ResearchBibliographyForm(ResearchProject project)
    {
        _project=project;Text=$"Bibliography & Zotero Export — {project.Name}";Width=1120;Height=720;
        MinimumSize=new Size(820,520);StartPosition=FormStartPosition.CenterParent;AppIcons.ApplyWindowIcon(this,"WordStudy");
        var header=new Panel{Dock=DockStyle.Top,Height=78,Padding=new Padding(10)};
        var intro=new Label{Text="Scholarship evidence retains structured citation metadata. Edit citekeys, select records, then export for Zotero or another reference manager.",Left=10,Top=10,Width=1060,Height=22,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};
        var save=Btn("Save citekeys",10,39,110);save.Click+=async(_,_)=>await SaveKeysAsync(showConfirmation:true);
        var bib=Btn("Export BibTeX…",130,39,125);bib.Click+=async(_,_)=>await ExportAsync("BibTeX");
        var ris=Btn("Export RIS…",265,39,105);ris.Click+=async(_,_)=>await ExportAsync("RIS");
        _status.SetBounds(390,45,680,22);_status.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        header.Controls.AddRange(new Control[]{intro,save,bib,ris,_status});

        _grid.Dock=DockStyle.Fill;_grid.AutoGenerateColumns=false;_grid.AllowUserToAddRows=false;_grid.AllowUserToDeleteRows=false;
        _grid.MultiSelect=false;_grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;_grid.RowHeadersVisible=false;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn{DataPropertyName="Include",HeaderText="Export",Width=58});
        _grid.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName="CiteKey",HeaderText="Citekey",Width=155});
        _grid.Columns.Add(Col("Authors","Author(s)",220));_grid.Columns.Add(Col("Year","Year",62));
        _grid.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName="Title",HeaderText="Title",ReadOnly=true,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        _grid.Columns.Add(Col("EntryType","Type",82));
        foreach(DataGridViewColumn c in _grid.Columns)if(c.Index>1)c.ReadOnly=true;
        _grid.SelectionChanged+=(_,_)=>ShowPreview();_grid.CellEndEdit+=(_,_)=>ShowPreview();

        _preview.Dock=DockStyle.Bottom;_preview.Height=180;_preview.Multiline=true;_preview.ReadOnly=true;
        _preview.ScrollBars=ScrollBars.Both;_preview.WordWrap=false;_preview.Font=new Font(FontFamily.GenericMonospace,9);
        Controls.Add(_grid);Controls.Add(_preview);Controls.Add(header);
        ReadingTheme.AttachTo(this,()=>{intro.ForeColor=ReadingTheme.MutedText;_status.ForeColor=ReadingTheme.MutedText;});
        WindowShortcuts.CloseOnEscape(this);Shown+=async(_,_)=>await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var metadata=await _repo.GetForProjectAsync(_project.ResearchProjectId);
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _rows=metadata.Select(m=>
        {
            var baseKey=string.IsNullOrWhiteSpace(m.CiteKey)?BibliographyExport.SuggestCiteKey(m.ToRecord()):m.CiteKey!;
            var key=baseKey;for(var suffix=2;!used.Add(key);suffix++)key=baseKey+suffix;
            m.CiteKey=key;return new Row(m);
        }).ToList();
        _grid.DataSource=_rows;_status.Text=_rows.Count==0?"No scholarship evidence is available to export.":$"{_rows.Count} scholarship source(s); generated citekeys remain editable.";
        ShowPreview();
    }

    private async Task<bool> SaveKeysAsync(bool showConfirmation)
    {
        _grid.EndEdit();
        var invalid=_rows.FirstOrDefault(r=>string.IsNullOrWhiteSpace(r.CiteKey)||!CiteKeyPattern().IsMatch(r.CiteKey));
        if(invalid!=null){MessageBox.Show(this,$"The citekey for '{invalid.Title}' contains unsupported characters.","Invalid citekey",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
        var duplicate=_rows.GroupBy(r=>r.CiteKey,StringComparer.OrdinalIgnoreCase).FirstOrDefault(g=>g.Count()>1);
        if(duplicate!=null){MessageBox.Show(this,$"The citekey '{duplicate.Key}' is used more than once.","Duplicate citekey",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
        foreach(var row in _rows){row.Metadata.CiteKey=row.CiteKey.Trim();await _repo.SaveAsync(row.Metadata);}
        if(showConfirmation)_status.Text=$"Saved {_rows.Count} citekey(s) and structured citation record(s).";
        return true;
    }

    private async Task ExportAsync(string format)
    {
        if(!await SaveKeysAsync(showConfirmation:false))return;
        var selected=_rows.Where(r=>r.Include).Select(r=>r.Metadata.ToRecord()).ToList();
        if(selected.Count==0){MessageBox.Show(this,"Select at least one bibliography record to export.");return;}
        var bib=format=="BibTeX";using var dialog=new SaveFileDialog{Title=$"Export {format} bibliography",Filter=bib?"BibTeX (*.bib)|*.bib":"RIS (*.ris)|*.ris",DefaultExt=bib?"bib":"ris",FileName=SafeName(_project.Name)+(bib?".bib":".ris")};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var text=bib?BibliographyExport.ToBibTeX(selected):BibliographyExport.ToRis(selected);
        await File.WriteAllTextAsync(dialog.FileName,text,new UTF8Encoding(false));
        await new ResearchRepository().AddSystemResearchLogEntryAsync(new ResearchLogEntry{ResearchProjectId=_project.ResearchProjectId,Kind=ResearchLogEntryKind.BibliographyExported,Summary=$"Exported {selected.Count} source(s) as {format}",Details=Path.GetFileName(dialog.FileName)});
        _status.Text=$"Exported {selected.Count} source(s) to {Path.GetFileName(dialog.FileName)}.";
    }

    private void ShowPreview()
    {
        if(_grid.CurrentRow?.DataBoundItem is not Row row){_preview.Clear();return;}
        row.Metadata.CiteKey=row.CiteKey;
        _preview.Text=BibliographyExport.ToBibTeX([row.Metadata.ToRecord()]);
    }
    private static Button Btn(string text,int x,int y,int width)=>new(){Text=text,Left=x,Top=y,Width=width,Height=29};
    private static DataGridViewTextBoxColumn Col(string property,string header,int width)=>new(){DataPropertyName=property,HeaderText=header,Width=width,ReadOnly=true};
    private static string SafeName(string value){var invalid=Path.GetInvalidFileNameChars();var result=new string(value.Select(c=>invalid.Contains(c)?'_':c).ToArray()).Trim();return string.IsNullOrWhiteSpace(result)?"bibliography":result;}
    [GeneratedRegex(@"^[\p{L}\p{N}_:.+\-]+$")]
    private static partial Regex CiteKeyPattern();

    private sealed class Row
    {
        public Row(EvidenceBibliographyMetadata metadata){Metadata=metadata;CiteKey=metadata.CiteKey??"";}
        public EvidenceBibliographyMetadata Metadata{get;}public bool Include{get;set;}=true;public string CiteKey{get;set;}
        public string Authors=>Metadata.Authors.Count==0?"—":string.Join("; ",Metadata.Authors);public string Year=>Metadata.Year??"—";
        public string Title=>Metadata.Title;public string EntryType=>Metadata.EntryType;
    }
}
