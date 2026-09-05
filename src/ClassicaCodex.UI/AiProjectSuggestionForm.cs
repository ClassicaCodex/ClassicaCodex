using System.Text;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public sealed class AiProjectSuggestionForm : ScaledForm
{
    private const int MaxCorpusChars=90_000;
    private readonly Work _work;private readonly string _author;private readonly ResearchRepository _research=new();private readonly EditionRepository _editions=new();private readonly TextNodeRepository _nodes=new();
    private readonly TextBox _query=new();private readonly CheckBox _crossref=new(){Text="Include verified Crossref scholarly metadata leads",Checked=true,AutoSize=true};private readonly Button _generate=new(){Text="Suggest projects with Gemini",Width=190};private readonly ListBox _suggestions=new();private readonly TextBox _details=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};private readonly Label _status=new();private readonly Button _create=new(){Text="Create selected project",Width=165,Enabled=false};
    private GeminiProjectSuggestionsResult? _result;private List<ScholarlyReadingLead> _leads=[];private readonly Dictionary<string,(TextNode Node,Edition Edition)> _passages=new(StringComparer.OrdinalIgnoreCase);
    public long? CreatedProjectId{get;private set;}

    public AiProjectSuggestionForm(Work work,string author)
    {
        _work=work;_author=author;Text=$"Let AI Suggest a New Project — {work.Title}";Width=1250;Height=780;MinimumSize=new Size(900,600);StartPosition=FormStartPosition.CenterParent;AppIcons.ApplyWindowIcon(this,"WordStudy");
        var top=new Panel{Dock=DockStyle.Top,Height=104,Padding=new Padding(10)};top.Controls.Add(new Label{Text="Scholarly search terms (editable)",Left=10,Top=8,Width=400});_query.SetBounds(10,30,800,27);_query.Text=$"{author} {work.Title} classical philology authorship interpretation";_crossref.SetBounds(10,67,360,25);_generate.SetBounds(390,64,190,30);_generate.Click+=async(_,_)=>await GenerateAsync();top.Controls.AddRange([_query,_crossref,_generate]);
        var split=new SplitContainer{Dock=DockStyle.Fill};_suggestions.Dock=DockStyle.Fill;_suggestions.SelectedIndexChanged+=(_,_)=>ShowSuggestion();_details.Dock=DockStyle.Fill;split.Panel1.Controls.Add(_suggestions);split.Panel2.Controls.Add(_details);
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=50,Padding=new Padding(8)};_create.Click+=async(_,_)=>await CreateAsync();bottom.Controls.Add(_create);bottom.Controls.Add(_status);_status.AutoSize=true;_status.Padding=new Padding(10,7,0,0);Controls.Add(split);Controls.Add(top);Controls.Add(bottom);
        ReadingTheme.AttachTo(this,()=>_status.ForeColor=ReadingTheme.MutedText);WindowShortcuts.CloseOnEscape(this);Shown+=(_,_)=>{var max=split.ClientSize.Width-600-split.SplitterWidth;if(max>=280){split.SplitterDistance=Math.Clamp(360,280,max);split.Panel1MinSize=280;split.Panel2MinSize=600;}};
    }

    private async Task GenerateAsync()
    {
        if(string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey)){using var settings=new TranslateApiSettingsForm();settings.ShowDialog(this);if(string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))return;}
        if(TranslationSettings.AlwaysConfirmBeforeSending&&MessageBox.Show(this,"This will send a bounded sample of the selected work, its attribution note, existing project titles, and any retrieved Crossref metadata to Gemini. Crossref receives only the editable scholarly search terms. Continue?","Discover research projects?",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        Enabled=false;_result=null;_suggestions.DataSource=null;_details.Clear();_create.Enabled=false;
        try
        {
            _status.Text=_crossref.Checked?"Retrieving scholarly metadata from Crossref…":"Building local corpus sample…";
            string? crossrefWarning=null;
            if(_crossref.Checked)
            {
                try{_leads=(await CrossrefDiscoveryService.SearchAsync(_query.Text)).ToList();}
                catch(Exception ex){_leads=[];crossrefWarning=" Crossref was unavailable, so these proposals use only the local corpus: "+ex.Message;}
            }
            else _leads=[];
            var corpus=await BuildCorpusSampleAsync();var projects=await _research.GetProjectsForWorkAsync(_work.WorkId);
            _status.Text="Gemini is proposing grounded projects…";
            var attribution=$"Attribution: {_work.AttributionStatus}; note: {_work.AttributionNote??"none"}";
            _result=await GeminiTranslationService.SuggestResearchProjectsAsync($"{_author}, {_work.Title}; CTS {_work.CtsUrn}; {attribution}",projects.Count==0?"(none)":string.Join("\n",projects.Select(p=>"- "+p.Name)),corpus,_leads,TranslationSettings.GeminiApiKey!);
            var leadKeys=_leads.Select(l=>l.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);var passageKeys=_passages.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var verified=_result.Suggestions.Select(s=>s with{ReadingLeadKeys=s.ReadingLeadKeys.Where(leadKeys.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),PassageKeys=s.PassageKeys.Where(passageKeys.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList()}).ToList();
            _result=_result with{Suggestions=verified};_suggestions.DataSource=verified.Select(s=>new SuggestionRow(s)).ToList();_create.Enabled=verified.Count>0;
            _status.Text=$"{verified.Count} proposal(s) · {_leads.Count} verified publication lead(s) · {_passages.Count} locally keyed passage(s)."+crossrefWarning;
        }catch(Exception ex){_status.Text="Could not generate suggestions: "+ex.Message;}finally{Enabled=true;}
    }

    private async Task<string> BuildCorpusSampleAsync()
    {
        _passages.Clear();var editions=await _editions.GetByWorkAsync(_work.WorkId);var edition=editions.FirstOrDefault(e=>e.Kind==EditionKind.Original&&(e.Orthography==null||e.Orthography.Equals("normalised",StringComparison.OrdinalIgnoreCase)))??editions.FirstOrDefault(e=>e.Kind==EditionKind.Original);
        if(edition==null)return "(No original-language edition is ingested.)";var text=new StringBuilder();var n=0;
        foreach(var node in await _nodes.GetByEditionAsync(edition.EditionId,true)){if(string.IsNullOrWhiteSpace(node.Text))continue;var key=$"P{n+1:00000}";var line=$"[{key}] [{PassageCitation.Display(node.CitationRef)}] {node.Text}\n";if(text.Length+line.Length>MaxCorpusChars)break;n++;text.Append(line);_passages[key]=(node,edition);}return text.ToString();
    }

    private void ShowSuggestion()
    {
        if((_suggestions.SelectedItem as SuggestionRow)?.Suggestion is not{}s){_details.Clear();return;}_details.Text=$"{s.Category}\r\n\r\nCENTRAL QUESTION\r\n{s.CentralQuestion}\r\n\r\nWHY IT MAY MATTER\r\n{s.Rationale}\r\n\r\nGROUNDING\r\n{s.Grounding}\r\n\r\nQUESTIONS\r\n- {string.Join("\r\n- ",s.ResearchQuestions)}\r\n\r\nRIVAL HYPOTHESES\r\n- {string.Join("\r\n- ",s.Hypotheses.Select(h=>$"{h.Title}: {h.Statement}"))}\r\n\r\nEXPERIMENTS\r\n- {string.Join("\r\n- ",s.Experiments.Select(e=>$"{e.Title} [{e.Method}] — falsified by: {e.FalsificationCriterion}"))}\r\n\r\nVERIFIED READING LEADS\r\n- {string.Join("\r\n- ",s.ReadingLeadKeys.Select(k=>_leads.First(l=>l.Key.Equals(k,StringComparison.OrdinalIgnoreCase))).Select(l=>$"{l.Title} — DOI {l.Doi}"))}\r\n\r\nLOCAL PASSAGES QUEUED\r\n{string.Join(", ",s.PassageKeys)}";
    }

    private async Task CreateAsync()
    {
        if(_result==null||(_suggestions.SelectedItem as SuggestionRow)?.Suggestion is not{}s)return;
        _create.Enabled=false;
        ResearchProject? project=null;
        try
        {
        project=new ResearchProject{WorkId=_work.WorkId,WorkCtsUrn=_work.CtsUrn,Name=s.Title,Notes=$"AI-proposed project ({s.Category}); human-selected.\r\n\r\n{s.Rationale}\r\n\r\nGrounding claimed by proposal: {s.Grounding}"};await _research.SaveProjectAsync(project);
        var questions=new[]{s.CentralQuestion}.Concat(s.ResearchQuestions).Where(q=>!string.IsNullOrWhiteSpace(q)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();for(var i=0;i<questions.Count;i++)await _research.SaveQuestionAsync(new ResearchQuestion{ResearchProjectId=project.ResearchProjectId,Text=questions[i],SortOrder=i,Origin=ResearchQuestionOrigin.AiProposed,AiModel=_result.Model,AiPrompt=_result.PromptProvenance,AiGeneratedUtc=DateTime.UtcNow});
        var hypotheses=new ResearchHypothesisRepository();for(var i=0;i<s.Hypotheses.Count;i++)await hypotheses.SaveHypothesisAsync(new ResearchHypothesis{ResearchProjectId=project.ResearchProjectId,Title=s.Hypotheses[i].Title,Statement=s.Hypotheses[i].Statement,Origin=EvidenceOrigin.AiCandidate,AiModel=_result.Model,AiPrompt=_result.PromptProvenance,AiGeneratedUtc=DateTime.UtcNow,SortOrder=i});
        for(var i=0;i<s.Experiments.Count;i++){var e=s.Experiments[i];await hypotheses.SaveExperimentAsync(new ResearchExperiment{ResearchProjectId=project.ResearchProjectId,Title=e.Title,Method=Enum.TryParse<ResearchExperimentMethod>(e.Method,true,out var m)?m:ResearchExperimentMethod.Manual,PredictedOutcome=Clean(e.PredictedOutcome),FalsificationCriterion=Clean(e.FalsificationCriterion),Origin=EvidenceOrigin.AiCandidate,AiModel=_result.Model,AiPrompt=_result.PromptProvenance,AiGeneratedUtc=DateTime.UtcNow,SortOrder=i});}
        var queue=new ResearchReadingQueueRepository();var sort=0;foreach(var key in s.ReadingLeadKeys){var l=_leads.First(x=>x.Key.Equals(key,StringComparison.OrdinalIgnoreCase));await queue.SaveAsync(new ResearchReadingItem{ResearchProjectId=project.ResearchProjectId,Kind=ResearchReadingKind.ExternalSource,Title=l.Title,Purpose="Verify relevance and read before treating any argument as a scholarly claim.",StableIdentifier="doi:"+l.Doi,Locator=l.Url,Notes=$"Verified Crossref metadata lead; content not reviewed. {string.Join("; ",l.Authors)} ({l.Year??"n.d."}), {l.ContainerTitle??l.Publisher}.",SortOrder=sort++});}
        foreach(var key in s.PassageKeys){var p=_passages[key];await queue.SaveAsync(new ResearchReadingItem{ResearchProjectId=project.ResearchProjectId,Kind=ResearchReadingKind.CorpusPassage,Title=$"{_work.Title} {PassageCitation.Display(p.Node.CitationRef)}",Purpose="Inspect the locally grounded passage that motivated this proposed project.",WorkCtsUrn=_work.CtsUrn,EditionCtsUrn=p.Edition.CtsUrn,CitationRef=p.Node.CitationRef,Quotation=p.Node.Text,Notes="Selected by an AI proposal; passage identity and text were resolved locally.",SortOrder=sort++});}
        CreatedProjectId=project.ResearchProjectId;DialogResult=DialogResult.OK;Close();
        }
        catch(Exception ex)
        {
            var rollback=" The incomplete project was removed.";
            if(project?.ResearchProjectId is>0)
            {
                try{await _research.DeleteIncompleteProjectAsync(project.ResearchProjectId);}
                catch(Exception cleanup){rollback=" Cleanup also failed, so inspect the project list for a partial project: "+cleanup.Message;}
            }
            else rollback=" No project was created.";
            _status.Text="Could not finish creating the project: "+ex.Message+rollback;
            _create.Enabled=true;
        }
    }

    private static string? Clean(string s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim();private sealed record SuggestionRow(AiResearchProjectSuggestion Suggestion){public override string ToString()=>$"[{Suggestion.Category}] {Suggestion.Title}";}
}
