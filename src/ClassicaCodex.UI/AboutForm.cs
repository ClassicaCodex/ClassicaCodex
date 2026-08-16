using System.Diagnostics;

namespace ClassicaCodex.UI;

/// <summary>
/// Attribution and licensing for every external data source this app reads
/// from. None of that data belongs to this project - Perseus's texts and
/// lexica, and the two lemma datasets, each carry their own license, and
/// this screen exists so that's never buried or assumed away.
///
/// One thing worth being explicit about here rather than discovering later:
/// the Greek lemma data (gcelano/LemmatizedAncientGreekXML) is licensed
/// CC BY-NC 4.0 - NonCommercial. That's a real constraint on this app, not
/// a footnote: as long as it uses that data, it can't be sold, ad-supported,
/// or monetized in any form. Free and open, or not distributed with that
/// data at all.
/// </summary>
public class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About Classica Codex";
        AppIcons.ApplyWindowIcon(this, "About");
        // ClientSize, not Width/Height - those set the OUTER window bounds
        // (title bar and borders included), while every control below is
        // positioned relative to the drawable area inside that, which is
        // smaller by however much chrome the current OS/DPI adds. That gap
        // was exactly enough to clip the Close button off the bottom edge.
        ClientSize = new Size(720, 680);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var scrollHost = new Panel
        {
            Left = 0,
            Top = 0,
            Width = 720,
            Height = 620,
            AutoScroll = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        var y = 16;

        AddHeading(scrollHost, "Classica Codex", ref y, 20, FontStyle.Bold);

        // Read from the assembly rather than written here, so it can't drift
        // from what actually shipped - the version in the csproj is the one
        // thing that definitely matches the binary someone is running.
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            AddParagraph(scrollHost, $"Version {version.Major}.{version.Minor}.{version.Build}",
                ref y, 672, Color.DimGray);
        }

        AddParagraph(scrollHost,
            "A personal reading and research tool for the Perseus Digital Library corpus of ancient Greek " +
            "and Latin texts - built to make close reading, cross-referencing, and word study easier across " +
            "an entire library at once.\r\n\r\n" +
            "Classica Codex is free and always will be. Part of the reason for that isn't just preference - " +
            "one of the data sources it depends on is licensed for non-commercial use only, and that alone " +
            "settles the question.", ref y, 672);

        AddHeading(scrollHost, "Data Sources & Licensing", ref y, 14, FontStyle.Bold);
        AddParagraph(scrollHost,
            "This app doesn't own or claim any of the texts, translations, dictionaries, or linguistic data " +
            "it reads. All of it comes from the open projects below, each under its own license. Full license " +
            "text is linked, not reproduced here.",
            ref y, 672, Color.DimGray);

        y += 8;
        AddSourceSection(
            scrollHost, ref y,
            "Ancient Greek & Latin Texts",
            "The full corpus of Greek and Latin literature this app reads - the primary source texts and " +
            "their translations.",
            "Perseus Digital Library (PerseusDL/canonical-greekLit, canonical-latinLit)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/PerseusDL/canonical-greekLit",
            "Tufts University holds the overall copyright to the Perseus Digital Library. Perseus's own " +
            "terms ask that any modifications made to their texts be offered back to the project.");

        AddSourceSection(
            scrollHost, ref y,
            "Dictionaries",
            "Liddell-Scott-Jones Greek-English Lexicon and Lewis & Short's A Latin Dictionary - the " +
            "definitions shown in Word Study.",
            "Perseus Digital Library (PerseusDL/lexica)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/PerseusDL/lexica",
            "Digitized and maintained by the Perseus Project with funding from the National Endowment for " +
            "the Humanities. The original 19th-century dictionaries are in the public domain; this digital " +
            "edition and its markup are Perseus's own work.");

        AddSourceSection(
            scrollHost, ref y,
            "Greek Lemma Data",
            "Maps inflected Greek word forms to their dictionary headwords - what makes Greek search and " +
            "Word Study work on more than just exact spellings.",
            "Giuseppe G. A. Celano (gcelano/LemmatizedAncientGreekXML)",
            "Creative Commons Attribution-NonCommercial 4.0 International",
            "https://github.com/gcelano/LemmatizedAncientGreekXML",
            "NonCommercial. This is the license that keeps this whole app free - as long as it uses this " +
            "data, it cannot be sold or monetized in any form.",
            highlightNotice: true);

        AddSourceSection(
            scrollHost, ref y,
            "Latin Lemma Data",
            "The same mapping for Latin.",
            "Lasciva Roma (lascivaroma/latin-lemmatized-texts)",
            "MIT License",
            "https://github.com/lascivaroma/latin-lemmatized-texts",
            null);

        AddSourceSection(
            scrollHost, ref y,
            "World Map Data",
            "Real coastline shapes for the Places Map. Entirely optional - without it, the map still " +
            "works, using simpler built-in landmasses instead.",
            "Natural Earth, via the nvkelso/natural-earth-vector mirror on GitHub",
            "Public Domain",
            "https://github.com/nvkelso/natural-earth-vector",
            "Public domain by the original project's own terms - no attribution is legally required, " +
            "included here anyway for completeness. Natural Earth's home project is at " +
            "naturalearthdata.com; only the single small land-outline file the Places Map actually uses " +
            "is downloaded, not the full dataset.");

        AddSourceSection(
            scrollHost, ref y,
            "Art & Archaeology Data",
            "Real objects from the ancient world - vases, coins, gems, sculptures, sites, and buildings, " +
            "with descriptions and photos - shown on the Places Map and Myth Network.",
            "Perseus Digital Library (perseus-aa/json)",
            "Perseus Digital Library terms; images not redistributed",
            "https://github.com/perseus-aa/json",
            "Only the catalog descriptions download and stay in your library, the same as everything " +
            "else here. The photographs do not: Perseus's own terms for this collection don't permit " +
            "redistributing the images outside its own site, so this app always fetches them live from " +
            "Perseus's server when you view one, and never saves a copy. That's a deliberate design " +
            "choice matching the license, not a missing feature.",
            highlightNotice: true);

        AddSourceSection(
            scrollHost, ref y,
            "English Lemma Data & Dictionary",
            "Maps English word forms back to their dictionary headword and supplies definitions - the " +
            "same job the Greek and Latin lemma data does above, applied to the English translations " +
            "already in your library. Makes search find \"spoke\" when you type \"speak\", and brings " +
            "Word Study to the translation side as well as the original.",
            "Princeton University (WordNet lexical database)",
            "WordNet License - permissive, free for any use",
            "https://wordnet.princeton.edu",
            "Unlike the Greek lemma data above, this one carries no NonCommercial restriction. " +
            "Princeton's own license permits any use, including commercial, provided its copyright " +
            "notice is retained - it doesn't loosen the constraint the Greek lemma data sets for the " +
            "app as a whole, but using it doesn't add a second one either.");

        AddSourceSection(
            scrollHost, ref y,
            "Renaissance & Early Modern English Texts",
            "Shakespeare, Marlowe, Holinshed, Hakluyt, Sidney, James I, Wilson, and Peacham - Perseus's " +
            "collection of the writers who reworked and responded to classical material in English.",
            "Perseus Digital Library (PerseusDL/canonical-engLit)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/PerseusDL/canonical-engLit",
            "Same Perseus/Tufts terms as the Greek and Latin texts above - this repository carries the " +
            "same standard license text PerseusDL applies across its canonical-* collections.");

        AddSourceSection(
            scrollHost, ref y,
            "Post-Classical Greek Texts",
            "The Open Greek and Latin project's sequel to Perseus's own Greek collection - Greek (and a " +
            "little Latin) written after the classical period, into late antiquity. Deliberately scoped by " +
            "its own maintainers to avoid works Perseus already carries; where the two do overlap, it's " +
            "because this collection adds an older alternate edition of an already-covered work, not a " +
            "duplicate of it.",
            "Open Greek and Latin Project (OpenGreekAndLatin/First1KGreek)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/OpenGreekAndLatin/First1KGreek",
            null);

        AddSourceSection(
            scrollHost, ref y,
            "Medieval Nordic Manuscripts",
            "Old Norse, Old Icelandic, Old Swedish and Old Danish manuscripts, transcribed word by word " +
            "from the parchment - Heimskringla, Laxdœla saga, the Codex Wormianus, the Old Norwegian " +
            "homily book, Vǫluspá in the Codex Regius. Downloaded by hand from Menota's catalogue rather " +
            "than fetched automatically: Menota publishes one file per manuscript, and there is no single " +
            "archive to pull.",
            "Medieval Nordic Text Archive (menota.org)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://www.menota.org/EN_forside.xhtml",
            "Each manuscript carries its own licence statement naming the editor who granted it. Menota " +
            "transcribes at up to three levels - facsimile, diplomatic and normalised - and this app reads " +
            "one of them per manuscript, chosen for coverage and recorded with the edition, because a text " +
            "assembled from whichever level each word happened to carry would belong to no scribe and no " +
            "dictionary.");

        AddHeading(scrollHost, "Online Services & Privacy", ref y, 14, FontStyle.Bold);
        AddParagraph(scrollHost,
            "Translation can send a selected passage to Anthropic's Claude or Google's Gemini when you " +
            "explicitly request it. Research Bench AI tools use Gemini to propose corpus evidence, rival " +
            "hypotheses, intertextual readings, provisional syntheses, and new research projects. These " +
            "calls send only the context described in the confirmation shown before the request - such as " +
            "a bounded corpus sample, project questions, or selected passages. Gemini requires your own API " +
            "key. Each provider's current data-use and privacy terms apply, so review them before sending " +
            "sensitive or unpublished material.",
            ref y, 672, Color.DimGray);

        AddParagraph(scrollHost,
            "The new-project assistant can also query Crossref's public REST API for publication metadata. " +
            "Crossref receives only the editable scholarly search terms - never your corpus text, notes, " +
            "evidence, or project database - and needs no API key. Returned titles, authors, years, DOIs, " +
            "links, and deposited abstracts are saved only as reading leads. Classica Codex never treats " +
            "metadata as proof of what a publication argues. Nothing is sent to Gemini or Crossref until " +
            "you click the corresponding AI or discovery action; confirmation prompts can remain enabled " +
            "in AI Translation Settings. See Help for the complete Research Bench workflow.",
            ref y, 672, Color.DimGray);

        var closeButton = new Button
        {
            Text = "Close",
            Left = 616,
            Top = 632,
            Width = 90,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };

        Controls.Add(scrollHost);
        Controls.Add(closeButton);
        AcceptButton = closeButton;

        ReadingTheme.AttachTo(this);

        WindowShortcuts.CloseOnEscape(this);
    }

    private static void AddHeading(Control parent, string text, ref int y, float size, FontStyle style)
    {
        var label = new Label
        {
            Text = text,
            Left = 16,
            Top = y,
            Width = 672,
            AutoSize = false,
            Height = (int)(size * 1.6),
            Font = new Font("Segoe UI", size, style)
        };
        parent.Controls.Add(label);
        y += label.Height + 6;
    }

    private static void AddParagraph(Control parent, string text, ref int y, int width, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            Left = 16,
            Top = y,
            Width = width,
            AutoSize = false,
            Height = TextRenderer.MeasureText(text, SystemFonts.DefaultFont, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak).Height + 8,
            ForeColor = color ?? Color.Black
        };
        parent.Controls.Add(label);
        y += label.Height + 6;
    }

    private static void AddSourceSection(
        Control parent, ref int y,
        string title, string description, string sourceLine, string licenseName, string url, string? note,
        bool highlightNotice = false)
    {
        var titleLabel = new Label
        {
            Text = title,
            Left = 16,
            Top = y,
            Width = 672,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        parent.Controls.Add(titleLabel);
        y += 24;

        AddParagraph(parent, description, ref y, 672, Color.DimGray);

        var sourceLabel = new Label { Text = "Source: " + sourceLine, Left = 16, Top = y, Width = 672 };
        parent.Controls.Add(sourceLabel);
        y += 20;

        var licenseLabel = new Label
        {
            Text = "License: " + licenseName,
            Left = 16,
            Top = y,
            Width = 400,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };
        parent.Controls.Add(licenseLabel);
        y += 20;

        var link = new LinkLabel { Text = url, Left = 16, Top = y, Width = 672 };
        link.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* if the shell can't open it, there's nothing more useful to do here */ }
        };
        parent.Controls.Add(link);
        y += 22;

        if (!string.IsNullOrEmpty(note))
        {
            var noteLabel = new Label
            {
                Text = note,
                Left = 16,
                Top = y,
                Width = 672,
                Height = TextRenderer.MeasureText(note, SystemFonts.DefaultFont, new Size(672, int.MaxValue),
                    TextFormatFlags.WordBreak).Height + 8,
                ForeColor = highlightNotice ? Color.DarkRed : Color.DimGray,
                Font = highlightNotice ? new Font(SystemFonts.DefaultFont, FontStyle.Bold) : SystemFonts.DefaultFont
            };
            parent.Controls.Add(noteLabel);
            y += noteLabel.Height + 8;
        }

        y += 16; // gap before next section
    }
}
