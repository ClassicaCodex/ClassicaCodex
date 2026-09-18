namespace ClassicaCodex.UI;

/// <summary>
/// The project's own addresses on the web.
///
/// There is exactly one of these that matters to a reader - where to take a
/// bug - and until now it existed only inside the crash dialog, as a literal,
/// in the one place a reader sees only when something has already gone wrong
/// badly enough to interrupt them. Someone who hit a subtler fault, or who
/// dismissed that dialog and came back to it later, had the evidence in
/// errors.log and nowhere to take it: Help described the log file and About
/// listed the corpora, and neither named the issue tracker.
///
/// It is a constant here rather than a literal in each window so the two
/// cannot drift apart. A repository can be renamed or moved, and a
/// half-updated address is worse than an obviously missing one - it looks
/// like a working link and lands on somebody else's project.
/// </summary>
internal static class ProjectLinks
{
    /// <summary>
    /// Where to report a problem. HTTPS deliberately: a plain-http link that
    /// redirects still sends the first request in clear, and this one is
    /// handed to whatever browser the reader uses.
    /// </summary>
    internal const string Issues = "https://github.com/ClassicaCodex/ClassicaCodex/issues";
}
