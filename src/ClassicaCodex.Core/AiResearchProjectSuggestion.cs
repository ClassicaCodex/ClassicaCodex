namespace ClassicaCodex.Core;

public sealed record ScholarlyReadingLead(
    string Key, string Title, IReadOnlyList<string> Authors, string? Year, string? ContainerTitle,
    string? Publisher, string Doi, string? Url, string? Abstract);

public sealed record SuggestedHypothesis(string Title, string Statement);
public sealed record SuggestedExperiment(
    string Title, string Method, string PredictedOutcome, string FalsificationCriterion);
public sealed record AiResearchProjectSuggestion(
    string Category, string Title, string CentralQuestion, string Rationale, string Grounding,
    IReadOnlyList<string> ResearchQuestions, IReadOnlyList<SuggestedHypothesis> Hypotheses,
    IReadOnlyList<SuggestedExperiment> Experiments, IReadOnlyList<string> ReadingLeadKeys,
    IReadOnlyList<string> PassageKeys);
public sealed record GeminiProjectSuggestionsResult(
    string Model, string PromptProvenance, IReadOnlyList<AiResearchProjectSuggestion> Suggestions);
