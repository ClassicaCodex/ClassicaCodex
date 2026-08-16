using System.ComponentModel;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>Compares rival explanations against the saved record and turns uncertainty into explicit tests.</summary>
public sealed class HypothesisLabForm : Form
{
    private readonly ResearchProject _project; private readonly Work _work; private readonly string _author;
    private readonly ResearchHypothesisRepository _repo=new(); private readonly ResearchRepository _research=new();
    private readonly ListBox _hypotheses=new(); private readonly TextBox _title=new(),_statement=new(),_hypothesisNote=new();
    private readonly ComboBox _hypothesisStatus=new(){DropDownStyle=ComboBoxStyle.DropDownList}; private readonly Label _origin=new();
    private readonly DataGridView _overview=new(),_matrix=new(),_experiments=new(); private BindingList<MatrixRow> _matrixRows=[];
    private readonly TextBox _experimentTitle=new(),_predicted=new(),_falsification=new(),_experimentNote=new();
    private readonly ComboBox _method=new(){DropDownStyle=ComboBoxStyle.DropDownList},_experimentStatus=new(){DropDownStyle=ComboBoxStyle.DropDownList},_experimentHypothesis=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly Label _status=new(); private ResearchHypothesis? _editingHypothesis; private ResearchExperiment? _editingExperiment;
    private List<ResearchHypothesis> _hypothesisData=[]; private List<HypothesisSource> _sources=[]; private List<ResearchExperiment> _experimentData=[]; private bool _loading;

    public HypothesisLabForm(ResearchProject project,Work work,string author)
    {
        _project=project;_work=work;_author=author;Text=$"Hypothesis Lab — {project.Name}";Width=1500;Height=880;MinimumSize=new Size(1120,680);StartPosition=FormStartPosition.CenterParent;AppIcons.ApplyWindowIcon(this,"WordStudy");
        var header=new Label{Dock=DockStyle.Top,Height=50,Padding=new Padding(10,8,6,0),Text="Compare explanations, state what would count against them, and distinguish diagnostic evidence from material that fits every theory."};
        var split=new SplitContainer{Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1};BuildLeft(split.Panel1);BuildRight(split.Panel2);
        _status.Dock=DockStyle.Bottom;_status.Height=26;_status.Padding=new Padding(8,4,0,0);Controls.Add(split);Controls.Add(header);Controls.Add(_status);
        ReadingTheme.AttachTo(this,()=>{header.ForeColor=ReadingTheme.MutedText;_origin.ForeColor=ReadingTheme.MutedText;_status.ForeColor=ReadingTheme.MutedText;});WindowShortcuts.CloseOnEscape(this);
        Shown+=async(_,_)=>{var max=split.ClientSize.Width-820-split.SplitterWidth;if(max>=280){split.SplitterDistance=Math.Clamp(340,280,max);split.Panel1MinSize=280;split.Panel2MinSize=820;}await ReloadAsync();};
    }

    private void BuildLeft(Control host)
    {
        var tools=new FlowLayoutPanel{Dock=DockStyle.Top,Height=82,Padding=new Padding(5)};var add=Btn("New",65);var remove=Btn("Remove",72);var challenge=Btn("AI challenge…",115);
        add.Click+=(_,_)=>NewHypothesis();remove.Click+=async(_,_)=>await RemoveHypothesisAsync();challenge.Click+=async(_,_)=>await ChallengeAsync();tools.Controls.AddRange([add,remove,challenge]);
        _hypotheses.Dock=DockStyle.Fill;_hypotheses.SelectedIndexChanged+=async(_,_)=>{if(!_loading)await SelectHypothesisAsync(_hypotheses.SelectedItem as ResearchHypothesis);};host.Controls.Add(_hypotheses);host.Controls.Add(tools);
    }

    private void BuildRight(Control host)
    {
        var tabs=new TabControl{Dock=DockStyle.Fill};var overview=new TabPage("Comparison overview");var details=new TabPage("Hypothesis");var matrix=new TabPage("Assess selected hypothesis");var experiments=new TabPage("Falsification experiments");BuildOverview(overview);BuildDetails(details);BuildMatrix(matrix);BuildExperiments(experiments);tabs.TabPages.AddRange([overview,details,matrix,experiments]);host.Controls.Add(tabs);
    }

    private void BuildOverview(Control host)
    {
        var note=new Label{Dock=DockStyle.Top,Height=48,Padding=new Padding(8,6,5,0),Text="Each hypothesis is a column. Blank means not yet assessed; ‘does not discriminate’ records material that fits the rival explanations equally well."};
        _overview.Dock=DockStyle.Fill;_overview.ReadOnly=true;_overview.AllowUserToAddRows=false;_overview.RowHeadersVisible=false;_overview.AutoGenerateColumns=false;_overview.SelectionMode=DataGridViewSelectionMode.FullRowSelect;host.Controls.Add(_overview);host.Controls.Add(note);
    }

    private void BuildDetails(Control host)
    {
        var p=new Panel{Dock=DockStyle.Fill,AutoScroll=true};var y=14;Area(p,"Short title",_title,28,ref y);Area(p,"Testable statement",_statement,110,ref y);
        p.Controls.Add(new Label{Text="Researcher status",Left=10,Top=y,Width=180});_hypothesisStatus.SetBounds(10,y+22,260,28);_hypothesisStatus.DataSource=Enum.GetValues<ResearchHypothesisStatus>();p.Controls.Add(_hypothesisStatus);
        _origin.SetBounds(290,y+26,600,24);p.Controls.Add(_origin);y+=62;Area(p,"Researcher note",_hypothesisNote,180,ref y);var save=Btn("Save hypothesis",125);save.SetBounds(10,y,125,30);save.Click+=async(_,_)=>await SaveHypothesisAsync();p.Controls.Add(save);host.Controls.Add(p);
    }

    private void BuildMatrix(Control host)
    {
        var note=new Label{Dock=DockStyle.Top,Height=48,Padding=new Padding(8,6,5,0),Text="Link only sources you have weighed. “Does not discriminate” is important: it records evidence compatible with several rivals without pretending it favors one."};
        _matrix.Dock=DockStyle.Fill;_matrix.AutoGenerateColumns=false;_matrix.AllowUserToAddRows=false;_matrix.RowHeadersVisible=false;_matrix.DataSource=_matrixRows;
        _matrix.Columns.Add(new DataGridViewCheckBoxColumn{DataPropertyName=nameof(MatrixRow.Linked),HeaderText="Assess",Width=52});
        _matrix.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(MatrixRow.Kind),HeaderText="Kind",Width=95,ReadOnly=true});
        _matrix.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(MatrixRow.Title),HeaderText="Source",Width=220,ReadOnly=true});
        _matrix.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(MatrixRow.Review),HeaderText="Review",Width=78,ReadOnly=true});
        _matrix.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(MatrixRow.Detail),HeaderText="Recorded material",Width=260,ReadOnly=true});
        _matrix.Columns.Add(new DataGridViewComboBoxColumn{DataPropertyName=nameof(MatrixRow.Relationship),HeaderText="Bearing",Width=145,DataSource=Enum.GetValues<HypothesisRelationship>()});
        _matrix.Columns.Add(new DataGridViewComboBoxColumn{DataPropertyName=nameof(MatrixRow.Strength),HeaderText="Strength",Width=90,DataSource=Enum.GetValues<HypothesisStrength>()});
        _matrix.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(MatrixRow.Note),HeaderText="Researcher assessment",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=46,Padding=new Padding(6)};var save=Btn("Save assessments",130);save.Click+=async(_,_)=>await SaveAssessmentsAsync();bottom.Controls.Add(save);host.Controls.Add(_matrix);host.Controls.Add(note);host.Controls.Add(bottom);
    }

    private void BuildExperiments(Control host)
    {
        var split=new SplitContainer{Dock=DockStyle.Fill};_experiments.Dock=DockStyle.Fill;_experiments.AutoGenerateColumns=false;_experiments.ReadOnly=true;_experiments.AllowUserToAddRows=false;_experiments.RowHeadersVisible=false;_experiments.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        _experiments.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(ExperimentRow.Status),HeaderText="Status",Width=82});_experiments.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(ExperimentRow.Method),HeaderText="Method",Width=120});_experiments.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(ExperimentRow.Title),HeaderText="Experiment",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        _experiments.SelectionChanged+=(_,_)=>{if(!_loading)ShowExperiment((_experiments.CurrentRow?.DataBoundItem as ExperimentRow)?.Experiment);};
        var listTools=new FlowLayoutPanel{Dock=DockStyle.Top,Height=45,Padding=new Padding(5)};var add=Btn("New",65);var remove=Btn("Remove",72);add.Click+=(_,_)=>ShowExperiment(new ResearchExperiment{ResearchProjectId=_project.ResearchProjectId,SortOrder=_experimentData.Count});remove.Click+=async(_,_)=>await RemoveExperimentAsync();listTools.Controls.AddRange([add,remove]);split.Panel1.Controls.Add(_experiments);split.Panel1.Controls.Add(listTools);
        var editor=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(8)};var y=10;Area(editor,"Experiment",_experimentTitle,28,ref y);ComboPair(editor,"Method",_method,Enum.GetValues<ResearchExperimentMethod>(),"Status",_experimentStatus,Enum.GetValues<ResearchExperimentStatus>(),ref y);editor.Controls.Add(new Label{Text="Tests hypothesis",Left=10,Top=y,Width=350});_experimentHypothesis.SetBounds(10,y+22,700,28);editor.Controls.Add(_experimentHypothesis);y+=62;Area(editor,"Predicted result if the linked explanation is right",_predicted,90,ref y);Area(editor,"Result that would count against it",_falsification,90,ref y);Area(editor,"Researcher note",_experimentNote,90,ref y);
        var save=Btn("Save experiment",125);var open=Btn("Open method tool",125);save.SetBounds(10,y,125,30);open.SetBounds(145,y,125,30);save.Click+=async(_,_)=>await SaveExperimentAsync();open.Click+=(_,_)=>OpenMethod();editor.Controls.Add(save);editor.Controls.Add(open);split.Panel2.Controls.Add(editor);host.Controls.Add(split);
        var configured=false;split.SizeChanged+=(_,_)=>{if(configured)return;var max=split.ClientSize.Width-500-split.SplitterWidth;if(max>=260){split.SplitterDistance=Math.Clamp(360,260,max);split.Panel1MinSize=260;split.Panel2MinSize=500;configured=true;}};
    }

    private async Task ReloadAsync(long selectHypothesis=0,long selectExperiment=0)
    {
        _hypothesisData=await _repo.GetHypothesesAsync(_project.ResearchProjectId);_sources=await _repo.GetSourcesAsync(_project.ResearchProjectId);_experimentData=await _repo.GetExperimentsAsync(_project.ResearchProjectId);_loading=true;
        _hypotheses.DataSource=null;_hypotheses.DataSource=_hypothesisData;if(selectHypothesis>0)_hypotheses.SelectedItem=_hypothesisData.FirstOrDefault(h=>h.ResearchHypothesisId==selectHypothesis);
        _experimentHypothesis.DataSource=null;_experimentHypothesis.DataSource=new[]{new HypothesisChoice(null,"(project-wide)")}.Concat(_hypothesisData.Select(h=>new HypothesisChoice(h.ResearchHypothesisId,h.Title))).ToList();
        var erows=_experimentData.Select(e=>new ExperimentRow(e)).ToList();_experiments.DataSource=erows;if(selectExperiment>0){var row=erows.FirstOrDefault(r=>r.Experiment.ResearchExperimentId==selectExperiment);if(row!=null)_experiments.CurrentCell=_experiments.Rows[erows.IndexOf(row)].Cells[0];}
        _loading=false;await SelectHypothesisAsync(_hypotheses.SelectedItem as ResearchHypothesis);await BuildOverviewAsync();ShowExperiment((_experiments.CurrentRow?.DataBoundItem as ExperimentRow)?.Experiment);_status.Text=$"{_hypothesisData.Count} hypothesis/hypotheses · {_sources.Count} available research source(s) · {_experimentData.Count} experiment(s).";
    }

    private async Task BuildOverviewAsync()
    {
        _overview.Columns.Clear();_overview.Rows.Clear();_overview.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Kind",Width=90});_overview.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Research source",Width=260});
        var assessments=new Dictionary<long,Dictionary<(HypothesisSourceKind,long),ResearchHypothesisAssessment>>();
        foreach(var hypothesis in _hypothesisData){_overview.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=hypothesis.Title,Width=165});assessments[hypothesis.ResearchHypothesisId]=(await _repo.GetAssessmentsAsync(hypothesis.ResearchHypothesisId)).ToDictionary(a=>(a.SourceKind,a.SourceId));}
        foreach(var source in _sources){var values=new object?[2+_hypothesisData.Count];values[0]=source.Kind.ToString();values[1]=source.Title;for(var i=0;i<_hypothesisData.Count;i++){if(assessments[_hypothesisData[i].ResearchHypothesisId].TryGetValue((source.Kind,source.Id),out var a))values[i+2]=$"{a.Relationship} · {a.Strength}";}var row=_overview.Rows[_overview.Rows.Add(values)];row.Cells[1].ToolTipText=source.Detail;}
    }

    private void NewHypothesis(){_hypotheses.ClearSelected();_editingHypothesis=new ResearchHypothesis{ResearchProjectId=_project.ResearchProjectId,SortOrder=_hypothesisData.Count};PopulateHypothesis();}
    private async Task SelectHypothesisAsync(ResearchHypothesis? hypothesis){_editingHypothesis=hypothesis;PopulateHypothesis();var links=hypothesis==null?[]:await _repo.GetAssessmentsAsync(hypothesis.ResearchHypothesisId);var by=links.ToDictionary(a=>(a.SourceKind,a.SourceId));_matrixRows=new BindingList<MatrixRow>(_sources.Select(s=>{by.TryGetValue((s.Kind,s.Id),out var a);return new MatrixRow(s,a);}).ToList());_matrix.DataSource=_matrixRows;}
    private void PopulateHypothesis(){var h=_editingHypothesis;_title.Text=h?.Title??"";_statement.Text=h?.Statement??"";_hypothesisStatus.SelectedItem=h?.Status??ResearchHypothesisStatus.Active;_hypothesisNote.Text=h?.ResearcherNote??"";_origin.Text=h==null?"New researcher-authored hypothesis":h.Origin==EvidenceOrigin.AiCandidate?$"Accepted AI proposal · {h.AiModel} · {h.AiGeneratedUtc?.ToLocalTime():g}":"Researcher-authored hypothesis";}
    private async Task SaveHypothesisAsync(){if(_editingHypothesis==null)return;if(string.IsNullOrWhiteSpace(_title.Text)||string.IsNullOrWhiteSpace(_statement.Text)){MessageBox.Show(this,"Enter a title and a testable statement.");return;}_editingHypothesis.Title=_title.Text.Trim();_editingHypothesis.Statement=_statement.Text.Trim();_editingHypothesis.Status=(ResearchHypothesisStatus)_hypothesisStatus.SelectedItem!;_editingHypothesis.ResearcherNote=Clean(_hypothesisNote.Text);await _repo.SaveHypothesisAsync(_editingHypothesis);await ReloadAsync(_editingHypothesis.ResearchHypothesisId);}
    private async Task RemoveHypothesisAsync(){if(_editingHypothesis?.ResearchHypothesisId is not>0)return;if(MessageBox.Show(this,$"Remove “{_editingHypothesis.Title}”? Its assessments will be removed and linked experiments will become project-wide.","Remove hypothesis",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;await _repo.DeleteHypothesisAsync(_editingHypothesis.ResearchHypothesisId);await ReloadAsync();}
    private async Task SaveAssessmentsAsync(){if(_editingHypothesis?.ResearchHypothesisId is not>0){MessageBox.Show(this,"Save a hypothesis before assessing sources.");return;}_matrix.EndEdit();var links=_matrixRows.Where(r=>r.Linked).Select(r=>new ResearchHypothesisAssessment{ResearchHypothesisId=_editingHypothesis.ResearchHypothesisId,SourceKind=r.Source.Kind,SourceId=r.Source.Id,Relationship=r.Relationship,Strength=r.Strength,ResearcherNote=Clean(r.Note)}).ToList();await _repo.SaveAssessmentsAsync(_editingHypothesis.ResearchHypothesisId,links);await BuildOverviewAsync();_status.Text=$"Saved {links.Count} source assessment(s) for {_editingHypothesis.Title}.";}

    private void ShowExperiment(ResearchExperiment? experiment){_editingExperiment=experiment;if(experiment==null){_experimentTitle.Text=_predicted.Text=_falsification.Text=_experimentNote.Text="";return;}_experimentTitle.Text=experiment.Title;_method.SelectedItem=experiment.Method;_experimentStatus.SelectedItem=experiment.Status;_predicted.Text=experiment.PredictedOutcome??"";_falsification.Text=experiment.FalsificationCriterion??"";_experimentNote.Text=experiment.ResearcherNote??"";if(_experimentHypothesis.DataSource is IEnumerable<HypothesisChoice> choices)_experimentHypothesis.SelectedItem=choices.FirstOrDefault(c=>c.Id==experiment.ResearchHypothesisId);}
    private async Task SaveExperimentAsync(){if(_editingExperiment==null)return;if(string.IsNullOrWhiteSpace(_experimentTitle.Text)){MessageBox.Show(this,"Give the experiment a title.");return;}_editingExperiment.Title=_experimentTitle.Text.Trim();_editingExperiment.Method=(ResearchExperimentMethod)_method.SelectedItem!;_editingExperiment.Status=(ResearchExperimentStatus)_experimentStatus.SelectedItem!;_editingExperiment.ResearchHypothesisId=(_experimentHypothesis.SelectedItem as HypothesisChoice)?.Id;_editingExperiment.PredictedOutcome=Clean(_predicted.Text);_editingExperiment.FalsificationCriterion=Clean(_falsification.Text);_editingExperiment.ResearcherNote=Clean(_experimentNote.Text);await _repo.SaveExperimentAsync(_editingExperiment);await ReloadAsync(_editingHypothesis?.ResearchHypothesisId??0,_editingExperiment.ResearchExperimentId);}
    private async Task RemoveExperimentAsync(){if(_editingExperiment?.ResearchExperimentId is not>0)return;if(MessageBox.Show(this,$"Remove experiment “{_editingExperiment.Title}”?","Remove experiment",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;await _repo.DeleteExperimentAsync(_editingExperiment.ResearchExperimentId);await ReloadAsync(_editingHypothesis?.ResearchHypothesisId??0);}
    private void OpenMethod(){if(_editingExperiment==null)return;switch(_editingExperiment.Method){case ResearchExperimentMethod.Stylometry:using(var f=new StylometryForm())f.ShowDialog(this);break;case ResearchExperimentMethod.CorpusInvestigator:case ResearchExperimentMethod.ParallelStudio:using(var f=new IntertextualAtlasForm(_project))f.ShowDialog(this);break;case ResearchExperimentMethod.Bibliography:using(var f=new ResearchBibliographyForm(_project))f.ShowDialog(this);break;case ResearchExperimentMethod.ReadingQueue:using(var f=new ResearchReadingQueueForm(_project,_work))f.ShowDialog(this);break;default:MessageBox.Show(this,"This experiment is a manual protocol; its prediction and falsification criterion are the working instructions.");break;}}

    private async Task ChallengeAsync()
    {
        if(string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey)){using var settings=new TranslateApiSettingsForm();settings.ShowDialog(this);if(string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))return;}
        var sentSources=_sources.Take(150).Select(s=>$"{s.Kind}; review {s.ReviewState}; {s.Title}: {Clip(s.Detail,700)}").ToList();
        if(TranslationSettings.AlwaysConfirmBeforeSending&&MessageBox.Show(this,$"This will send the project name/notes, {_hypothesisData.Count} hypotheses, {sentSources.Count} bounded source summaries, and {_experimentData.Count} experiment descriptions to Gemini. Returned proposals remain unsaved until you check and accept them. Continue?","Send project structure to Gemini?",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        Enabled=false;_status.Text="Gemini is looking for rival explanations and discriminating tests…";
        try
        {
            var ai=await GeminiTranslationService.ChallengeResearchHypothesesAsync(
                $"{_author}, {_work.Title}\n{_project.Name}\n{_project.Notes}",
                _hypothesisData.Select(h=>$"{h.Title}: {h.Statement} ({h.Status})").ToList(),sentSources,
                _experimentData.Select(e=>$"{e.Title}; {e.Method}; {e.Status}; prediction {e.PredictedOutcome}; falsified by {e.FalsificationCriterion}").ToList(),
                TranslationSettings.GeminiApiKey!);
            await _research.AddSystemResearchLogEntryAsync(new ResearchLogEntry{ResearchProjectId=_project.ResearchProjectId,
                Kind=ResearchLogEntryKind.HypothesisChallengeGenerated,Summary="Generated AI hypothesis challenge proposals",
                Details=$"{ai.Model}; {ai.Proposals.Count} proposal(s); {DateTime.UtcNow:O}"});
            Enabled=true;using var review=new HypothesisChallengeReviewForm(ai);
            if(review.ShowDialog(this)==DialogResult.OK)
            {
                var nextHypothesisSort=_hypothesisData.Count;var nextExperimentSort=_experimentData.Count;
                foreach(var p in review.Accepted)
                {
                    if(p.Kind=="rivalHypothesis")
                        await _repo.SaveHypothesisAsync(new ResearchHypothesis{ResearchProjectId=_project.ResearchProjectId,
                            Title=p.Title,Statement=p.Statement,ResearcherNote=p.Rationale,Origin=EvidenceOrigin.AiCandidate,
                            AiModel=ai.Model,AiPrompt=ai.PromptProvenance,AiGeneratedUtc=DateTime.UtcNow,SortOrder=nextHypothesisSort++});
                    else
                        await _repo.SaveExperimentAsync(new ResearchExperiment{ResearchProjectId=_project.ResearchProjectId,
                            ResearchHypothesisId=_editingHypothesis?.ResearchHypothesisId,Title=p.Title,Method=ParseMethod(p.Method),
                            PredictedOutcome=Clean(p.PredictedOutcome),FalsificationCriterion=Clean(p.FalsificationCriterion),
                            ResearcherNote=$"{p.Statement}\r\n\r\nWhy proposed: {p.Rationale}",Origin=EvidenceOrigin.AiCandidate,
                            AiModel=ai.Model,AiPrompt=ai.PromptProvenance,AiGeneratedUtc=DateTime.UtcNow,SortOrder=nextExperimentSort++});
                }
                await ReloadAsync();
            }
            _status.Text=$"Gemini proposed {ai.Proposals.Count} rival(s)/experiment(s)."+
                (sentSources.Count<_sources.Count?$" Scope was bounded to the first {sentSources.Count} of {_sources.Count} sources.":"");
        }
        catch(Exception ex){_status.Text="AI challenge did not finish: "+ex.Message;}
        finally{Enabled=true;}
    }

    private static ResearchExperimentMethod ParseMethod(string value)=>Enum.TryParse<ResearchExperimentMethod>(value,true,out var parsed)?parsed:ResearchExperimentMethod.Manual;
    private static string Clip(string s,int n)=>s.Length<=n?s:s[..n]+"…";private static string? Clean(string? s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim();private static Button Btn(string text,int width)=>new(){Text=text,Width=width,Height=28};
    private static void Area(Control p,string label,TextBox box,int height,ref int y){p.Controls.Add(new Label{Text=label,Left=10,Top=y,Width=850,Height=20});y+=20;box.SetBounds(10,y,850,height);box.Multiline=height>30;box.ScrollBars=box.Multiline?ScrollBars.Vertical:ScrollBars.None;box.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;p.Controls.Add(box);y+=height+12;}
    private static void ComboPair(Control p,string l1,ComboBox c1,object d1,string l2,ComboBox c2,object d2,ref int y){p.Controls.Add(new Label{Text=l1,Left=10,Top=y,Width=330});p.Controls.Add(new Label{Text=l2,Left=360,Top=y,Width=330});y+=20;c1.SetBounds(10,y,330,28);c2.SetBounds(360,y,330,28);c1.DataSource=d1;c2.DataSource=d2;p.Controls.Add(c1);p.Controls.Add(c2);y+=40;}
    private sealed record HypothesisChoice(long? Id,string Label){public override string ToString()=>Label;}private sealed class ExperimentRow{public ExperimentRow(ResearchExperiment e)=>Experiment=e;public ResearchExperiment Experiment{get;}public string Status=>Experiment.Status.ToString();public string Method=>Experiment.Method.ToString();public string Title=>Experiment.Title;}
    private sealed class MatrixRow{public MatrixRow(HypothesisSource s,ResearchHypothesisAssessment? a){Source=s;Linked=a!=null;Relationship=a?.Relationship??HypothesisRelationship.Contextualizes;Strength=a?.Strength??HypothesisStrength.Moderate;Note=a?.ResearcherNote??"";}public HypothesisSource Source{get;}public bool Linked{get;set;}public string Kind=>Source.Kind.ToString();public string Title=>Source.Title;public string Review=>Source.ReviewState;public string Detail=>Source.Detail;public HypothesisRelationship Relationship{get;set;}public HypothesisStrength Strength{get;set;}public string Note{get;set;}}
}

internal sealed class HypothesisChallengeReviewForm:Form
{
    private readonly DataGridView _grid=new();private readonly BindingList<Row> _rows;public IReadOnlyList<HypothesisChallengeProposal> Accepted=>_rows.Where(r=>r.Include).Select(r=>r.Proposal).ToList();
    public HypothesisChallengeReviewForm(GeminiHypothesisChallengeResult result){Text="Review AI challenge proposals";Width=1150;Height=650;MinimumSize=new Size(800,500);StartPosition=FormStartPosition.CenterParent;_rows=new BindingList<Row>(result.Proposals.Select(p=>new Row(p)).ToList());var note=new Label{Dock=DockStyle.Top,Height=54,Padding=new Padding(9,7,5,0),Text=$"Candidate proposals from {result.Model}. Check only rivals or tests worth adding; acceptance preserves AI provenance but does not mark the proposal true."};_grid.Dock=DockStyle.Fill;_grid.AutoGenerateColumns=false;_grid.AllowUserToAddRows=false;_grid.RowHeadersVisible=false;_grid.DataSource=_rows;_grid.Columns.Add(new DataGridViewCheckBoxColumn{DataPropertyName=nameof(Row.Include),HeaderText="Add",Width=48});_grid.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(Row.Kind),HeaderText="Kind",Width=110,ReadOnly=true});_grid.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(Row.Title),HeaderText="Proposal",Width=210,ReadOnly=true});_grid.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(Row.Method),HeaderText="Method",Width=115,ReadOnly=true});_grid.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=nameof(Row.Detail),HeaderText="Statement / rationale / falsification",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,ReadOnly=true});var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,Padding=new Padding(8),FlowDirection=FlowDirection.RightToLeft};var cancel=new Button{Text="Cancel",Width=80,DialogResult=DialogResult.Cancel};var accept=new Button{Text="Accept checked",Width=120};accept.Click+=(_,_)=>{_grid.EndEdit();DialogResult=DialogResult.OK;Close();};bottom.Controls.AddRange([cancel,accept]);Controls.Add(_grid);Controls.Add(note);Controls.Add(bottom);ReadingTheme.AttachTo(this,()=>note.ForeColor=ReadingTheme.MutedText);WindowShortcuts.CloseOnEscape(this);}
    private sealed class Row{public Row(HypothesisChallengeProposal p)=>Proposal=p;public HypothesisChallengeProposal Proposal{get;}public bool Include{get;set;}public string Kind=>Proposal.Kind=="rivalHypothesis"?"Rival":"Experiment";public string Title=>Proposal.Title;public string Method=>Proposal.Method;public string Detail=>$"{Proposal.Statement} — {Proposal.Rationale}"+(string.IsNullOrWhiteSpace(Proposal.FalsificationCriterion)?"":$" Falsified by: {Proposal.FalsificationCriterion}");}
}
