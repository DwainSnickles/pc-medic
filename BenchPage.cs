namespace PC_Medic;

internal sealed class BenchPage : PageBase
{
    private readonly List<(ToolTile Tile, PortableApp App)> _tiles = new();
    private readonly TableLayoutPanel _grid;
    private readonly Label _status = new();
    private readonly ToolTip _tips = new();
    private readonly NeonButton _getMissing = Button("GET ALL MISSING", width: 210, height: 28);
    private readonly NeonButton _findMissing = Button("SEARCH THIS PC", width: 168, height: 28);

    public BenchPage()
        : base("BENCHMARKS", "Portable test rigs, detected beside the executable.")
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0),
        };
        for (int i = 0; i < 3; i++) _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        for (int i = 0; i < 2; i++) _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        foreach (PortableApp app in Tools.Portables)
        {
            var tile = new ToolTile
            {
                Glyph = app.Glyph,
                Mark = app.Mark,
                Title = app.Title,
                Subtitle = app.Note,
                Tone = Theme.Accent,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 10),
                Tag = app,
            };

            tile.Click += (sender, _) =>
            {
                if (sender is ToolTile clicked && clicked.Tag is PortableApp target)
                {
                    Tools.LaunchPortable(target);
                    RefreshStatuses();
                }
            };

            tile.ShowFinder = true;
            tile.FinderClicked += (sender, _) =>
            {
                if (sender is ToolTile source && source.Tag is PortableApp target) BrowseForRig(target);
            };

            tile.ContextMenuStrip = BuildRigMenu(app);
            _tips.SetToolTip(tile, $"Click to open \u2022 right-click to set the {app.ExeName} location");
            _tiles.Add((tile, app));
        }

        var folder = new ToolTile
        {
            Glyph = "\uE8B7",
            Mark = RigMark.Folder,
            Title = "SUITE FOLDER",
            Subtitle = "Drop portable test rigs here",
            Status = "PATH",
            StatusTone = Theme.TextDim,
            ActionText = "OPEN \u2197",
            ActionTone = Theme.Accent,
            Tone = Theme.Violet,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 10),
        };

        folder.Click += (_, _) => Tools.Launch(Tools.PortableRoot);

        int index = 0;
        foreach ((ToolTile tile, PortableApp _) in _tiles)
        {
            _grid.Controls.Add(tile, index % 3, index / 3);
            index++;
        }

        _grid.Controls.Add(folder, index % 3, index / 3);

        root.Controls.Add(_grid, 0, 0);

        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.AutoSize = true;
        _status.Location = new Point(2, 6);

        var pathLabel = new Label
        {
            Text = Tools.PortableRoot,
            Font = Theme.MonoSmall,
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, 0, 0),
        };

        _tips.SetToolTip(pathLabel, Tools.PortableRoot);

        root.Controls.Add(pathLabel, 0, 2);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0) };
        _status.Location = new Point(2, 7);
        footer.Controls.Add(_status);
        footer.Controls.Add(_findMissing);
        footer.Controls.Add(_getMissing);

        void LayoutFooter()
        {
            _getMissing.SetBounds(footer.ClientSize.Width - _getMissing.Width, 1, _getMissing.Width, 28);
            _findMissing.SetBounds(
                Math.Max(0, _getMissing.Left - _findMissing.Width - 8), 1, _findMissing.Width, 28);
        }

        footer.Resize += (_, _) => LayoutFooter();
        LayoutFooter();
        root.Controls.Add(footer, 0, 1);

        _getMissing.Click += (_, _) => OpenMissingDownloads();
        _tips.SetToolTip(_getMissing, "Installs every missing rig through winget, one progress bar per download.");

        _findMissing.Click += (_, _) => OpenMissingSearch();
        _tips.SetToolTip(
            _findMissing,
            "Walks every drive, plus the registry and winget, for the missing rigs \u2014 a copy already on D: needs no download.");

        Controls.Add(root);
        RefreshStatuses();
    }

    public override void OnNavigated() => RefreshStatuses();

    public override void RefreshData() => RefreshStatuses();

    private void RefreshStatuses()
    {
        int ready = 0;
        int customCount = 0;
        foreach ((ToolTile tile, PortableApp app) in _tiles)
        {
            string? resolved = Tools.ResolvePortable(app);
            string? stored = Tools.StoredPath(app);
            bool found = resolved is not null;
            bool isCustom = found && stored is not null &&
                            string.Equals(resolved, stored, StringComparison.OrdinalIgnoreCase);
            bool stale = !found && stored is not null;
            if (found) ready++;
            if (isCustom) customCount++;

            tile.Status = found ? (isCustom ? "CUSTOM" : "READY") : "MISSING";
            tile.StatusTone = found ? (isCustom ? Theme.Violet : Theme.Good) : Theme.Warn;
            tile.ActionText = found ? "OPEN \u2197" : "INSTALL \u2197";
            tile.ActionTone = found ? Theme.Accent : Theme.Violet;
            tile.Subtitle = found
                ? (isCustom ? $"Custom \u2022 {ShortPath(resolved!)}" : app.Note)
                : stale
                    ? "Remembered path is gone \u2022 browse again"
                    : $"Not in {app.Folder}";
            tile.Missing = !found;
            tile.LinkText = found ? string.Empty : $"{Host(app.Url)}  \u2197";
            tile.LinkTone = found ? Theme.TextFaint : Theme.Violet;

            _tips.SetToolTip(
                tile,
                found
                    ? $"Click to open \u2022 the magnifier browses for its folder \u2022 right-click for more"
                    : $"Click for the install link \u2022 the magnifier browses for it \u2022 {app.Url}");

            tile.Invalidate();
        }

        int missing = _tiles.Count - ready;
        string customSuffix = customCount > 0 ? $"  \u2022  {customCount} CUSTOM" : string.Empty;
        _status.Text = missing == 0
            ? $"{ready} / {_tiles.Count} RIGS DETECTED{customSuffix}  \u2022  ALL FREE / OPEN-SOURCE"
            : $"{ready} / {_tiles.Count} RIGS DETECTED{customSuffix}  \u2022  {missing} MISSING  \u2022  CLICK GET FOR THE INSTALL LINK";
        _status.ForeColor = ready == _tiles.Count ? Theme.Good : Theme.TextDim;

        _getMissing.Text = missing == 0 ? "ALL RIGS READY" : $"INSTALL ALL MISSING ({missing})";
        _getMissing.Enabled = missing > 0;
        _getMissing.Invalidate();

        _findMissing.Enabled = missing > 0;
        _findMissing.Invalidate();
    }

    /// <summary>
    /// Installs every rig that is not present yet, one winget package at a time, with a live bar for
    /// each download. The grid is re-detected afterwards, so installed rigs stop reading as missing.
    /// </summary>
    private void OpenMissingDownloads()
    {
        List<PortableApp> missing = Tools.Portables
            .Where(app => Tools.ResolvePortable(app) is null)
            .ToList();

        if (missing.Count == 0)
        {
            ActivityLog.Info("Every benchmark rig is already present.");
            RefreshStatuses();
            return;
        }

        if (UpdatesService.WingetPath is null)
        {
            // Without winget there is nothing to drive a download, so the install pages remain the offer.
            int opened = 0;

            foreach (PortableApp app in missing)
            {
                (bool ok, string message) = Tools.OpenUrl(app.Url);
                if (ok)
                {
                    opened++;
                    ActivityLog.Success($"{app.Title}: download page opened.");
                }
                else
                {
                    ActivityLog.Error($"{app.Title}: {message}");
                }
            }

            ActivityLog.Warn($"winget is not installed \u2014 opened {opened} of {missing.Count} install pages instead.");
            RefreshStatuses();
            return;
        }

        using var setup = new RigSetupForm(missing);
        setup.ShowDialog(FindForm());

        if (setup.NeedsSearch.Count > 0)
        {
            ActivityLog.Warn(
                $"{setup.NeedsSearch.Count} rig(s) were installed but no executable was found \u2014 use the magnifier on their tile to browse to it.");
        }

        ActivityLog.Info(setup.InstalledAnything
            ? "Rig setup finished \u2014 rescanning the suite."
            : "Rig setup closed without installing anything.");

        RefreshStatuses();
    }

    /// <summary>
    /// Finds rigs that are already on disk but outside the portable folder, which is the usual case
    /// when the console lives on a USB stick and the rigs were installed elsewhere. Nothing is
    /// downloaded: the hits are remembered by path and the grid re-detects them.
    /// </summary>
    private void OpenMissingSearch()
    {
        List<PortableApp> missing = Tools.Portables
            .Where(app => Tools.ResolvePortable(app) is null)
            .ToList();

        if (missing.Count == 0)
        {
            ActivityLog.Info("Every benchmark rig is already present.");
            RefreshStatuses();
            return;
        }

        using var search = new RigSearchForm(missing);
        search.ShowDialog(FindForm());

        ActivityLog.Info(search.FoundAnything
            ? "Rig search finished \u2014 rescanning the suite."
            : "Rig search found nothing \u2014 install, or set the location by hand.");

        RefreshStatuses();
    }

    private void SearchForRig(PortableApp app)
    {
        using var search = new RigSearchForm(new[] { app });
        search.ShowDialog(FindForm());

        if (search.FoundAnything) ActivityLog.Info($"{app.Title}: rescanning the suite.");
        RefreshStatuses();
    }

    /// <summary>Right-click menu: point a rig at any executable on disk, or reset it.</summary>
    private ContextMenuStrip BuildRigMenu(PortableApp app)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.Surface2,
            ForeColor = Theme.Text,
            Font = Theme.Ui,
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
        };

        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            menu.Items.Add("Open install page \u2197", null, (_, _) => OpenInstallPage(app));
            menu.Items.Add("Copy install link", null, (_, _) => CopyInstallLink(app));

            if (Tools.ResolvePortable(app) is null)
            {
                menu.Items.Add("Search this PC for it\u2026", null, (_, _) => SearchForRig(app));
            }

            menu.Items.Add("Set install location\u2026", null, (_, _) => PickCustomPath(app));

            string? stored = Tools.StoredPath(app);
            if (stored is null) return;

            menu.Items.Add("Use portable folder instead", null, (_, _) =>
            {
                Tools.ClearCustomPath(app);
                ActivityLog.Info($"{app.Title}: back to the portable folder.");
                RefreshStatuses();
            });

            menu.Items.Add("Open containing folder", null, (_, _) =>
            {
                string? folder = Path.GetDirectoryName(stored);
                if (folder is not null) Tools.Launch(folder);
            });
        };

        return menu;
    }

    /// <summary>
    /// Browses straight to the folder a rig was installed into. The picked folder is searched for the
    /// rig's executable and then remembered, so a rig kept off C: never needs downloading again.
    /// </summary>
    private void BrowseForRig(PortableApp app)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Pick the folder that holds {app.ExeName}",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Tools.PortableRoot,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string? found = LocateIn(dialog.SelectedPath, app.ExeName);

        if (found is null)
        {
            ActivityLog.Warn($"{app.Title}: no {app.ExeName} anywhere under {ShortPath(dialog.SelectedPath)}.");
            RefreshStatuses();
            return;
        }

        (bool ok, string message) = Tools.SetCustomPath(app, found);

        if (ok) ActivityLog.Success($"{app.Title} now points at {ShortPath(found)}.");
        else ActivityLog.Error($"{app.Title}: {message}");

        RefreshStatuses();
    }

    /// <summary>The executable straight in the picked folder, else the first one below it.</summary>
    private static string? LocateIn(string folder, string exeName)
    {
        try
        {
            string direct = Path.Combine(folder, exeName);
            if (File.Exists(direct)) return direct;

            foreach (string candidate in Directory.EnumerateFiles(folder, exeName, SearchOption.AllDirectories))
            {
                return candidate;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            ActivityLog.Warn($"{ShortPath(folder)}: {ex.Message}");
        }

        return null;
    }

    private void PickCustomPath(PortableApp app)
    {
        string? stored = Tools.StoredPath(app);
        using var dialog = new OpenFileDialog
        {
            Title = $"Locate {app.ExeName}",
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            FileName = app.ExeName,
            InitialDirectory = stored is not null && File.Exists(stored)
                ? Path.GetDirectoryName(stored) ?? Tools.PortableRoot
                : Tools.PortableRoot,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        (bool ok, string message) = Tools.SetCustomPath(app, dialog.FileName);
        if (ok)
        {
            ActivityLog.Success($"{app.Title} now points at {ShortPath(message)}.");
        }
        else
        {
            ActivityLog.Error($"{app.Title}: {message}");
        }

        RefreshStatuses();
    }

    private void OpenInstallPage(PortableApp app)
    {
        (bool ok, string message) = Tools.OpenUrl(app.Url);

        if (ok) ActivityLog.Info($"{app.Title}: install page opened.");
        else ActivityLog.Error($"{app.Title}: {message}");
    }

    private void CopyInstallLink(PortableApp app)
    {
        try
        {
            Clipboard.SetText(app.Url);
            ActivityLog.Success($"{app.Title}: install link copied.");
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"{app.Title}: clipboard unavailable ({ex.Message}).");
        }
    }

    private static string ShortPath(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        string parent = folder is null ? string.Empty : Path.GetFileName(folder);
        return string.IsNullOrEmpty(parent) ? path : Path.Combine(parent, Path.GetFileName(path));
    }

    private static string Host(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? uri.Host.Replace("www.", string.Empty)
            : url;

}
