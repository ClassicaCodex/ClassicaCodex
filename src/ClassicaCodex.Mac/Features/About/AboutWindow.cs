using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Help;

namespace ClassicaCodex.Mac.Features.About;

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
/// data at all. Its notice is set in the warning colour for that reason, and
/// so is the one about the archaeology photographs, which this app never
/// keeps a copy of.
/// </summary>
public sealed class AboutWindow : FeatureWindow
{
    private const string LinkFailure = "About Classica Codex";

    public AboutWindow()
    {
        Title = "About Classica Codex";
        Width = 720;
        Height = 680;
        MinWidth = 560;
        MinHeight = 420;
        CanMaximize = false;

        var body = new StackPanel { Margin = new Thickness(32, 22, 32, 30) };

        body.Children.Add(Prose.Paragraph(
            "A personal reading and research tool for the Perseus Digital Library corpus of ancient Greek " +
            "and Latin texts - built to make close reading, cross-referencing, and word study easier across " +
            "an entire library at once."));
        body.Children.Add(Prose.Paragraph(
            "Classica Codex is free and always will be. Part of the reason for that isn't just preference - " +
            "one of the data sources it depends on is licensed for non-commercial use only, and that alone " +
            "settles the question."));

        body.Children.Add(Prose.Heading("Data Sources & Licensing", 19, top: 16));
        body.Children.Add(Prose.Paragraph(
            "This app doesn't own or claim any of the texts, translations, dictionaries, or linguistic data " +
            "it reads. All of it comes from the open projects below, each under its own license. Full license " +
            "text is linked, not reproduced here.", muted: true, bottom: 14));

        AddSourceSection(body,
            "Ancient Greek & Latin Texts",
            "The full corpus of Greek and Latin literature this app reads - the primary source texts and " +
            "their translations.",
            "Perseus Digital Library (PerseusDL/canonical-greekLit, canonical-latinLit)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/PerseusDL/canonical-greekLit",
            "Tufts University holds the overall copyright to the Perseus Digital Library. Perseus's own " +
            "terms ask that any modifications made to their texts be offered back to the project.");

        AddSourceSection(body,
            "Dictionaries",
            "Liddell-Scott-Jones Greek-English Lexicon and Lewis & Short's A Latin Dictionary - the " +
            "definitions shown in Word Study.",
            "Perseus Digital Library (PerseusDL/lexica)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/PerseusDL/lexica",
            "Digitized and maintained by the Perseus Project with funding from the National Endowment for " +
            "the Humanities. The original 19th-century dictionaries are in the public domain; this digital " +
            "edition and its markup are Perseus's own work.");

        AddSourceSection(body,
            "Greek Lemma Data",
            "Maps inflected Greek word forms to their dictionary headwords - what makes Greek search and " +
            "Word Study work on more than just exact spellings.",
            "Giuseppe G. A. Celano (gcelano/LemmatizedAncientGreekXML)",
            "Creative Commons Attribution-NonCommercial 4.0 International",
            "https://github.com/gcelano/LemmatizedAncientGreekXML",
            "NonCommercial. This is the license that keeps this whole app free - as long as it uses this " +
            "data, it cannot be sold or monetized in any form.",
            highlightNotice: true);

        AddSourceSection(body,
            "Latin Lemma Data",
            "The same mapping for Latin.",
            "Lasciva Roma (lascivaroma/latin-lemmatized-texts)",
            "MIT License",
            "https://github.com/lascivaroma/latin-lemmatized-texts",
            null);

        AddSourceSection(body,
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

        AddSourceSection(body,
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

        AddSourceSection(body,
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

        AddSourceSection(body,
            "Renaissance & Early Modern English Texts",
            "Shakespeare, Holinshed, Hakluyt, Sidney, James I, Wilson, and Peacham - Perseus's " +
            "collection of the writers who reworked and responded to classical material in English. " +
            "Perseus divides it into freely redistributable texts and ones still in copyright, and only " +
            "the first are imported - which leaves out the New Variorum editions and every one of " +
            "Marlowe's own works. The one Marlowe file that is freely licensed is not by him: the Faust " +
            "Book, the anonymous English prose romance he used as his source for Doctor Faustus, which " +
            "Perseus files under his name and this follows.",
            "Perseus Digital Library (PerseusDL/canonical-engLit)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/PerseusDL/canonical-engLit",
            "Same Perseus/Tufts terms as the Greek and Latin texts above - this repository carries the " +
            "same standard license text PerseusDL applies across its canonical-* collections.");

        AddSourceSection(body,
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

        AddSourceSection(body,
            "Latin Church Fathers (CSEL)",
            "The Corpus Scriptorum Ecclesiasticorum Latinorum - critical editions of Augustine, Ambrose, " +
            "Jerome, Cyprian and their contemporaries, from the volumes old enough to be out of copyright.",
            "Open Greek and Latin Project (OpenGreekAndLatin/csel-dev)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/OpenGreekAndLatin/csel-dev",
            "The repository declares no licence at its top level; each text file declares CC BY-SA 4.0 in " +
            "its own TEI header, which is the same licence Perseus applies across its canonical collections. " +
            "The underlying CSEL volumes are 19th and early 20th-century editions in the public domain; the " +
            "OCR correction and EpiDoc encoding are the Leipzig project's own work.");

        AddSourceSection(body,
            "Patrologia Latina",
            "Migne's collection of Latin Christian writing, from Tertullian to the twelfth century - the " +
            "largest body of Latin here, and a 19th-century reprint rather than a critical edition.",
            "Open Greek and Latin Project (OpenGreekAndLatin/patrologia_latina-dev)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/OpenGreekAndLatin/patrologia_latina-dev",
            "Declared per file in the TEI headers, as with CSEL above. Migne's volumes (1844-1865) are long " +
            "out of copyright; the OCR correction and encoding are the Leipzig project's work. Where a work " +
            "appears both here and in CSEL, the CSEL text is the critical edition and this is the reprint.");

        AddSourceSection(body,
            "Political Theory",
            "Jean Bodin's Six Books of the Commonwealth in French, Latin and English - the founding text of " +
            "modern sovereignty, in the author's own two versions and its first English translation.",
            "Perseus Digital Library (PerseusDL/canonical-pdlpsci)",
            "Creative Commons Attribution-ShareAlike 4.0 International",
            "https://github.com/PerseusDL/canonical-pdlpsci",
            "The same Perseus and Tufts terms as the Greek and Latin texts above. The printed editions " +
            "digitised here date from 1577, 1586 and 1606 and are long out of copyright.");

        AddSourceSection(body,
            "Cuneiform Texts",
            "Akkadian and Sumerian pressed into clay - the royal inscriptions of Tiglath-pileser III, " +
            "Sennacherib and Esarhaddon, the letters written to Sargon II, scholarly commentaries and " +
            "bilinguals, and the magic: Maqlû, the anti-witchcraft rituals, and the tablet libraries of " +
            "Sultantepe and Uruk with Šurpu among them. Each text is held three ways: in the wedge signs themselves, in the " +
            "transliteration scholars read them by, and as the dictionary meaning of every word in " +
            "order, which opens opposite the signs where a translation would. Gods, kings and places " +
            "are marked as such by Oracc's own editors, which is why a cuneiform word can be looked up " +
            "here at all.",
            "Oracc: The Open Richly Annotated Cuneiform Corpus (oracc.org)",
            "Creative Commons Attribution-ShareAlike 3.0, or CC0, depending on the project",
            "https://oracc.museum.upenn.edu/",
            "Oracc licenses per project rather than as a whole, and the two licences carry different " +
            "obligations, so this app records each text's own credits line - naming its editors and " +
            "stating its licence - alongside the text, and shows it on the edition. Where a project's " +
            "own metadata and its texts disagree, the stricter licence is the one applied. " +
            "Oracc's open data carries the transliteration, the lemmatisation and an index of the " +
            "English, but not the translated prose, which stays on Oracc's own site - for some corpora " +
            "because the translation is a living scholar's published work. Where a project's site " +
            "releases its translations openly, they are fetched from it and installed beside the text, " +
            "credited: Tzvi Abusch's Maqlû and Abusch and Schwemer's Corpus of Mesopotamian " +
            "Anti-witchcraft Rituals from CMAwRo, and the Geography of Knowledge project's translations " +
            "of the Sultantepe and Uruk tablets, all under Creative Commons Attribution-ShareAlike 3.0. " +
            "For the rest that gap is left open rather than filled with generated English, and each " +
            "word's dictionary meaning is offered instead.");

        AddSourceSection(body,
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

        body.Children.Add(Prose.Heading("Online Services & Privacy", 19, top: 18));
        body.Children.Add(Prose.Paragraph(
            "Classica Codex is offline-first. These optional actions use online services only when you ask:",
            muted: true, bottom: 12));

        body.Children.Add(Prose.Bullet(
            "AI translation - Claude or Gemini",
            "Sends the selected passage only after you request a translation. Both providers require your " +
            "own API key."));

        body.Children.Add(Prose.Bullet(
            "Research Bench AI - Gemini",
            "May receive the project context named in the confirmation dialog, such as questions, selected " +
            "passages, or a bounded corpus sample. It can propose evidence, rival hypotheses, intertextual " +
            "readings, provisional syntheses, and new projects; its suggestions still require human review."));

        body.Children.Add(Prose.Bullet(
            "Publication discovery - Crossref",
            "Sends only the editable scholarly search terms. It never sends corpus text, notes, evidence, " +
            "or the project database, and it needs no API key. Returned metadata is saved as reading leads, " +
            "not as proof of what a publication argues."));

        body.Children.Add(Prose.Bullet(
            "You stay in control",
            "Nothing is sent until you click the corresponding action. Confirmation prompts can remain " +
            "enabled in AI Translation Settings. Review each provider's current privacy and data-use terms " +
            "before sending sensitive or unpublished material. See Help for the complete workflow."));

        // What the Mac front end adds to what the Windows app is built from:
        // the interface framework and the glyphs. Their full notices are in
        // THIRD-PARTY-NOTICES-MAC.md; this says they are there.
        body.Children.Add(Prose.Heading("The Mac App", 19, top: 18));
        body.Children.Add(Prose.Paragraph(
            "The same shared libraries as the Windows app, reading the same library file, with a Mac " +
            "interface of its own.", muted: true, bottom: 12));

        AddCredit(body, "Avalonia", "The interface framework, version 12.", "MIT License",
            "https://github.com/AvaloniaUI/Avalonia");
        AddCredit(body, "Phosphor Icons", "The toolbar and menu glyphs, version 2.1.1.", "MIT License",
            "https://github.com/phosphor-icons/core");

        var root = new DockPanel();
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
    }

    /// <summary>
    /// Read from the assembly rather than written here, so it can't drift
    /// from what actually shipped - the version in the project file is the
    /// one thing that definitely matches the program someone is running.
    /// </summary>
    internal static string? VersionText
    {
        get
        {
            var version = typeof(AboutWindow).Assembly.GetName().Version;
            return version == null ? null : $"Version {version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>
    /// Codex's painting of the gears and the scroll when it has arrived, the
    /// drawn gears until then; the name and the version over the right third,
    /// which both keep dark for them.
    /// </summary>
    private static Control BuildHeader()
    {
        var panel = new Panel { Height = 196, ClipToBounds = true };

        var painting = Art.Load("about-art.png");
        if (painting != null)
        {
            panel.Children.Add(new Image
            {
                Source = painting,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        else
        {
            panel.Children.Add(new AboutArt());
        }

        // Light type on a ground that is dark in both appearances.
        var ivory = new SolidColorBrush(Color.FromRgb(255, 248, 236));

        var text = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 32, 26)
        };
        var name = new TextBlock { Text = "Classica Codex", FontSize = 34, FontWeight = FontWeight.Medium, Foreground = ivory, LetterSpacing = 0.3 };
        Prose.UseFont(name, "Cc.DisplayFont");
        text.Children.Add(name);

        if (VersionText is { } version)
        {
            text.Children.Add(new SelectableTextBlock
            {
                Text = version,
                Foreground = ivory,
                Opacity = 0.72,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 2, 0)
            });
        }

        panel.Children.Add(text);
        return panel;
    }

    private void AddSourceSection(
        StackPanel parent,
        string title, string description, string sourceLine, string licenseName, string url, string? note,
        bool highlightNotice = false)
    {
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        card.Children.Add(Prose.Paragraph(description, muted: true, bottom: 8));
        card.Children.Add(Prose.Paragraph("Source: " + sourceLine, bottom: 2));

        // The full width, and never trimmed: a licence name is the one line in
        // a section that has to be read whole.
        card.Children.Add(new SelectableTextBlock { Text = "License: " + licenseName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) });
        card.Children.Add(Prose.Link(this, url, LinkFailure));

        if (!string.IsNullOrEmpty(note))
        {
            // The two highlighted notices are the NonCommercial licence and
            // the image redistribution terms - exactly the sentences that
            // must not be skimmed past.
            var notice = Prose.Paragraph(note, muted: !highlightNotice, bottom: 0);
            notice.Margin = new Thickness(0, 8, 0, 0);
            if (highlightNotice)
            {
                notice.FontWeight = FontWeight.SemiBold;
                notice.Bind(TextBlock.ForegroundProperty, notice.GetResourceObservable("Cc.Danger"));
            }

            card.Children.Add(notice);
        }

        parent.Children.Add(new Border
        {
            Classes = { "card" },
            Padding = new Thickness(18, 14, 18, 14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = card
        });
    }

    private void AddCredit(StackPanel parent, string name, string what, string licence, string url)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(new SelectableTextBlock { Text = $"{name} - {what}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(Prose.Paragraph(licence, muted: true, bottom: 1));
        stack.Children.Add(Prose.Link(this, url, LinkFailure));
        parent.Children.Add(stack);
    }
}
