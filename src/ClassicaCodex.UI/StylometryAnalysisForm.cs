using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Compares saved stylometric runs against each other.
///
/// A single Delta run says very little. The useful questions are comparative:
/// does this work sit where an author's undisputed works sit, and does that
/// answer survive a change of preprocessing? Both need many runs side by side,
/// which is what this form is for.
///
/// The headline measure is DEPTH TO FIRST OUTSIDER - the rank at which the
/// first work by a different author appears in a work's own neighbour list.
/// It is used in preference to nearest-neighbour identity because it proved
/// far more stable: across feature counts and accent-folding settings that
/// reshuffled raw rank order substantially, depth stayed in the same band for
/// the same work. A measure that moves when only the preprocessing moves is
/// not measuring the text.
/// </summary>
public class StylometryAnalysisForm : Form
{
    private readonly StylometryRunRepository _runRepo = new();

    private readonly ComboBox _profileCombo;
    private readonly ComboBox _authorCombo;
    private readonly ListView _metricsList;
    private readonly Label _summaryLabel;
    private readonly ListView _stabilityList;
    private readonly ListView _lengthList;
    private readonly Label _lengthSummary;
    private readonly ThemedTabControl _tabs;

    private List<StylometrySettings> _profiles = new();
    private List<StylometryRunMetrics> _currentMetrics = new();

    public StylometryAnalysisForm()
    {
        Text = "Stylometry - Compare Saved Runs";
        AppIcons.ApplyWindowIcon(this, "Stylometry");
        Width = 1120;
        Height = 720;
        StartPosition = FormStartPosition.CenterParent;

        var profileLabel = new Label { Text = "Settings profile:", Left = 12, Top = 14, Width = 100 };
        _profileCombo = new ComboBox
        {
            Left = 116,
            Top = 10,
            Width = 320,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _profileCombo.SelectedIndexChanged += async (_, _) => await RefreshAsync();

        var authorLabel = new Label { Text = "Author:", Left = 452, Top = 14, Width = 50 };
        _authorCombo = new ComboBox
        {
            Left = 506,
            Top = 10,
            Width = 240,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _authorCombo.SelectedIndexChanged += (_, _) => RenderMetrics();

        _tabs = new ThemedTabControl
        {
            Left = 12,
            Top = 44,
            Width = 1076,
            Height = 630,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        // --- Tab 1: reference distribution -----------------------------------
        var distributionTab = new TabPage("Reference distribution");

        _metricsList = new ListView
        {
            Left = 8,
            Top = 8,
            Width = 1052,
            Height = 400,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        _metricsList.Columns.Add("Work", 240);
        _metricsList.Columns.Add("Depth to first outsider", 150);
        _metricsList.Columns.Add("z-score", 80);
        _metricsList.Columns.Add("Delta floor", 90);
        _metricsList.Columns.Add("Purity @10", 80);
        _metricsList.Columns.Add("Nearest neighbour", 380);

        _summaryLabel = new Label
        {
            Left = 8,
            Top = 416,
            Width = 1052,
            Height = 170,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        distributionTab.Controls.Add(_metricsList);
        distributionTab.Controls.Add(_summaryLabel);

        // --- Tab 2: stability across settings --------------------------------
        var stabilityTab = new TabPage("Stability across settings");

        var stabilityHelp = new Label
        {
            Text = "Depth to first outsider for each work under every saved settings profile. " +
                   "A row that holds steady across columns is a result about the text; " +
                   "a row that swings is a result about the preprocessing.",
            Left = 8,
            Top = 8,
            Width = 1052,
            Height = 34,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _stabilityList = new ListView
        {
            Left = 8,
            Top = 48,
            Width = 1052,
            Height = 540,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        stabilityTab.Controls.Add(stabilityHelp);
        stabilityTab.Controls.Add(_stabilityList);

        // --- Tab 3: length confound ------------------------------------------
        var lengthTab = new TabPage("Length confound");

        var lengthHelp = new Label
        {
            Text = "Depth to first outsider against text length. Shorter texts give noisier " +
                   "relative-frequency estimates, which inflates Delta against everything and lets " +
                   "other authors rise earlier in the ranking. If depth correlates with length, it " +
                   "is measuring how much text there is, not who wrote it.",
            Left = 8,
            Top = 8,
            Width = 1052,
            Height = 48,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _lengthList = new ListView
        {
            Left = 8,
            Top = 62,
            Width = 1052,
            Height = 370,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        _lengthList.Columns.Add("Work", 260);
        _lengthList.Columns.Add("Tokens", 90);
        _lengthList.Columns.Add("Depth", 70);
        _lengthList.Columns.Add("Length rank", 90);
        _lengthList.Columns.Add("Depth rank", 90);
        _lengthList.Columns.Add("Rank gap", 80);
        _lengthList.Columns.Add("Delta floor", 90);

        _lengthSummary = new Label
        {
            Left = 8,
            Top = 440,
            Width = 1052,
            Height = 150,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        lengthTab.Controls.Add(lengthHelp);
        lengthTab.Controls.Add(_lengthList);
        lengthTab.Controls.Add(_lengthSummary);

        _tabs.TabPages.Add(distributionTab);
        _tabs.TabPages.Add(stabilityTab);
        _tabs.TabPages.Add(lengthTab);

        Controls.Add(profileLabel);
        Controls.Add(_profileCombo);
        Controls.Add(authorLabel);
        Controls.Add(_authorCombo);
        Controls.Add(_tabs);

        Load += async (_, _) => await LoadProfilesAsync();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task LoadProfilesAsync()
    {
        _profiles = await _runRepo.GetSettingsProfilesAsync();
        _profileCombo.Items.Clear();

        foreach (var p in _profiles) _profileCombo.Items.Add(p.Describe());

        if (_profileCombo.Items.Count > 0)
        {
            _profileCombo.SelectedIndex = 0;
        }
        else
        {
            _summaryLabel.Text = "No saved runs yet. Run an analysis in the Stylometry window and save it, " +
                                 "or use Run All to batch an author's works.";
        }

        await BuildStabilityMatrixAsync();
    }

    private async Task RefreshAsync()
    {
        if (_profileCombo.SelectedIndex < 0 || _profileCombo.SelectedIndex >= _profiles.Count) return;

        var settings = _profiles[_profileCombo.SelectedIndex];
        _currentMetrics = await _runRepo.GetRunMetricsAsync(settingsFilter: settings);

        var authors = _currentMetrics
            .Select(m => m.TargetAuthorName)
            .Distinct()
            .OrderBy(a => a)
            .ToList();

        var previous = _authorCombo.SelectedItem as string;
        _authorCombo.Items.Clear();
        _authorCombo.Items.Add("(all authors)");
        foreach (var a in authors) _authorCombo.Items.Add(a);

        var restored = previous != null ? _authorCombo.Items.IndexOf(previous) : -1;
        _authorCombo.SelectedIndex = restored >= 0 ? restored : 0;

        RenderMetrics();
        RenderLengthConfound();
    }

    /// <summary>
    /// Renders the per-work metrics and, when a single author is selected,
    /// the reference distribution over that author's works.
    ///
    /// The z-score for each work is computed leave-one-out: the mean and
    /// standard deviation come from the OTHER works by that author, not from
    /// all of them. Including a suspected outlier in the distribution it is
    /// being tested against pulls the mean toward it and shrinks its own
    /// z-score - the test would partly hide what it is looking for.
    /// </summary>
    private void RenderMetrics()
    {
        _metricsList.Items.Clear();

        var selectedAuthor = _authorCombo.SelectedItem as string;
        var filtered = (selectedAuthor == null || selectedAuthor.StartsWith("("))
            ? _currentMetrics
            : _currentMetrics.Where(m => m.TargetAuthorName == selectedAuthor).ToList();

        if (filtered.Count == 0)
        {
            _summaryLabel.Text = "No runs for this selection.";
            return;
        }

        var depths = filtered
            .Where(m => m.DepthToFirstOutsider.HasValue)
            .Select(m => (double)m.DepthToFirstOutsider!.Value)
            .ToList();

        foreach (var m in filtered.OrderBy(m => m.DepthToFirstOutsider ?? int.MaxValue))
        {
            double? z = null;

            if (m.DepthToFirstOutsider.HasValue && depths.Count >= 3)
            {
                // Leave-one-out: exclude this work from its own reference set.
                var others = filtered
                    .Where(o => o.RunId != m.RunId && o.DepthToFirstOutsider.HasValue)
                    .Select(o => (double)o.DepthToFirstOutsider!.Value)
                    .ToList();

                if (others.Count >= 2)
                {
                    var mean = others.Average();
                    var sd = Math.Sqrt(others.Select(v => (v - mean) * (v - mean)).Average());
                    if (sd > 1e-9) z = (m.DepthToFirstOutsider.Value - mean) / sd;
                }
            }

            var item = new ListViewItem($"{m.TargetAuthorName}, {m.TargetWorkTitle}");
            item.SubItems.Add(m.DepthToFirstOutsider?.ToString() ?? "(all same author)");
            item.SubItems.Add(z.HasValue ? z.Value.ToString("F2") : "-");
            item.SubItems.Add(m.DeltaFloor.ToString("F3"));
            item.SubItems.Add(m.AuthorPurityAt10.ToString("P0"));
            item.SubItems.Add($"{m.NearestAuthor}, {m.NearestTitle}");

            // Two standard deviations below the leave-one-out mean is a
            // conventional flag, not a verdict. It marks a work worth looking
            // at, and with a dozen or so works one flag is expected by chance.
            if (z.HasValue && z.Value <= -2.0) item.BackColor = Color.FromArgb(255, 235, 235);

            _metricsList.Items.Add(item);
        }

        if (depths.Count >= 3)
        {
            var mean = depths.Average();
            var sd = Math.Sqrt(depths.Select(v => (v - mean) * (v - mean)).Average());
            var floors = filtered.Select(m => m.DeltaFloor).ToList();

            _summaryLabel.Text =
                $"Reference distribution over {depths.Count} runs" +
                (selectedAuthor != null && !selectedAuthor.StartsWith("(") ? $" for {selectedAuthor}" : "") + ":" +
                Environment.NewLine +
                $"  Depth to first outsider - mean {mean:F1}, sd {sd:F1}, range {depths.Min():F0} to {depths.Max():F0}" +
                Environment.NewLine +
                $"  Delta floor - mean {floors.Average():F3}, range {floors.Min():F3} to {floors.Max():F3}" +
                Environment.NewLine + Environment.NewLine +
                "z-scores are leave-one-out: each work is scored against the others, not against a" +
                Environment.NewLine +
                "distribution it is itself part of. Rows shaded pink sit two or more standard deviations" +
                Environment.NewLine +
                "below the mean - worth a closer look, not a conclusion. With a dozen works, one flag is" +
                Environment.NewLine +
                "roughly what chance alone produces." +
                Environment.NewLine + Environment.NewLine +
                "A shallow depth means the analysis stopped agreeing with the attribution early. It does" +
                Environment.NewLine +
                "not distinguish a different author from a short text, an unusual genre, or an edition" +
                Environment.NewLine +
                "prepared to different conventions.";
        }
        else
        {
            _summaryLabel.Text =
                $"{filtered.Count} run(s) shown. At least three runs for one author are needed before a " +
                "reference distribution means anything - batch that author's works from the Stylometry window.";
        }
    }

    /// <summary>
    /// Tests whether depth to first outsider is tracking text length.
    ///
    /// Spearman's rho rather than Pearson's r: the relationship is expected to
    /// be monotonic but not linear (doubling a short text helps its frequency
    /// estimates far more than doubling an already-long one), and rho does not
    /// assume a straight line. It is also unbothered by the one or two extreme
    /// lengths that a small corpus of plays inevitably contains.
    ///
    /// Ranks are averaged over ties, which matters here: depth is a small
    /// integer and ties are common.
    ///
    /// A strong POSITIVE rho means long texts get deep rankings and short texts
    /// shallow ones - the confound. Sign is easy to get backwards, so it is
    /// spelled out in the summary rather than left to the reader.
    /// </summary>
    private void RenderLengthConfound()
    {
        _lengthList.Items.Clear();

        var usable = _currentMetrics
            .Where(m => m.TargetTokenCount.HasValue && m.DepthToFirstOutsider.HasValue)
            .ToList();

        var selectedAuthor = _authorCombo.SelectedItem as string;
        if (selectedAuthor != null && !selectedAuthor.StartsWith("("))
            usable = usable.Where(m => m.TargetAuthorName == selectedAuthor).ToList();

        if (usable.Count < 5)
        {
            _lengthSummary.Text =
                $"{usable.Count} run(s) carry a token count. Runs saved before the token-count column " +
                "was added have none and are excluded rather than counted as zero - re-run the batch " +
                "to populate them. At least five are needed before a correlation means anything.";
            return;
        }

        var lengthRank = AverageRanks(usable.Select(m => (double)m.TargetTokenCount!.Value).ToList());
        var depthRank = AverageRanks(usable.Select(m => (double)m.DepthToFirstOutsider!.Value).ToList());

        var n = usable.Count;
        var meanL = lengthRank.Average();
        var meanD = depthRank.Average();
        var cov = 0d; var varL = 0d; var varD = 0d;
        for (var i = 0; i < n; i++)
        {
            var dl = lengthRank[i] - meanL;
            var dd = depthRank[i] - meanD;
            cov += dl * dd; varL += dl * dl; varD += dd * dd;
        }
        var rho = (varL > 1e-9 && varD > 1e-9) ? cov / Math.Sqrt(varL * varD) : 0d;

        var order = Enumerable.Range(0, n).OrderBy(i => usable[i].TargetTokenCount).ToList();
        foreach (var i in order)
        {
            var m = usable[i];
            var gap = Math.Abs(lengthRank[i] - depthRank[i]);

            var item = new ListViewItem($"{m.TargetAuthorName}, {m.TargetWorkTitle}");
            item.SubItems.Add(m.TargetTokenCount!.Value.ToString("N0"));
            item.SubItems.Add(m.DepthToFirstOutsider!.Value.ToString());
            item.SubItems.Add(lengthRank[i].ToString("F1"));
            item.SubItems.Add(depthRank[i].ToString("F1"));
            item.SubItems.Add(gap.ToString("F1"));
            item.SubItems.Add(m.DeltaFloor.ToString("F3"));

            // Works whose depth rank is far from their length rank are the ones
            // length does NOT explain - the residuals, and the only rows where
            // an authorship reading is even available.
            if (gap >= n / 3.0) item.BackColor = Color.FromArgb(232, 245, 233);

            _lengthList.Items.Add(item);
        }

        string verdict;
        if (rho >= 0.7)
            verdict = "STRONG. Depth is largely a function of text length. A shallow depth is what a " +
                      "short text looks like, and reading it as weak authorial signal is not supported.";
        else if (rho >= 0.4)
            verdict = "MODERATE. Length explains part of the variation. Depth cannot be read directly - " +
                      "length has to be controlled for first, by chunking every text to a common size " +
                      "and re-running.";
        else if (rho > -0.4)
            verdict = "WEAK. Length does not account for the variation, so depth is measuring something " +
                      "else. Whether that something is authorship is a separate question this does not " +
                      "settle.";
        else
            verdict = "NEGATIVE, which is the wrong direction for the length confound and suggests a " +
                      "different effect. Check the token counts before trusting this.";

        _lengthSummary.Text =
            $"Spearman's rho between length and depth = {rho:F3}  (n = {n})" +
            Environment.NewLine + Environment.NewLine +
            verdict +
            Environment.NewLine + Environment.NewLine +
            "Positive rho = longer texts rank deeper, shorter texts shallower - the confound." +
            Environment.NewLine +
            "Green rows are works whose depth rank is far from their length rank. Those are the " +
            "residuals: the cases length does not explain, and the only ones where an authorship" +
            Environment.NewLine +
            "reading is available at all." +
            Environment.NewLine + Environment.NewLine +
            "This is a correlation over a couple of dozen works. It can tell you a confound is " +
            "present; it cannot tell you it is absent.";
    }

    /// <summary>
    /// Ranks smallest to largest, averaging over ties.
    ///
    /// Tie handling is not optional here - depth is a small integer over twenty
    /// works, so ties are the normal case. Assigning them arbitrary distinct
    /// ranks would manufacture correlation out of list order.
    /// </summary>
    private static List<double> AverageRanks(List<double> values)
    {
        var n = values.Count;
        var idx = Enumerable.Range(0, n).OrderBy(i => values[i]).ToList();
        var ranks = new double[n];

        var i2 = 0;
        while (i2 < n)
        {
            var j = i2;
            while (j + 1 < n && Math.Abs(values[idx[j + 1]] - values[idx[i2]]) < 1e-9) j++;

            var avg = (i2 + j) / 2.0 + 1;
            for (var k = i2; k <= j; k++) ranks[idx[k]] = avg;

            i2 = j + 1;
        }

        return ranks.ToList();
    }

    /// <summary>
    /// Builds the works-by-settings matrix of depth-to-first-outsider.
    ///
    /// Runs are grouped by settings profile across the columns, so a work's row
    /// reads as its behaviour under each preprocessing choice. Spread is
    /// reported per row: a low spread means the reading is a property of the
    /// text, a high one means it is a property of the tokeniser.
    /// </summary>
    private async Task BuildStabilityMatrixAsync()
    {
        _stabilityList.Columns.Clear();
        _stabilityList.Items.Clear();

        var all = await _runRepo.GetRunMetricsAsync();
        if (all.Count == 0) return;

        var profiles = all
            .Select(m => m.Settings)
            .DistinctBy(s => s.ProfileKey)
            .OrderBy(s => s.AlgorithmVersion)
            .ThenBy(s => s.FeatureWordCount)
            .ToList();

        _stabilityList.Columns.Add("Work", 260);
        foreach (var p in profiles) _stabilityList.Columns.Add(p.ProfileKey, 150);
        _stabilityList.Columns.Add("Spread", 70);

        var byWork = all
            .GroupBy(m => $"{m.TargetAuthorName}, {m.TargetWorkTitle}")
            .OrderBy(g => g.Key);

        foreach (var group in byWork)
        {
            var item = new ListViewItem(group.Key);
            var values = new List<double>();

            foreach (var p in profiles)
            {
                var match = group.FirstOrDefault(m => m.Settings.ProfileKey == p.ProfileKey);
                if (match?.DepthToFirstOutsider is int d)
                {
                    item.SubItems.Add(d.ToString());
                    values.Add(d);
                }
                else
                {
                    item.SubItems.Add("-");
                }
            }

            // Spread only means something with at least two profiles to compare.
            item.SubItems.Add(values.Count >= 2 ? (values.Max() - values.Min()).ToString("F0") : "-");

            // A work whose depth doubles between settings has not been measured
            // yet - it has been measured twice, differently.
            if (values.Count >= 2 && values.Max() - values.Min() > values.Min())
                item.BackColor = Color.FromArgb(255, 248, 225);

            _stabilityList.Items.Add(item);
        }
    }
}
