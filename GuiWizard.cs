using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

internal static class GuiWizardApplication
{
    public static void Run(string? configPathOverride)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new GuiWizardForm(configPathOverride));
            }
            catch (Exception ex)
            {
                failure = ex;
                MessageBox.Show(ex.Message, "REE-Content-Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }
}

internal sealed class GuiWizardForm : Form
{
    private const string ReePakToolProjectsRawBaseUrl = "https://raw.githubusercontent.com/Ekey/REE.PAK.Tool/refs/heads/main/Projects/";

    private readonly string configPath;
    private WizardConfig config;
    private Process? runningProcess;

    private readonly Label currentGameLabel = new();
    private readonly ComboBox gameCombo = new();
    private readonly TextBox extractRootText = new();
    private readonly TextBox exportRootText = new();
    private readonly TextBox blenderPathText = new();
    private readonly TextBox meshText = new();
    private readonly ListBox additionalMeshList = new();
    private readonly CheckBox includeAnimationsCheck = new() { Text = "Include animations" };
    private readonly ComboBox animationSourceCombo = new();
    private readonly TextBox motlistDirText = new();
    private readonly ListBox animationFileList = new();
    private readonly TextBox animationFilterText = new();
    private readonly ComboBox outputFormatCombo = new();
    private readonly ComboBox textureFormatCombo = new();
    private readonly NumericUpDown fbxScaleInput = new();
    private readonly CheckBox splitMotlistsCheck = new() { Text = "Split by MOTLIST" };
    private readonly CheckBox splitAnimationsCheck = new() { Text = "Split animations" };
    private readonly CheckBox noTexturesCheck = new() { Text = "No textures" };
    private readonly CheckBox includeLodsCheck = new() { Text = "Include LODs" };
    private readonly CheckBox includeOcclusionCheck = new() { Text = "Include occlusion" };
    private readonly CheckBox noPlaceholderBonesCheck = new() { Text = "Skip missing bone channels" };
    private readonly CheckBox allowMissingStreamingCheck = new() { Text = "Allow missing streaming buffers" };
    private readonly TextBox outputPathText = new();
    private readonly TextBox commandPreviewText = new();
    private readonly TextBox logText = new();
    private readonly ProgressBar progressBar = new();
    private readonly Button runButton = new() { Text = "Run Export" };
    private readonly Button cancelButton = new() { Text = "Cancel", Enabled = false };
    private Button? saveGameButton;
    private Button? changeGameButton;

    public GuiWizardForm(string? configPathOverride)
    {
        configPath = ResolveConfigPath(configPathOverride);
        config = LoadConfig(configPath) ?? new WizardConfig();

        Text = "REE-Content-Exporter Wizard";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 760);
        Size = new Size(1180, 860);

        BuildLayout();
        LoadConfigIntoControls();
        UpdateGameUi();
        UpdateCommandPreview();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 348));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);

        root.Controls.Add(BuildGamePanel(), 0, 0);
        root.Controls.Add(BuildPathPanel(), 0, 1);
        root.Controls.Add(BuildExportPanel(), 0, 2);
        root.Controls.Add(BuildLogPanel(), 0, 3);
        root.Controls.Add(BuildActionPanel(), 0, 4);
    }

    private Control BuildGamePanel()
    {
        var panel = CreateGroup("Game configuration");
        var grid = CreateGrid(4);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.Controls.Add(grid);

        currentGameLabel.AutoSize = true;
        currentGameLabel.TextAlign = ContentAlignment.MiddleLeft;
        gameCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        gameCombo.DisplayMember = nameof(WizardGameDefinition.DisplayName);
        gameCombo.ValueMember = nameof(WizardGameDefinition.Id);
        gameCombo.Items.AddRange(WizardGames.Definitions.Cast<object>().ToArray());
        gameCombo.SelectedIndexChanged += (_, _) => UpdateCommandPreview();

        saveGameButton = new Button { Text = "Save Game" };
        saveGameButton.Click += async (_, _) => await SaveSelectedGameAsync();
        changeGameButton = new Button { Text = "Change Game" };
        changeGameButton.Click += (_, _) => ClearSelectedGame();

        grid.Controls.Add(new Label { Text = "Current", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(currentGameLabel, 1, 0);
        grid.Controls.Add(changeGameButton, 2, 0);
        grid.Controls.Add(saveGameButton, 3, 0);
        grid.Controls.Add(new Label { Text = "Select game", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(gameCombo, 1, 1);
        grid.SetColumnSpan(gameCombo, 3);
        return panel;
    }

    private Control BuildPathPanel()
    {
        var panel = CreateGroup("Paths");
        var grid = CreateGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        panel.Controls.Add(grid);

        AddPathRow(grid, 0, "Extract root", extractRootText, () => BrowseFolder(extractRootText));
        AddPathRow(grid, 1, "Export folder", exportRootText, () => BrowseFolder(exportRootText));
        AddPathRow(grid, 2, "Blender 4.5.9", blenderPathText, () => BrowseFile(blenderPathText, "blender.exe|blender.exe|Executable|*.exe|All files|*.*"));
        return panel;
    }

    private Control BuildExportPanel()
    {
        var panel = CreateGroup("Export setup");
        var grid = CreateGrid(8);
        grid.RowStyles[1] = new RowStyle(SizeType.Absolute, 64);
        grid.RowStyles[4] = new RowStyle(SizeType.Absolute, 64);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        panel.Controls.Add(grid);

        AddPathRow(grid, 0, "Primary mesh", meshText, () => BrowseFile(meshText, "RE Engine mesh|*.mesh*|All files|*.*"));
        var findMeshButton = new Button { Text = "Find in list" };
        findMeshButton.Click += (_, _) => PickAssetFromList(meshText, AssetPickerKind.Mesh);
        grid.Controls.Add(findMeshButton, 3, 0);

        grid.Controls.Add(new Label { Text = "Additional meshes", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        additionalMeshList.Height = 56;
        grid.Controls.Add(additionalMeshList, 1, 1);
        grid.SetColumnSpan(additionalMeshList, 2);
        var addMeshButton = new Button { Text = "Add" };
        addMeshButton.Click += (_, _) => AddAdditionalMesh();
        var removeMeshButton = new Button { Text = "Remove" };
        removeMeshButton.Click += (_, _) => RemoveSelectedAdditionalMesh();
        var meshButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        meshButtons.Controls.Add(addMeshButton);
        meshButtons.Controls.Add(removeMeshButton);
        grid.Controls.Add(meshButtons, 3, 1);
        grid.SetColumnSpan(meshButtons, 2);

        includeAnimationsCheck.CheckedChanged += (_, _) => UpdateAnimationSourceUi();
        grid.Controls.Add(includeAnimationsCheck, 0, 2);
        grid.Controls.Add(new Label { Text = "Animation source", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 2);
        animationSourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        animationSourceCombo.Items.AddRange(["MOTLIST folder", "MOTLIST files", "MOT files"]);
        animationSourceCombo.SelectedIndex = 0;
        animationSourceCombo.SelectedIndexChanged += (_, _) => UpdateAnimationSourceUi();
        grid.Controls.Add(animationSourceCombo, 2, 2);
        grid.SetColumnSpan(animationSourceCombo, 2);

        AddPathRow(grid, 3, "MOTLIST folder", motlistDirText, () => BrowseFolder(motlistDirText));
        var findMotlistButton = new Button { Text = "Find folder" };
        findMotlistButton.Click += (_, _) => PickAssetFromList(motlistDirText, AssetPickerKind.MotlistDirectory);
        grid.Controls.Add(findMotlistButton, 3, 3);

        grid.Controls.Add(new Label { Text = "Animation files", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        animationFileList.Height = 56;
        grid.Controls.Add(animationFileList, 1, 4);
        grid.SetColumnSpan(animationFileList, 2);
        var addAnimationFileButton = new Button { Text = "Add" };
        addAnimationFileButton.Click += (_, _) => AddAnimationFileFromDisk();
        var findAnimationFileButton = new Button { Text = "Find in list" };
        findAnimationFileButton.Click += (_, _) => PickAnimationFileFromList();
        var removeAnimationFileButton = new Button { Text = "Remove" };
        removeAnimationFileButton.Click += (_, _) => RemoveSelectedAnimationFile();
        var animationButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        animationButtons.Controls.Add(addAnimationFileButton);
        animationButtons.Controls.Add(findAnimationFileButton);
        animationButtons.Controls.Add(removeAnimationFileButton);
        grid.Controls.Add(animationButtons, 3, 4);
        grid.SetColumnSpan(animationButtons, 2);

        grid.Controls.Add(new Label { Text = "Animation filter", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        grid.Controls.Add(animationFilterText, 1, 5);
        outputFormatCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        outputFormatCombo.Items.AddRange(["fbx", "glb"]);
        outputFormatCombo.SelectedIndex = 0;
        textureFormatCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        textureFormatCombo.Items.AddRange(["png", "dds"]);
        textureFormatCombo.SelectedIndex = 0;
        fbxScaleInput.DecimalPlaces = 2;
        fbxScaleInput.Minimum = 0.01M;
        fbxScaleInput.Maximum = 1000M;
        fbxScaleInput.Value = 100M;
        var optionFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        optionFlow.Controls.AddRange([
            new Label { Text = "Output", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
            outputFormatCombo,
            new Label { Text = "Textures", AutoSize = true, Padding = new Padding(12, 6, 0, 0) },
            textureFormatCombo,
            new Label { Text = "FBX scale", AutoSize = true, Padding = new Padding(12, 6, 0, 0) },
            fbxScaleInput,
        ]);
        grid.Controls.Add(optionFlow, 2, 5);
        grid.SetColumnSpan(optionFlow, 3);

        var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        checks.Controls.AddRange([splitMotlistsCheck, splitAnimationsCheck, noTexturesCheck, includeLodsCheck, includeOcclusionCheck, noPlaceholderBonesCheck, allowMissingStreamingCheck]);
        grid.Controls.Add(checks, 0, 6);
        grid.SetColumnSpan(checks, 5);

        AddPathRow(grid, 7, "Output path", outputPathText, () => BrowseSaveOutput());

        foreach (Control control in EnumerateControls(grid))
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.TextChanged += (_, _) => UpdateCommandPreview();
                    break;
                case ComboBox comboBox:
                    comboBox.SelectedIndexChanged += (_, _) => UpdateCommandPreview();
                    break;
                case CheckBox checkBox:
                    checkBox.CheckedChanged += (_, _) => UpdateCommandPreview();
                    break;
                case NumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => UpdateCommandPreview();
                    break;
            }
        }

        UpdateAnimationSourceUi();
        return panel;
    }

    private Control BuildLogPanel()
    {
        var panel = CreateGroup("Progress and command");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(grid);

        progressBar.Dock = DockStyle.Fill;
        commandPreviewText.Dock = DockStyle.Fill;
        commandPreviewText.Multiline = true;
        commandPreviewText.ReadOnly = true;
        commandPreviewText.ScrollBars = ScrollBars.Vertical;
        logText.Dock = DockStyle.Fill;
        logText.Multiline = true;
        logText.ReadOnly = true;
        logText.ScrollBars = ScrollBars.Both;
        logText.WordWrap = false;

        grid.Controls.Add(progressBar, 0, 0);
        grid.Controls.Add(commandPreviewText, 0, 1);
        grid.Controls.Add(logText, 0, 2);
        return panel;
    }

    private Control BuildActionPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        runButton.Width = 120;
        cancelButton.Width = 100;
        runButton.Click += async (_, _) => await RunExportAsync();
        cancelButton.Click += (_, _) => CancelExport();
        var saveConfigButton = new Button { Text = "Save Paths", Width = 100 };
        saveConfigButton.Click += (_, _) => SavePathConfig();
        var copyCommandButton = new Button { Text = "Copy Command", Width = 120 };
        copyCommandButton.Click += (_, _) => Clipboard.SetText(commandPreviewText.Text);
        panel.Controls.Add(runButton);
        panel.Controls.Add(cancelButton);
        panel.Controls.Add(copyCommandButton);
        panel.Controls.Add(saveConfigButton);
        return panel;
    }

    private static GroupBox CreateGroup(string title)
        => new() { Text = title, Dock = DockStyle.Fill, Padding = new Padding(10) };

    private static TableLayoutPanel CreateGrid(int rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = rows };
        for (var i = 0; i < rows; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        return grid;
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in EnumerateControls(child)) yield return grandchild;
        }
    }

    private static void AddPathRow(TableLayoutPanel grid, int row, string label, TextBox textBox, Action browse, int startColumn = 0)
    {
        textBox.Dock = DockStyle.Fill;
        var browseButton = new Button { Text = "Browse" };
        browseButton.Click += (_, _) => browse();
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, startColumn, row);
        grid.Controls.Add(textBox, startColumn + 1, row);
        grid.SetColumnSpan(textBox, startColumn == 0 ? 1 : 2);
        grid.Controls.Add(browseButton, startColumn == 0 ? 2 : 3, row);
    }

    private void LoadConfigIntoControls()
    {
        extractRootText.Text = config.ExtractRoot;
        exportRootText.Text = config.DefaultExportRoot;
        blenderPathText.Text = config.BlenderPath;
        textureFormatCombo.SelectedItem = string.IsNullOrWhiteSpace(config.TextureFormat) ? "png" : config.TextureFormat;
        TrySelectConfiguredGame();
    }

    private bool TrySelectConfiguredGame()
    {
        if (string.IsNullOrWhiteSpace(config.Game)) return false;
        for (var i = 0; i < WizardGames.Definitions.Count; i++)
        {
            if (WizardGames.Definitions[i].Id.Equals(config.Game, StringComparison.OrdinalIgnoreCase))
            {
                if (i < gameCombo.Items.Count) gameCombo.SelectedIndex = i;
                return true;
            }
        }
        return false;
    }

    private void UpdateGameUi()
    {
        if (TryGetConfiguredGame(out var game))
        {
            currentGameLabel.Text = $"{game.DisplayName} ({game.Id}) - delete the \"game\" line from config.json or click Change Game to set a different game.";
            gameCombo.Enabled = false;
            if (saveGameButton != null) saveGameButton.Enabled = false;
            if (changeGameButton != null) changeGameButton.Enabled = true;
        }
        else if (!string.IsNullOrWhiteSpace(config.Game))
        {
            currentGameLabel.Text = $"Unsupported saved game: {config.Game}. Click Change Game or delete the \"game\" line from config.json.";
            gameCombo.Enabled = false;
            if (saveGameButton != null) saveGameButton.Enabled = false;
            if (changeGameButton != null) changeGameButton.Enabled = true;
        }
        else
        {
            currentGameLabel.Text = "No game saved yet. Select a game and click Save Game.";
            gameCombo.Enabled = true;
            if (saveGameButton != null) saveGameButton.Enabled = true;
            if (changeGameButton != null) changeGameButton.Enabled = false;
        }
    }

    private bool TryGetConfiguredGame(out WizardGameDefinition game)
    {
        if (!string.IsNullOrWhiteSpace(config.Game))
        {
            foreach (var candidate in WizardGames.Definitions)
            {
                if (candidate.Id.Equals(config.Game, StringComparison.OrdinalIgnoreCase))
                {
                    game = candidate;
                    return true;
                }
            }
        }
        game = null!;
        return false;
    }

    private bool TryGetSelectedGame(out WizardGameDefinition game)
    {
        if (gameCombo.SelectedItem is WizardGameDefinition selected)
        {
            game = selected;
            return true;
        }
        game = null!;
        return false;
    }

    private async Task SaveSelectedGameAsync()
    {
        if (!TryGetSelectedGame(out var game)) return;
        try
        {
            var listPath = ResolveListPath(game);
            await DownloadGameListAsync(game, listPath);
            config.Game = game.Id;
            config.GameDisplayName = game.DisplayName;
            config.GameListFile = game.ListFileName;
            config.GameListPath = listPath;
            SaveConfig();
            UpdateGameUi();
            UpdateCommandPreview();
            AppendLog($"Saved game configuration: {game.DisplayName}");
        }
        catch (Exception ex)
        {
            AppendLog("Game configuration failed: " + ex.Message);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearSelectedGame()
    {
        config.Game = "";
        config.GameDisplayName = "";
        config.GameListFile = "";
        config.GameListPath = "";
        SaveConfig();
        UpdateGameUi();
        UpdateCommandPreview();
        AppendLog("Cleared game configuration. Select a game and click Save Game.");
    }

    private void SavePathConfig()
    {
        config.ExtractRoot = extractRootText.Text.Trim();
        config.DefaultExportRoot = exportRootText.Text.Trim();
        config.BlenderPath = blenderPathText.Text.Trim();
        config.TextureFormat = (textureFormatCombo.SelectedItem?.ToString() ?? "png").ToLowerInvariant();
        SaveConfig();
        AppendLog("Saved path and texture settings.");
    }

    private void SaveConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? ".");
        config.UpdatedUtc = DateTimeOffset.UtcNow;
        if (config.CreatedUtc == default) config.CreatedUtc = config.UpdatedUtc;
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task DownloadGameListAsync(WizardGameDefinition game, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");
        var url = ReePakToolProjectsRawBaseUrl + Uri.EscapeDataString(game.ListFileName);
        AppendLog($"Downloading {game.ListFileName}");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("REE-Content-Exporter/0.5");
        var bytes = await http.GetByteArrayAsync(url);
        if (bytes.Length == 0) throw new InvalidOperationException($"Downloaded list was empty: {url}");
        await File.WriteAllBytesAsync(targetPath, bytes);
        AppendLog($"Saved game list: {targetPath}");
    }

    private string ResolveListPath(WizardGameDefinition game)
        => Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "lists", game.ListFileName);

    private IReadOnlyList<string> LoadConfiguredListLines()
    {
        if (string.IsNullOrWhiteSpace(config.GameListPath) || !File.Exists(config.GameListPath))
            return [];
        return File.ReadLines(config.GameListPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private void PickAssetFromList(TextBox target, AssetPickerKind kind)
    {
        SavePathConfig();
        var entries = LoadConfiguredListLines();
        if (entries.Count == 0)
        {
            MessageBox.Show("Save a game first so its REE.PAK.Tool list can be downloaded.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var picker = new AssetPickerForm(entries, extractRootText.Text.Trim(), kind);
        if (picker.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(picker.SelectedPath))
        {
            target.Text = picker.SelectedPath;
        }
    }

    private void AddAdditionalMesh()
    {
        using var dialog = new OpenFileDialog { Filter = "RE Engine mesh|*.mesh*|All files|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK && !additionalMeshList.Items.Contains(dialog.FileName))
        {
            additionalMeshList.Items.Add(dialog.FileName);
            UpdateCommandPreview();
        }
    }

    private void AddAnimationFileFromDisk()
    {
        var mode = GetAnimationSourceMode();
        var filter = mode == GuiAnimationSourceMode.MotFiles
            ? "RE Engine MOT|*.mot*|All files|*.*"
            : "RE Engine MOTLIST|*.motlist*|All files|*.*";
        using var dialog = new OpenFileDialog { Filter = filter, Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var file in dialog.FileNames)
        {
            if (!animationFileList.Items.Contains(file)) animationFileList.Items.Add(file);
        }
        UpdateCommandPreview();
    }

    private void PickAnimationFileFromList()
    {
        var mode = GetAnimationSourceMode();
        if (mode == GuiAnimationSourceMode.MotlistDirectory)
        {
            PickAssetFromList(motlistDirText, AssetPickerKind.MotlistDirectory);
            return;
        }

        SavePathConfig();
        var entries = LoadConfiguredListLines();
        if (entries.Count == 0)
        {
            MessageBox.Show("Save a game first so its REE.PAK.Tool list can be downloaded.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var pickerKind = mode == GuiAnimationSourceMode.MotFiles ? AssetPickerKind.MotFile : AssetPickerKind.MotlistFile;
        using var picker = new AssetPickerForm(entries, extractRootText.Text.Trim(), pickerKind);
        if (picker.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(picker.SelectedPath) && !animationFileList.Items.Contains(picker.SelectedPath))
        {
            animationFileList.Items.Add(picker.SelectedPath);
            UpdateCommandPreview();
        }
    }

    private void RemoveSelectedAnimationFile()
    {
        var selected = animationFileList.SelectedItem;
        if (selected != null)
        {
            animationFileList.Items.Remove(selected);
            UpdateCommandPreview();
        }
    }

    private void RemoveSelectedAdditionalMesh()
    {
        var selected = additionalMeshList.SelectedItem;
        if (selected != null)
        {
            additionalMeshList.Items.Remove(selected);
            UpdateCommandPreview();
        }
    }

    private void BrowseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(target.Text) ? target.Text : "" };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void BrowseFile(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, FileName = target.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private void BrowseSaveOutput()
    {
        var ext = outputFormatCombo.SelectedItem?.ToString() ?? "fbx";
        using var dialog = new SaveFileDialog
        {
            Filter = ext == "fbx" ? "FBX|*.fbx|All files|*.*" : "GLB|*.glb|All files|*.*",
            DefaultExt = ext,
            FileName = string.IsNullOrWhiteSpace(meshText.Text)
                ? $"export.{ext}"
                : $"{Path.GetFileNameWithoutExtension(meshText.Text)}.{ext}",
        };
        if (Directory.Exists(exportRootText.Text)) dialog.InitialDirectory = exportRootText.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) outputPathText.Text = dialog.FileName;
    }

    private List<string> BuildExportArgs()
    {
        if (string.IsNullOrWhiteSpace(config.Game)) throw new InvalidOperationException("Save the game configuration before exporting.");
        if (!TryGetConfiguredGame(out var game)) throw new InvalidOperationException($"Saved game is not supported: {config.Game}");
        if (string.IsNullOrWhiteSpace(meshText.Text)) throw new InvalidOperationException("Select a primary mesh.");
        var output = ResolveOutputPath();
        var args = new List<string>
        {
            "--game", game.Id,
            "--mesh", meshText.Text.Trim(),
            "--texture-format", textureFormatCombo.SelectedItem?.ToString() ?? "png",
            "--fbx-scale", fbxScaleInput.Value.ToString("0.##"),
            "--output", output,
        };
        foreach (var item in additionalMeshList.Items.Cast<object>().Select(item => item.ToString()).Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            args.Add("--additional-mesh");
            args.Add(item!);
        }
        if (!includeAnimationsCheck.Checked)
        {
            args.Add("--no-animations");
        }
        else
        {
            switch (GetAnimationSourceMode())
            {
                case GuiAnimationSourceMode.MotlistDirectory:
                    if (string.IsNullOrWhiteSpace(motlistDirText.Text)) throw new InvalidOperationException("Select a MOTLIST folder or turn off animations.");
                    args.Add("--motlist-dir");
                    args.Add(motlistDirText.Text.Trim());
                    if (splitMotlistsCheck.Checked) args.Add("--split-motlists");
                    break;
                case GuiAnimationSourceMode.MotlistFiles:
                    AddAnimationFileArgs(args, "--motlist");
                    if (splitAnimationsCheck.Checked) args.Add("--split-animations");
                    break;
                case GuiAnimationSourceMode.MotFiles:
                    AddAnimationFileArgs(args, "--mot");
                    if (splitAnimationsCheck.Checked) args.Add("--split-animations");
                    break;
            }
        }
        if (!string.IsNullOrWhiteSpace(animationFilterText.Text))
        {
            args.Add("--animation-name");
            args.Add(animationFilterText.Text.Trim());
        }
        if (noTexturesCheck.Checked) args.Add("--no-textures");
        if (includeLodsCheck.Checked) args.Add("--include-lods");
        if (includeOcclusionCheck.Checked) args.Add("--include-occlusion");
        if (noPlaceholderBonesCheck.Checked) args.Add("--no-placeholder-animation-bones");
        if (allowMissingStreamingCheck.Checked) args.Add("--allow-missing-streaming");
        return args;
    }

    private void AddAnimationFileArgs(List<string> args, string option)
    {
        var files = animationFileList.Items.Cast<object>()
            .Select(item => item.ToString())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0) throw new InvalidOperationException($"Select one or more {(option == "--mot" ? "MOT" : "MOTLIST")} files or turn off animations.");
        foreach (var file in files)
        {
            args.Add(option);
            args.Add(file!);
        }
    }

    private GuiAnimationSourceMode GetAnimationSourceMode()
        => animationSourceCombo.SelectedIndex switch
        {
            1 => GuiAnimationSourceMode.MotlistFiles,
            2 => GuiAnimationSourceMode.MotFiles,
            _ => GuiAnimationSourceMode.MotlistDirectory,
        };

    private void UpdateAnimationSourceUi()
    {
        var enabled = includeAnimationsCheck.Checked;
        var mode = GetAnimationSourceMode();
        motlistDirText.Enabled = enabled && mode == GuiAnimationSourceMode.MotlistDirectory;
        animationFileList.Enabled = enabled && mode != GuiAnimationSourceMode.MotlistDirectory;
        splitMotlistsCheck.Enabled = enabled && mode == GuiAnimationSourceMode.MotlistDirectory;
        splitAnimationsCheck.Enabled = enabled && mode != GuiAnimationSourceMode.MotlistDirectory;
        if (mode != GuiAnimationSourceMode.MotlistDirectory) splitMotlistsCheck.Checked = false;
        if (mode == GuiAnimationSourceMode.MotlistDirectory) splitAnimationsCheck.Checked = false;
        UpdateCommandPreview();
    }

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(outputPathText.Text)) return outputPathText.Text.Trim();
        var root = string.IsNullOrWhiteSpace(exportRootText.Text) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : exportRootText.Text.Trim();
        var ext = outputFormatCombo.SelectedItem?.ToString() ?? "fbx";
        var baseName = string.IsNullOrWhiteSpace(meshText.Text) ? "export" : Path.GetFileNameWithoutExtension(meshText.Text);
        return Path.Combine(root, $"{SanitizeFileName(baseName)}.{ext}");
    }

    private void UpdateCommandPreview()
    {
        try
        {
            var exe = Environment.ProcessPath ?? "REE-Content-Exporter.exe";
            commandPreviewText.Text = Quote(exe) + " " + string.Join(" ", BuildExportArgs().Select(Quote));
        }
        catch (Exception ex)
        {
            commandPreviewText.Text = ex.Message;
        }
    }

    private async Task RunExportAsync()
    {
        try
        {
            SavePathConfig();
            var args = BuildExportArgs();
            outputPathText.Text = ResolveOutputPath();
            UpdateRunningState(true);
            logText.Clear();
            AppendLog("Starting export");
            await RunExporterProcessAsync(args);
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 100;
            AppendLog("Export completed.");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateRunningState(false);
        }
    }

    private async Task RunExporterProcessAsync(IReadOnlyList<string> args)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Could not resolve exporter executable path.");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        runningProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
        runningProcess.OutputDataReceived += (_, e) => { if (e.Data != null) BeginInvoke(() => AppendLog(e.Data)); };
        runningProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) BeginInvoke(() => AppendLog(e.Data)); };
        if (!runningProcess.Start()) throw new InvalidOperationException("Failed to start exporter process.");
        runningProcess.BeginOutputReadLine();
        runningProcess.BeginErrorReadLine();
        await runningProcess.WaitForExitAsync();
        var exitCode = runningProcess.ExitCode;
        runningProcess.Dispose();
        runningProcess = null;
        if (exitCode != 0) throw new InvalidOperationException($"Exporter failed with exit code {exitCode}.");
    }

    private void CancelExport()
    {
        try
        {
            runningProcess?.Kill(entireProcessTree: true);
            AppendLog("Export cancelled.");
        }
        catch (Exception ex)
        {
            AppendLog("Cancel failed: " + ex.Message);
        }
    }

    private void UpdateRunningState(bool running)
    {
        runButton.Enabled = !running;
        cancelButton.Enabled = running;
        progressBar.Style = running ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        if (!running && progressBar.Value != 100) progressBar.Value = 0;
    }

    private void AppendLog(string line)
    {
        logText.AppendText(line + Environment.NewLine);
    }

    private static WizardConfig? LoadConfig(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<WizardConfig>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveConfigPath(string? overridePath)
        => string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REE-Content-Exporter", "config.json")
            : Path.GetFullPath(overridePath);

    private static string Quote(string value)
        => value.Contains(' ') || value.Contains('"') ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;

    private static string SanitizeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(name) ? "export" : name;
    }
}

internal enum AssetPickerKind
{
    Mesh,
    MotlistDirectory,
    MotlistFile,
    MotFile,
}

internal enum GuiAnimationSourceMode
{
    MotlistDirectory,
    MotlistFiles,
    MotFiles,
}

internal sealed class AssetPickerForm : Form
{
    private readonly IReadOnlyList<string> entries;
    private readonly string extractRoot;
    private readonly AssetPickerKind kind;
    private readonly TextBox searchText = new();
    private readonly ListBox resultList = new();

    public string SelectedPath { get; private set; } = "";

    public AssetPickerForm(IReadOnlyList<string> entries, string extractRoot, AssetPickerKind kind)
    {
        this.entries = entries;
        this.extractRoot = extractRoot;
        this.kind = kind;
        Text = kind switch
        {
            AssetPickerKind.Mesh => "Find mesh",
            AssetPickerKind.MotFile => "Find MOT file",
            AssetPickerKind.MotlistFile => "Find MOTLIST file",
            _ => "Find MOTLIST folder",
        };
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 600);
        BuildLayout();
        RefreshResults();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);

        searchText.Dock = DockStyle.Fill;
        searchText.PlaceholderText = "Type part of a filename or path";
        searchText.TextChanged += (_, _) => RefreshResults();
        resultList.Dock = DockStyle.Fill;
        resultList.DoubleClick += (_, _) => AcceptSelection();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var choose = new Button { Text = "Choose", Width = 100 };
        choose.Click += (_, _) => AcceptSelection();
        var cancel = new Button { Text = "Cancel", Width = 100 };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        actions.Controls.Add(choose);
        actions.Controls.Add(cancel);

        root.Controls.Add(searchText, 0, 0);
        root.Controls.Add(resultList, 0, 1);
        root.Controls.Add(actions, 0, 2);
    }

    private void RefreshResults()
    {
        var query = searchText.Text.Trim();
        var matches = entries
            .Where(MatchesKind)
            .Where(entry => query.Length == 0 || entry.Contains(query, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(entry).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(ToDisplayPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(250)
            .ToList();
        resultList.BeginUpdate();
        resultList.Items.Clear();
        foreach (var match in matches) resultList.Items.Add(match);
        resultList.EndUpdate();
    }

    private bool MatchesKind(string entry)
    {
        var normalized = entry.Replace('\\', '/');
        if (kind == AssetPickerKind.Mesh)
            return normalized.Contains(".mesh", StringComparison.OrdinalIgnoreCase) && !normalized.Split('/').Contains("streaming", StringComparer.OrdinalIgnoreCase);
        if (kind is AssetPickerKind.MotlistDirectory or AssetPickerKind.MotlistFile)
            return IsMotlistPath(normalized);
        return IsMotPath(normalized);
    }

    private string ToDisplayPath(string entry)
    {
        if (kind == AssetPickerKind.MotlistDirectory)
        {
            entry = Path.GetDirectoryName(entry.Replace('/', Path.DirectorySeparatorChar)) ?? entry;
        }
        if (string.IsNullOrWhiteSpace(extractRoot)) return entry;
        var rel = entry.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var directRel = StripNativesStmPrefix(rel);
        foreach (var candidate in new[]
        {
            Path.Combine(extractRoot, rel),
            Path.Combine(extractRoot, directRel),
            Path.Combine(extractRoot, "re_chunk_000", rel),
            Path.Combine(extractRoot, "re_chunk_000", directRel),
        })
        {
            if (kind == AssetPickerKind.MotlistDirectory)
            {
                if (Directory.Exists(candidate)) return candidate;
            }
            else if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(extractRoot, directRel);
    }

    private static string StripNativesStmPrefix(string rel)
    {
        var prefix = "natives" + Path.DirectorySeparatorChar + "stm" + Path.DirectorySeparatorChar;
        return rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? rel[prefix.Length..] : rel;
    }

    private static bool IsMotlistPath(string path)
        => path.Contains(".motlist.", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".motlist", StringComparison.OrdinalIgnoreCase);

    private static bool IsMotPath(string path)
        => (path.Contains(".mot.", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mot", StringComparison.OrdinalIgnoreCase))
            && !IsMotlistPath(path);

    private void AcceptSelection()
    {
        if (resultList.SelectedItem == null) return;
        SelectedPath = resultList.SelectedItem.ToString() ?? "";
        DialogResult = DialogResult.OK;
    }
}
