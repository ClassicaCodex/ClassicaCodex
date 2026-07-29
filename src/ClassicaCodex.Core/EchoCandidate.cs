namespace ClassicaCodex.Core;

/// <summary>
/// One candidate echo as the AI reported it - a citation ref into the
/// comparison work, a rough confidence level, and a one-sentence rationale.
/// Deliberately just data at this stage: CrossLanguageEchoForm is the one
/// that checks CitationRef against the comparison work's real TextNodes
/// before showing anything as a genuine result. A candidate whose ref
/// doesn't resolve to a real passage never reaches the person as if it were
/// one - see CrossLanguageEchoForm for why that check exists at all.
/// </summary>
public record EchoCandidate(string CitationRef, string Confidence, string Rationale);
