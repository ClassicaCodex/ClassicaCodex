using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public sealed class ResearchCorpusSnapshotsForm : Form
{
    private readonly ResearchProject _project;
    private readonly ResearchCorpusSnapshotRepository _repo=new();
    private readonly ListBox _snapshots=new();
    private readonly DataGridView _details=new();
    private readonly TextBox _name=new();
    private readonly TextBox _notes=new();
    private readonly ComboBox _scope=new();
    private readonly Label _status=new();
    private readonly Button _capture;
    private readonly Button _compare;
    private readonly Button _cancel;
    private CancellationTokenSource? _operation;
    private ResearchCorpusSnapshot? Current=>_snapshots.SelectedItem as ResearchCorpusSnapshot;

    public ResearchCorpusSnapshotsForm(ResearchProject project)
    {
        _project=project;Text=$"Corpus Snapshots — {project.Name}";Width=1180;Height=760;MinimumSize=new Size(850,560);
        StartPosition=FormStartPosition.CenterParent;AppIcons.ApplyWindowIcon(this,"Stylometry");
        var capturePanel=new Panel{Dock=DockStyle.Top,Height=116,Padding=new Padding(10)};
        capturePanel.Controls.Add(Label("Snapshot name",10,7,180));_name.SetBounds(10,28,300,26);capturePanel.Controls.Add(_name);
        capturePanel.Controls.Add(Label("Corpus scope",325,7,160));_scope.SetBounds(325,28,210,26);_scope.DropDownStyle=ComboBoxStyle.DropDownList;
        _scope.DataSource=new[]{new ScopeChoice(CorpusSnapshotScope.ProjectWork,"This work only"),new ScopeChoice(CorpusSnapshotScope.SameAuthor,"All works by this author"),new ScopeChoice(CorpusSnapshotScope.EntireCorpus,"Entire installed corpus")};capturePanel.Controls.Add(_scope);
        capturePanel.Controls.Add(Label("Notes / purpose",550,7,180));_notes.SetBounds(550,28,430,52);_notes.Multiline=true;_notes.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;capturePanel.Controls.Add(_notes);
        _capture=Btn("Capture snapshot",10,70,125);_capture.Click+=async(_,_)=>await CaptureAsync();
        _cancel=Btn("Cancel",145,70,80);_cancel.Enabled=false;_cancel.Click+=(_,_)=>_operation?.Cancel();
        _status.SetBounds(240,75,925,24);_status.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        capturePanel.Controls.AddRange(new Control[]{_capture,_cancel,_status});

        var split=new SplitContainer{Dock=DockStyle.Fill};
        var toolbar=new Panel{Dock=DockStyle.Top,Height=46,Padding=new Padding(8)};
        _compare=Btn("Compare with current",8,8,145);_compare.Click+=async(_,_)=>await CompareAsync();
        var remove=Btn("Remove",163,8,85);remove.Click+=async(_,_)=>await RemoveAsync();
        toolbar.Controls.AddRange(new Control[]{_compare,remove});_snapshots.Dock=DockStyle.Fill;_snapshots.SelectedIndexChanged+=async(_,_)=>await ShowSnapshotAsync();
        split.Panel1.Controls.Add(_snapshots);split.Panel1.Controls.Add(toolbar);
        _details.Dock=DockStyle.Fill;_details.AutoGenerateColumns=false;_details.AllowUserToAddRows=false;_details.AllowUserToDeleteRows=false;
        _details.ReadOnly=true;_details.RowHeadersVisible=false;_details.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        _details.Columns.Add(Col("Status","Status",82));_details.Columns.Add(Col("Work","Work",230));_details.Columns.Add(Col("Edition","Edition / CTS URN",310));
        _details.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName="Details",HeaderText="Frozen state / comparison",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        split.Panel2.Controls.Add(_details);Controls.Add(split);Controls.Add(capturePanel);
        ReadingTheme.AttachTo(this,()=>_status.ForeColor=ReadingTheme.MutedText);WindowShortcuts.CloseOnEscape(this);
        Shown+=async(_,_)=>
        {
            if(split.ClientSize.Width-split.SplitterWidth>=650)
            {
                split.Panel1MinSize=250;split.Panel2MinSize=400;
                split.SplitterDistance=Math.Clamp(350,split.Panel1MinSize,split.ClientSize.Width-split.SplitterWidth-split.Panel2MinSize);
            }
            _name.Text=$"Corpus state {DateTime.Now:yyyy-MM-dd HHmm}";await ReloadAsync();
        };
        FormClosing+=(_,e)=>{if(_operation==null)return;_operation.Cancel();e.Cancel=true;_status.Text="Cancelling the corpus operation…";};
    }

    private async Task ReloadAsync(long select=0)
    {
        var items=await _repo.GetSnapshotsAsync(_project.ResearchProjectId);_snapshots.DataSource=null;_snapshots.DataSource=items;
        if(select>0)_snapshots.SelectedItem=items.FirstOrDefault(x=>x.ResearchCorpusSnapshotId==select);
        if(items.Count==0){_details.DataSource=null;_status.Text="No frozen corpus state yet.";}
    }
    private async Task CaptureAsync()
    {
        if(string.IsNullOrWhiteSpace(_name.Text)){MessageBox.Show(this,"Enter a snapshot name.");return;}
        var scope=((ScopeChoice)_scope.SelectedItem!).Scope;
        if(scope==CorpusSnapshotScope.EntireCorpus&&MessageBox.Show(this,"Hash every installed edition? This can take time on a large corpus.","Capture entire corpus",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        await RunAsync(async token=>
        {
            var progress=new Progress<CorpusSnapshotProgress>(p=>_status.Text=p.Total==0?"Reading corpus…":$"Fingerprinting {p.Completed:N0}/{p.Total:N0}: {p.CurrentWork}");
            var snapshot=await _repo.CaptureAsync(_project.ResearchProjectId,_name.Text,scope,Application.ProductVersion,_notes.Text,progress,token);
            await ReloadAsync(snapshot.ResearchCorpusSnapshotId);_name.Text=$"Corpus state {DateTime.Now:yyyy-MM-dd HHmm}";_notes.Clear();
            _status.Text=$"Captured {snapshot.WorkCount} works, {snapshot.EditionCount} editions, and {snapshot.TextNodeCount:N0} ordered text nodes.";
        });
    }
    private async Task CompareAsync()
    {
        var snapshot=Current;if(snapshot==null)return;await RunAsync(async token=>
        {
            var progress=new Progress<CorpusSnapshotProgress>(p=>_status.Text=p.Total==0?"Reading corpus…":$"Comparing {p.Completed:N0}/{p.Total:N0}: {p.CurrentWork}");
            var result=await _repo.CompareAsync(snapshot,progress,token);
            _details.DataSource=result.Differences.Select(d=>new DisplayRow(d.Status,d.Work,d.Edition,d.Details)).ToList();
            _status.Text=result.Differences.Count==0?$"Exact match: {result.Unchanged} frozen entries are unchanged.":$"Compared: {result.Unchanged} unchanged, {result.Changed} changed, {result.Added} added, {result.Missing} missing.";
        });
    }
    private async Task ShowSnapshotAsync()
    {
        var snapshot=Current;if(snapshot==null)return;var entries=await _repo.GetEntriesAsync(snapshot.ResearchCorpusSnapshotId);
        _details.DataSource=entries.Select(e=>new DisplayRow(e.AttributionStatus,e.AuthorName+" — "+e.WorkTitle,e.EditionCtsUrn??"(no edition)",
            e.EditionCtsUrn==null?"No installed edition":$"{e.EditionKind}; {e.Language??"unknown language"}; {e.TextNodeCount:N0} nodes; SHA-256 {e.ContentSha256?[..Math.Min(16,e.ContentSha256.Length)]}…" )).ToList();
        _status.Text=$"{snapshot.Name}: {snapshot.Scope}; {snapshot.WorkCount} works; {snapshot.EditionCount} editions; app {snapshot.AppVersion}; captured {snapshot.CreatedUtc.ToLocalTime():g}.";
    }
    private async Task RemoveAsync(){var snapshot=Current;if(snapshot==null)return;if(MessageBox.Show(this,$"Remove the frozen corpus snapshot '{snapshot.Name}'?","Remove snapshot",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;await _repo.DeleteAsync(snapshot.ResearchCorpusSnapshotId);await ReloadAsync();}
    private async Task RunAsync(Func<CancellationToken,Task> action)
    {
        _operation=new CancellationTokenSource();_capture.Enabled=false;_compare.Enabled=false;_cancel.Enabled=true;
        try{await action(_operation.Token);}catch(OperationCanceledException){_status.Text="Operation cancelled; no partial snapshot was saved.";}catch(Exception ex){MessageBox.Show(this,ex.Message,"Corpus snapshot",MessageBoxButtons.OK,MessageBoxIcon.Error);_status.Text="The operation did not complete.";}finally{_operation.Dispose();_operation=null;_capture.Enabled=true;_compare.Enabled=true;_cancel.Enabled=false;}
    }
    private static Button Btn(string text,int x,int y,int width)=>new(){Text=text,Left=x,Top=y,Width=width,Height=29};
    private static Label Label(string text,int x,int y,int width)=>new(){Text=text,Left=x,Top=y,Width=width,Height=20};
    private static DataGridViewTextBoxColumn Col(string property,string header,int width)=>new(){DataPropertyName=property,HeaderText=header,Width=width};
    private sealed record ScopeChoice(CorpusSnapshotScope Scope,string Text){public override string ToString()=>Text;}
    private sealed record DisplayRow(string Status,string Work,string Edition,string Details);
}
