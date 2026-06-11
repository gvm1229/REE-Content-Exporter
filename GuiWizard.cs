using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly Color DarkBack = Color.FromArgb(25, 28, 34);
    private static readonly Color DarkPanel = Color.FromArgb(34, 38, 46);
    private static readonly Color DarkInput = Color.FromArgb(18, 21, 26);
    private static readonly Color DarkBorder = Color.FromArgb(63, 70, 84);
    private static readonly Color DarkText = Color.FromArgb(232, 236, 244);
    private static readonly Color MutedText = Color.FromArgb(170, 178, 192);
    private static readonly Color Accent = Color.FromArgb(73, 145, 255);

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
    private readonly ComboBox exportOptionsModeCombo = new();
    private readonly ComboBox languageCombo = new();
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
    private readonly Label progressPercentLabel = new() { Text = "0%", TextAlign = ContentAlignment.MiddleRight };
    private readonly ToolTip tooltips = new();
    private readonly Button runButton = new FooterActionButton() { Text = "Run Export" };
    private readonly Button cancelButton = new FooterActionButton() { Text = "Cancel", Enabled = false };
    private Button? savePathsButton;
    private Button? copyCommandButton;
    private Button? saveGameButton;
    private Button? changeGameButton;
    private FlowLayoutPanel? exportOptionChecksPanel;
    private Control? motlistDirRow;
    private Control? animationFileRow;
    private bool suppressExportOptionPersistence;
    private bool suppressLanguagePersistence;
    private bool initializing = true;

    public GuiWizardForm(string? configPathOverride)
    {
        configPath = ResolveConfigPath(configPathOverride);
        config = LoadConfig(configPath) ?? new WizardConfig();

        Text = "REE-Content-Exporter Wizard";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 820);
        Size = new Size(1280, 920);
        BackColor = DarkBack;
        ForeColor = DarkText;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();
        LoadConfigIntoControls();
        UpdateGameUi();
        ApplyLocalization();
        ApplyTooltips();
        UpdateCommandPreview();
        ApplyDarkTheme(this);
        initializing = false;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
            BackColor = DarkBack,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        Controls.Add(root);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            Padding = new Point(16, 6),
        };
        tabs.DrawItem += DrawDarkTab;

        tabs.TabPages.Add(BuildSetupTab());
        tabs.TabPages.Add(BuildExportTab());
        tabs.TabPages.Add(BuildProgressTab());

        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(BuildActionPanel(), 0, 1);
    }

    private TabPage BuildSetupTab()
    {
        var page = CreateTabPage("Setup");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10), BackColor = DarkBack };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(grid);

        grid.Controls.Add(BuildGamePanel(), 0, 0);
        grid.Controls.Add(BuildPathPanel(), 0, 1);
        var spacer = new Panel { Dock = DockStyle.Fill, BackColor = DarkBack };
        grid.Controls.Add(spacer, 0, 2);
        return page;
    }

    private TabPage BuildExportTab()
    {
        var page = CreateTabPage("Export");
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10), BackColor = DarkBack };
        var exportPanel = BuildExportPanel();
        exportPanel.Dock = DockStyle.Top;
        exportPanel.Height = 760;
        scroller.Controls.Add(exportPanel);
        page.Controls.Add(scroller);
        return page;
    }

    private TabPage BuildProgressTab()
    {
        var page = CreateTabPage("Progress");
        page.Padding = new Padding(10);
        page.Controls.Add(BuildLogPanel());
        return page;
    }

    private static TabPage CreateTabPage(string title)
        => new(title) { BackColor = DarkBack, ForeColor = DarkText, UseVisualStyleBackColor = false };

    private static void DrawDarkTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        var selected = e.Index == tabs.SelectedIndex;
        var bounds = e.Bounds;
        using var back = new SolidBrush(selected ? DarkPanel : DarkBack);
        using var text = new SolidBrush(selected ? DarkText : MutedText);
        e.Graphics.FillRectangle(back, bounds);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, bounds, selected ? DarkText : MutedText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private Control BuildGamePanel()
    {
        var panel = CreateGroup("Game configuration");
        var grid = CreateGrid(2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        panel.Controls.Add(grid);

        currentGameLabel.AutoSize = false;
        currentGameLabel.AutoEllipsis = true;
        currentGameLabel.Dock = DockStyle.Fill;
        currentGameLabel.TextAlign = ContentAlignment.MiddleLeft;
        gameCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        gameCombo.Dock = DockStyle.Fill;
        gameCombo.Margin = new Padding(0, 4, 0, 4);
        gameCombo.DisplayMember = nameof(WizardGameDefinition.DisplayName);
        gameCombo.ValueMember = nameof(WizardGameDefinition.Id);
        gameCombo.Items.AddRange(WizardGames.Definitions.Cast<object>().ToArray());
        gameCombo.SelectedIndexChanged += (_, _) => UpdateCommandPreview();

        saveGameButton = new Button { Text = "Set", Dock = DockStyle.Fill, Margin = new Padding(4, 3, 4, 3) };
        saveGameButton.Click += async (_, _) => await SaveSelectedGameAsync();
        changeGameButton = new Button { Text = "Edit", Dock = DockStyle.Fill, Margin = new Padding(4, 3, 4, 3) };
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
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        panel.Controls.Add(grid);

        AddPathRow(grid, 0, "Extract root", extractRootText, () => BrowseFolder(extractRootText));
        AddPathRow(grid, 1, "Export folder", exportRootText, () => BrowseFolder(exportRootText));
        AddPathRow(grid, 2, "Blender 4.5.9", blenderPathText, () => BrowseFile(blenderPathText, "blender.exe|blender.exe|Executable|*.exe|All files|*.*"));
        return panel;
    }

    private Control BuildExportPanel()
    {
        var panel = CreateGroup("Export setup");
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = DarkPanel,
            Padding = new Padding(2),
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.Controls.Add(grid);

        var findMeshButton = CreateCompactButton("Find", 58);
        findMeshButton.Click += (_, _) => PickAssetFromList(meshText, AssetPickerKind.Mesh);
        grid.Controls.Add(CreatePathRow("Primary mesh", meshText, () => BrowseFile(meshText, "RE Engine mesh|*.mesh*|All files|*.*"), findMeshButton), 0, 0);

        var addMeshButton = CreateCompactButton("+", 44);
        addMeshButton.Click += (_, _) => AddAdditionalMesh();
        var removeMeshButton = CreateCompactButton("-", 44);
        removeMeshButton.Click += (_, _) => RemoveSelectedAdditionalMesh();
        grid.Controls.Add(CreateListRow("Additional meshes", additionalMeshList, addMeshButton, removeMeshButton), 0, 1);

        includeAnimationsCheck.CheckedChanged += (_, _) => UpdateAnimationSourceUi();
        animationSourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        animationSourceCombo.Items.AddRange(["MOTLIST folder", "MOTLIST files", "MOT files"]);
        animationSourceCombo.SelectedIndex = 0;
        animationSourceCombo.SelectedIndexChanged += (_, _) => UpdateAnimationSourceUi();
        grid.Controls.Add(CreateAnimationSourceRow(), 0, 2);

        var findMotlistButton = CreateCompactButton("Find", 58);
        findMotlistButton.Click += (_, _) => PickAssetFromList(motlistDirText, AssetPickerKind.MotlistDirectory);
        motlistDirRow = CreatePathRow("MOTLIST folder", motlistDirText, () => BrowseFolder(motlistDirText), findMotlistButton);
        grid.Controls.Add(motlistDirRow, 0, 3);

        var addAnimationFileButton = CreateCompactButton("+", 44);
        addAnimationFileButton.Click += (_, _) => AddAnimationFileFromDisk();
        var findAnimationFileButton = CreateCompactButton("Find", 58);
        findAnimationFileButton.Click += (_, _) => PickAnimationFileFromList();
        var removeAnimationFileButton = CreateCompactButton("-", 44);
        removeAnimationFileButton.Click += (_, _) => RemoveSelectedAnimationFile();
        animationFileRow = CreateListRow("Animation files", animationFileList, addAnimationFileButton, findAnimationFileButton, removeAnimationFileButton);
        grid.Controls.Add(animationFileRow, 0, 4);

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
        grid.Controls.Add(CreateFormatRow(), 0, 5);

        grid.Controls.Add(CreateExportOptionsRow(), 0, 6);

        grid.Controls.Add(CreatePathRow("Output path", outputPathText, () => BrowseSaveOutput()), 0, 7);

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

        Button CreateCompactButton(string text, int width)
            => new() { Text = text, Width = width, Height = 34, Margin = new Padding(4, 3, 0, 3) };

        Label CreateRowLabel(string text)
            => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 0) };

        TableLayoutPanel CreatePathRow(string label, TextBox textBox, Action browse, params Button[] extraButtons)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 + extraButtons.Length, RowCount = 1, BackColor = DarkPanel };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            foreach (var _ in extraButtons) row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 5, 6, 5);
            var browseButton = CreateCompactButton("...", 46);
            browseButton.Dock = DockStyle.Fill;
            browseButton.Click += (_, _) => browse();

            row.Controls.Add(CreateRowLabel(label), 0, 0);
            row.Controls.Add(textBox, 1, 0);
            row.Controls.Add(browseButton, 2, 0);
            for (var i = 0; i < extraButtons.Length; i++)
            {
                extraButtons[i].Dock = DockStyle.Fill;
                row.Controls.Add(extraButtons[i], 3 + i, 0);
            }
            return row;
        }

        TableLayoutPanel CreateListRow(string label, ListBox listBox, params Button[] buttons)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = DarkPanel };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(150, buttons.Length * 80)));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            listBox.Dock = DockStyle.Fill;
            listBox.Margin = new Padding(0, 5, 6, 5);
            var buttonFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = DarkPanel };
            buttonFlow.Controls.AddRange(buttons);

            row.Controls.Add(CreateRowLabel(label), 0, 0);
            row.Controls.Add(listBox, 1, 0);
            row.Controls.Add(buttonFlow, 2, 0);
            return row;
        }

        TableLayoutPanel CreateAnimationSourceRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = DarkPanel };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            includeAnimationsCheck.AutoSize = true;
            includeAnimationsCheck.Margin = new Padding(0, 9, 0, 0);
            animationSourceCombo.Dock = DockStyle.Fill;
            animationSourceCombo.Margin = new Padding(0, 7, 0, 5);
            row.Controls.Add(CreateRowLabel("Animations"), 0, 0);
            row.Controls.Add(includeAnimationsCheck, 1, 0);
            row.Controls.Add(CreateRowLabel("Source"), 2, 0);
            row.Controls.Add(animationSourceCombo, 3, 0);
            return row;
        }

        TableLayoutPanel CreateFormatRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = DarkPanel };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            animationFilterText.Dock = DockStyle.Fill;
            animationFilterText.Margin = new Padding(0, 5, 0, 5);
            var animationNameFilterLabel = CreateRowLabel("Animation name filter");
            animationNameFilterLabel.AutoSize = false;
            animationNameFilterLabel.Dock = DockStyle.Fill;
            animationNameFilterLabel.TextAlign = ContentAlignment.MiddleLeft;
            row.Controls.Add(animationNameFilterLabel, 0, 0);
            row.Controls.Add(animationFilterText, 1, 0);

            var optionFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = false, WrapContents = false, BackColor = DarkPanel };
            outputFormatCombo.Width = 90;
            textureFormatCombo.Width = 90;
            fbxScaleInput.Width = 90;
            optionFlow.Controls.AddRange([
                new Label { Text = "Output", AutoSize = true, Padding = new Padding(0, 7, 0, 0) },
                outputFormatCombo,
                new Label { Text = "Textures", AutoSize = true, Padding = new Padding(20, 7, 0, 0) },
                textureFormatCombo,
                new Label { Text = "FBX scale", AutoSize = true, Padding = new Padding(20, 7, 0, 0) },
                fbxScaleInput,
            ]);
            row.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = DarkPanel }, 0, 1);
            row.Controls.Add(optionFlow, 1, 1);
            return row;
        }

        TableLayoutPanel CreateExportOptionsRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = DarkPanel };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            exportOptionsModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            exportOptionsModeCombo.Items.AddRange(["Default", "Custom"]);
            exportOptionsModeCombo.Dock = DockStyle.Fill;
            exportOptionsModeCombo.Margin = new Padding(0, 5, 0, 5);
            exportOptionsModeCombo.SelectedIndexChanged += (_, _) => OnExportOptionsModeChanged();

            exportOptionChecksPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = false, WrapContents = true, BackColor = DarkPanel };
            exportOptionChecksPanel.Controls.AddRange([splitMotlistsCheck, splitAnimationsCheck, noTexturesCheck, includeLodsCheck, includeOcclusionCheck, noPlaceholderBonesCheck, allowMissingStreamingCheck]);
            foreach (var checkBox in GetExportOptionCheckBoxes())
            {
                checkBox.CheckedChanged += (_, _) => OnExportOptionCheckChanged(checkBox);
            }

            row.Controls.Add(CreateRowLabel("Export options"), 0, 0);
            row.Controls.Add(exportOptionsModeCombo, 1, 0);
            row.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = DarkPanel }, 0, 1);
            row.Controls.Add(exportOptionChecksPanel, 1, 1);
            return row;
        }
    }

    private Control BuildLogPanel()
    {
        var panel = CreateGroup("Progress and command");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(grid);

        var progressRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = DarkPanel };
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        progressBar.Dock = DockStyle.Fill;
        progressBar.Style = ProgressBarStyle.Blocks;
        progressBar.Minimum = 0;
        progressBar.Maximum = 100;
        progressBar.Margin = new Padding(0, 7, 6, 7);
        progressPercentLabel.Dock = DockStyle.Fill;
        progressPercentLabel.AutoSize = false;
        progressPercentLabel.Margin = new Padding(0);
        progressRow.Controls.Add(progressBar, 0, 0);
        progressRow.Controls.Add(progressPercentLabel, 1, 0);
        commandPreviewText.Dock = DockStyle.Fill;
        commandPreviewText.Multiline = true;
        commandPreviewText.ReadOnly = true;
        commandPreviewText.ScrollBars = ScrollBars.Vertical;
        logText.Dock = DockStyle.Fill;
        logText.Multiline = true;
        logText.ReadOnly = true;
        logText.ScrollBars = ScrollBars.Both;
        logText.WordWrap = false;

        grid.Controls.Add(progressRow, 0, 0);
        grid.Controls.Add(commandPreviewText, 0, 1);
        grid.Controls.Add(logText, 0, 2);
        return panel;
    }

    private Control BuildActionPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = DarkBack };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 900));

        var languagePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = DarkBack };
        var languageLabel = new Label { Text = "Language", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 9, 6, 0) };
        languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        languageCombo.Width = 150;
        languageCombo.Height = 34;
        languageCombo.Items.AddRange(["English", "Korean"]);
        languageCombo.SelectedIndexChanged += (_, _) => OnLanguageChanged();
        languagePanel.Controls.Add(languageLabel);
        languagePanel.Controls.Add(languageCombo);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = DarkBack };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        runButton.Text = "Run Export";
        ConfigureActionButton(runButton, leftMargin: 6);
        ConfigureActionButton(cancelButton, leftMargin: 6);
        runButton.Click += async (_, _) => await RunExportAsync();
        cancelButton.Click += (_, _) => CancelExport();
        savePathsButton = new FooterActionButton { Text = "Save Paths" };
        ConfigureActionButton(savePathsButton, leftMargin: 0);
        savePathsButton.Click += (_, _) => SavePathConfig();
        copyCommandButton = new FooterActionButton { Text = "Copy Command" };
        ConfigureActionButton(copyCommandButton, leftMargin: 6);
        copyCommandButton.Click += (_, _) => Clipboard.SetText(commandPreviewText.Text);
        actions.Controls.Add(savePathsButton, 1, 0);
        actions.Controls.Add(copyCommandButton, 2, 0);
        actions.Controls.Add(cancelButton, 3, 0);
        actions.Controls.Add(runButton, 4, 0);

        panel.Controls.Add(languagePanel, 0, 0);
        panel.Controls.Add(actions, 1, 0);
        return panel;

        static void ConfigureActionButton(Button button, int leftMargin)
        {
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            button.Height = 36;
            button.Margin = new Padding(leftMargin, 8, 0, 0);
        }
    }

    private sealed class FooterActionButton : Button
    {
        public FooterActionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var back = Enabled ? BackColor : Color.FromArgb(42, 47, 57);
            using var fill = new SolidBrush(back);
            e.Graphics.FillRectangle(fill, ClientRectangle);
            using var border = new Pen(Accent);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            var textColor = Enabled ? ForeColor : MutedText;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private static GroupBox CreateGroup(string title)
        => new() { Text = title, Dock = DockStyle.Fill, Padding = new Padding(10) };

    private static TableLayoutPanel CreateGrid(int rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = rows, BackColor = DarkPanel };
        for (var i = 0; i < rows; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        return grid;
    }

    private static void ApplyDarkTheme(Control root)
    {
        root.BackColor = root is TextBoxBase or ListBox or ComboBox ? DarkInput : root is GroupBox ? DarkPanel : root.BackColor == SystemColors.Control ? DarkBack : root.BackColor;
        root.ForeColor = root.Enabled ? DarkText : MutedText;

        switch (root)
        {
            case GroupBox group:
                group.BackColor = DarkPanel;
                group.ForeColor = DarkText;
                break;
            case TextBox textBox:
                textBox.BackColor = DarkInput;
                textBox.ForeColor = DarkText;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListBox listBox:
                listBox.BackColor = DarkInput;
                listBox.ForeColor = DarkText;
                listBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = DarkInput;
                comboBox.ForeColor = DarkText;
                comboBox.FlatStyle = FlatStyle.Flat;
                break;
            case Button button:
                button.BackColor = DarkBorder;
                button.ForeColor = DarkText;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Accent;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(54, 65, 82);
                button.UseCompatibleTextRendering = false;
                button.TextAlign = ContentAlignment.MiddleCenter;
                button.MinimumSize = new Size(0, 32);
                break;
            case CheckBox checkBox:
                checkBox.AutoSize = true;
                checkBox.BackColor = root.Parent?.BackColor ?? DarkPanel;
                checkBox.ForeColor = DarkText;
                break;
            case Label label:
                label.BackColor = root.Parent?.BackColor ?? DarkPanel;
                label.ForeColor = DarkText;
                break;
            case FlowLayoutPanel flow:
                flow.BackColor = root.Parent?.BackColor ?? DarkPanel;
                break;
            case TableLayoutPanel table:
                table.BackColor = root.Parent?.BackColor ?? DarkPanel;
                break;
            case TabControl tabs:
                tabs.BackColor = DarkBack;
                tabs.ForeColor = DarkText;
                break;
            case TabPage page:
                page.BackColor = DarkBack;
                page.ForeColor = DarkText;
                break;
        }

        foreach (Control child in root.Controls) ApplyDarkTheme(child);
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
        textBox.Margin = new Padding(0, 4, 6, 4);
        var browseButton = new Button { Text = "...", Dock = DockStyle.Fill, Margin = new Padding(4, 3, 4, 3) };
        browseButton.Click += (_, _) => browse();
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, startColumn, row);
        grid.Controls.Add(textBox, startColumn + 1, row);
        grid.SetColumnSpan(textBox, startColumn == 0 ? 1 : 2);
        grid.Controls.Add(browseButton, startColumn == 0 ? 2 : 3, row);
    }

    private void LoadConfigIntoControls()
    {
        suppressLanguagePersistence = true;
        languageCombo.SelectedIndex = IsKorean() ? 1 : 0;
        suppressLanguagePersistence = false;

        extractRootText.Text = config.ExtractRoot;
        exportRootText.Text = config.DefaultExportRoot;
        blenderPathText.Text = config.BlenderPath;
        textureFormatCombo.SelectedItem = string.IsNullOrWhiteSpace(config.TextureFormat) ? "png" : config.TextureFormat;
        suppressExportOptionPersistence = true;
        exportOptionsModeCombo.SelectedIndex = IsCustomExportOptionsMode() ? 1 : 0;
        LoadCustomExportOptionsIntoChecks();
        suppressExportOptionPersistence = false;
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
            currentGameLabel.Text = IsKorean()
                ? $"{game.DisplayName} ({game.Id}). 변경하려면 config.json의 game 줄을 삭제하거나 편집을 누르세요."
                : $"{game.DisplayName} ({game.Id}). Delete the config.json game line or click Edit to change.";
            gameCombo.Enabled = false;
            if (saveGameButton != null) saveGameButton.Enabled = false;
            if (changeGameButton != null) changeGameButton.Enabled = true;
        }
        else if (!string.IsNullOrWhiteSpace(config.Game))
        {
            currentGameLabel.Text = IsKorean()
                ? $"지원하지 않는 저장 게임: {config.Game}. 편집을 누르거나 config.json의 game 줄을 삭제하세요."
                : $"Unsupported saved game: {config.Game}. Click Edit or delete the config.json game line.";
            gameCombo.Enabled = false;
            if (saveGameButton != null) saveGameButton.Enabled = false;
            if (changeGameButton != null) changeGameButton.Enabled = true;
        }
        else
        {
            currentGameLabel.Text = L("No game saved. Select a game, then click Set.");
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
            AppendLog(IsKorean() ? $"게임 구성을 저장했습니다: {game.DisplayName}" : $"Saved game configuration: {game.DisplayName}");
        }
        catch (Exception ex)
        {
            AppendLog(L("Game configuration failed: ") + ex.Message);
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
        AppendLog(L("Cleared game configuration. Select a game and click Set."));
    }

    private void SavePathConfig()
    {
        config.ExtractRoot = extractRootText.Text.Trim();
        config.DefaultExportRoot = exportRootText.Text.Trim();
        config.BlenderPath = blenderPathText.Text.Trim();
        config.TextureFormat = (textureFormatCombo.SelectedItem?.ToString() ?? "png").ToLowerInvariant();
        SaveConfig();
        AppendLog(L("Saved path and texture settings."));
    }

    private bool IsKorean()
        => string.Equals(config.Language, "ko", StringComparison.OrdinalIgnoreCase)
            || string.Equals(config.Language, "korean", StringComparison.OrdinalIgnoreCase);

    private bool IsCustomExportOptionsMode()
        => string.Equals(config.GuiExportOptionsMode, "custom", StringComparison.OrdinalIgnoreCase);

    private void LoadCustomExportOptionsIntoChecks()
    {
        suppressExportOptionPersistence = true;
        splitMotlistsCheck.Checked = config.GuiSplitMotlists;
        splitAnimationsCheck.Checked = config.GuiSplitAnimations;
        noTexturesCheck.Checked = config.GuiNoTextures;
        includeLodsCheck.Checked = config.GuiIncludeLods;
        includeOcclusionCheck.Checked = config.GuiIncludeOcclusion;
        noPlaceholderBonesCheck.Checked = config.GuiNoPlaceholderBones;
        allowMissingStreamingCheck.Checked = config.GuiAllowMissingStreaming;
        suppressExportOptionPersistence = false;
    }

    private void ApplyDefaultExportOptions()
    {
        var animations = includeAnimationsCheck.Checked;
        var mode = GetAnimationSourceMode();
        suppressExportOptionPersistence = true;
        splitMotlistsCheck.Checked = animations && mode is GuiAnimationSourceMode.MotlistDirectory or GuiAnimationSourceMode.MotlistFiles;
        splitAnimationsCheck.Checked = false;
        noTexturesCheck.Checked = false;
        includeLodsCheck.Checked = false;
        includeOcclusionCheck.Checked = false;
        noPlaceholderBonesCheck.Checked = animations;
        allowMissingStreamingCheck.Checked = false;
        suppressExportOptionPersistence = false;
    }

    private void SaveCustomExportOptions()
    {
        if (initializing) return;
        config.GuiExportOptionsMode = "custom";
        config.GuiSplitMotlists = splitMotlistsCheck.Checked;
        config.GuiSplitAnimations = splitAnimationsCheck.Checked;
        config.GuiNoTextures = noTexturesCheck.Checked;
        config.GuiIncludeLods = includeLodsCheck.Checked;
        config.GuiIncludeOcclusion = includeOcclusionCheck.Checked;
        config.GuiNoPlaceholderBones = noPlaceholderBonesCheck.Checked;
        config.GuiAllowMissingStreaming = allowMissingStreamingCheck.Checked;
        SaveConfig();
    }

    private void OnExportOptionsModeChanged()
    {
        if (suppressExportOptionPersistence) return;
        config.GuiExportOptionsMode = exportOptionsModeCombo.SelectedIndex == 1 ? "custom" : "";
        if (initializing)
        {
            UpdateAnimationSourceUi();
            UpdateCommandPreview();
            return;
        }
        if (IsCustomExportOptionsMode())
        {
            SaveCustomExportOptions();
        }
        else
        {
            SaveConfig();
        }
        UpdateAnimationSourceUi();
        UpdateCommandPreview();
    }

    private void OnExportOptionCheckChanged(CheckBox checkBox)
    {
        if (suppressExportOptionPersistence) return;
        if (checkBox == splitMotlistsCheck && splitMotlistsCheck.Checked && splitAnimationsCheck.Checked)
        {
            suppressExportOptionPersistence = true;
            splitAnimationsCheck.Checked = false;
            suppressExportOptionPersistence = false;
        }
        if (checkBox == splitAnimationsCheck && splitAnimationsCheck.Checked && splitMotlistsCheck.Checked)
        {
            suppressExportOptionPersistence = true;
            splitMotlistsCheck.Checked = false;
            suppressExportOptionPersistence = false;
        }
        if (IsCustomExportOptionsMode() && !initializing) SaveCustomExportOptions();
    }

    private void OnLanguageChanged()
    {
        if (suppressLanguagePersistence) return;
        config.Language = languageCombo.SelectedIndex == 1 ? "ko" : "en";
        if (!initializing) SaveConfig();
        ApplyLocalization();
        ApplyTooltips();
        UpdateGameUi();
        UpdateCommandPreview();
    }

    private void ApplyLocalization()
    {
        Text = L("REE-Content-Exporter Wizard");
        LocalizeControlTree(this);
        var previousExportSuppress = suppressExportOptionPersistence;
        suppressExportOptionPersistence = true;
        UpdateComboItems(exportOptionsModeCombo, ["Default", "Custom"], [L("Default"), L("Custom")]);
        var previousLanguageSuppress = suppressLanguagePersistence;
        suppressLanguagePersistence = true;
        UpdateComboItems(animationSourceCombo, ["MOTLIST folder", "MOTLIST files", "MOT files"], [L("MOTLIST folder"), L("MOTLIST files"), L("MOT files")]);
        suppressLanguagePersistence = previousLanguageSuppress;
        suppressExportOptionPersistence = previousExportSuppress;
    }

    private void LocalizeControlTree(Control root)
    {
        if (root is not ComboBox && !string.IsNullOrWhiteSpace(root.Text))
        {
            root.Text = L(root.Text);
        }
        foreach (Control child in root.Controls) LocalizeControlTree(child);
    }

    private static void UpdateComboItems(ComboBox comboBox, string[] englishItems, string[] localizedItems)
    {
        var selected = Math.Max(0, comboBox.SelectedIndex);
        comboBox.BeginUpdate();
        comboBox.Items.Clear();
        comboBox.Items.AddRange(localizedItems.Cast<object>().ToArray());
        comboBox.SelectedIndex = Math.Min(selected, comboBox.Items.Count - 1);
        comboBox.EndUpdate();
    }

    private void ApplyTooltips()
    {
        tooltips.SetToolTip(languageCombo, L("Choose the GUI language. The setting is saved immediately."));
        tooltips.SetToolTip(exportOptionsModeCombo, L("Default uses the legacy CLI wizard preferences. Custom enables and saves these checkboxes."));
        tooltips.SetToolTip(animationFilterText, L("Optional. Maps to --animation-name <contains> and filters exported animation names after sources are selected."));
        if (savePathsButton != null) tooltips.SetToolTip(savePathsButton, L("Save extract, export, Blender, texture, language, and GUI option settings."));
        if (copyCommandButton != null) tooltips.SetToolTip(copyCommandButton, L("Copy the generated CLI command preview to the clipboard."));
        tooltips.SetToolTip(cancelButton, L("Cancel the running export process."));
        tooltips.SetToolTip(runButton, L("Run the export with the current GUI settings."));
    }

    private string L(string text)
    {
        if (!IsKorean())
        {
            return KoToEn.TryGetValue(text, out var english) ? english : text;
        }
        if (KoToEn.ContainsKey(text)) return text;
        return EnToKo.TryGetValue(text, out var korean) ? korean : text;
    }

    private static readonly Dictionary<string, string> EnToKo = new(StringComparer.Ordinal)
    {
        ["REE-Content-Exporter Wizard"] = "REE-Content-Exporter 마법사",
        ["Setup"] = "설정",
        ["Export"] = "내보내기",
        ["Progress"] = "진행",
        ["Game configuration"] = "게임 구성",
        ["Current"] = "현재",
        ["Select game"] = "게임 선택",
        ["Set"] = "설정",
        ["Edit"] = "편집",
        ["Paths"] = "경로",
        ["Extract root"] = "추출 루트",
        ["Export folder"] = "내보내기 폴더",
        ["Blender 4.5.9"] = "Blender 4.5.9",
        ["Export setup"] = "내보내기 설정",
        ["Primary mesh"] = "기본 메시",
        ["Additional meshes"] = "추가 메시",
        ["Animations"] = "애니메이션",
        ["Include animations"] = "애니메이션 포함",
        ["Source"] = "소스",
        ["MOTLIST folder"] = "MOTLIST 폴더",
        ["MOTLIST files"] = "MOTLIST 파일",
        ["MOT files"] = "MOT 파일",
        ["Animation files"] = "애니메이션 파일",
        ["Animation name filter"] = "애니메이션 이름 필터",
        ["Output"] = "출력",
        ["Textures"] = "텍스처",
        ["FBX scale"] = "FBX 스케일",
        ["Export options"] = "내보내기 옵션",
        ["Default"] = "기본값",
        ["Custom"] = "사용자 지정",
        ["Split by MOTLIST"] = "MOTLIST별 분할",
        ["Split animations"] = "애니메이션 분할",
        ["No textures"] = "텍스처 없음",
        ["Include LODs"] = "LOD 포함",
        ["Include occlusion"] = "Occlusion 포함",
        ["Skip missing bone channels"] = "없는 본 채널 건너뛰기",
        ["Allow missing streaming buffers"] = "누락된 스트리밍 버퍼 허용",
        ["Output path"] = "출력 경로",
        ["Progress and command"] = "진행 및 명령",
        ["Language"] = "언어",
        ["Save Paths"] = "경로 저장",
        ["Copy Command"] = "명령 복사",
        ["Cancel"] = "취소",
        ["Run Export"] = "내보내기 실행",
        ["Find"] = "찾기",
        ["Choose"] = "선택",
        ["Type part of a filename or path"] = "파일 이름 또는 경로 일부 입력",
        ["No game saved. Select a game, then click Set."] = "저장된 게임이 없습니다. 게임을 선택한 뒤 설정을 누르세요.",
        ["Cleared game configuration. Select a game and click Set."] = "게임 구성을 지웠습니다. 게임을 선택한 뒤 설정을 누르세요.",
        ["Saved path and texture settings."] = "경로 및 텍스처 설정을 저장했습니다.",
        ["Game configuration failed: "] = "게임 구성 실패: ",
        ["Choose the GUI language. The setting is saved immediately."] = "GUI 언어를 선택합니다. 설정은 즉시 저장됩니다.",
        ["Default uses the legacy CLI wizard preferences. Custom enables and saves these checkboxes."] = "기본값은 기존 CLI 마법사 선호 설정을 사용합니다. 사용자 지정은 체크박스를 활성화하고 저장합니다.",
        ["Optional. Maps to --animation-name <contains> and filters exported animation names after sources are selected."] = "선택 사항입니다. --animation-name <contains>에 대응하며 소스 선택 후 내보낼 애니메이션 이름을 필터링합니다.",
        ["Save extract, export, Blender, texture, language, and GUI option settings."] = "추출, 내보내기, Blender, 텍스처, 언어, GUI 옵션 설정을 저장합니다.",
        ["Copy the generated CLI command preview to the clipboard."] = "생성된 CLI 명령 미리보기를 클립보드에 복사합니다.",
        ["Cancel the running export process."] = "실행 중인 내보내기 프로세스를 취소합니다.",
        ["Run the export with the current GUI settings."] = "현재 GUI 설정으로 내보내기를 실행합니다.",
        ["Save a game first so its REE.PAK.Tool list can be downloaded."] = "REE.PAK.Tool 목록을 다운로드할 수 있도록 먼저 게임을 저장하세요.",
        ["Starting export"] = "내보내기를 시작합니다",
        ["Export completed."] = "내보내기가 완료되었습니다.",
        ["ERROR: "] = "오류: ",
        ["Export cancelled."] = "내보내기를 취소했습니다.",
        ["Cancel failed: "] = "취소 실패: ",
    };

    private static readonly Dictionary<string, string> KoToEn = EnToKo
        .GroupBy(pair => pair.Value, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    private void SaveConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? ".");
        if (string.IsNullOrWhiteSpace(config.Language)) config.Language = "en";
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
            MessageBox.Show(L("Save a game first so its REE.PAK.Tool list can be downloaded."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var picker = new AssetPickerForm(entries, extractRootText.Text.Trim(), kind, IsKorean());
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
            MessageBox.Show(L("Save a game first so its REE.PAK.Tool list can be downloaded."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var pickerKind = mode == GuiAnimationSourceMode.MotFiles ? AssetPickerKind.MotFile : AssetPickerKind.MotlistFile;
        using var picker = new AssetPickerForm(entries, extractRootText.Text.Trim(), pickerKind, IsKorean());
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
                    if (splitMotlistsCheck.Checked) args.Add("--split-motlists");
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
        if (includeAnimationsCheck.Checked && noPlaceholderBonesCheck.Checked) args.Add("--no-placeholder-animation-bones");
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
        var customOptions = IsCustomExportOptionsMode();
        var motlistDirEnabled = enabled && mode == GuiAnimationSourceMode.MotlistDirectory;
        var animationFilesEnabled = enabled && mode != GuiAnimationSourceMode.MotlistDirectory;
        animationSourceCombo.Enabled = enabled;
        animationFilterText.Enabled = enabled;
        SetControlTreeEnabled(motlistDirRow, motlistDirEnabled);
        SetControlTreeEnabled(animationFileRow, animationFilesEnabled);
        if (!customOptions)
        {
            ApplyDefaultExportOptions();
        }
        exportOptionsModeCombo.Enabled = true;
        SetCheckEnabled(splitMotlistsCheck, customOptions && enabled && mode != GuiAnimationSourceMode.MotFiles);
        SetCheckEnabled(splitAnimationsCheck, customOptions && enabled && mode != GuiAnimationSourceMode.MotlistDirectory && !splitMotlistsCheck.Checked);
        SetCheckEnabled(noTexturesCheck, customOptions);
        SetCheckEnabled(includeLodsCheck, customOptions);
        SetCheckEnabled(includeOcclusionCheck, customOptions);
        SetCheckEnabled(noPlaceholderBonesCheck, customOptions && enabled);
        SetCheckEnabled(allowMissingStreamingCheck, customOptions);
        if (mode == GuiAnimationSourceMode.MotFiles && splitMotlistsCheck.Checked)
        {
            suppressExportOptionPersistence = true;
            splitMotlistsCheck.Checked = false;
            suppressExportOptionPersistence = false;
        }
        if ((mode == GuiAnimationSourceMode.MotlistDirectory || splitMotlistsCheck.Checked) && splitAnimationsCheck.Checked)
        {
            suppressExportOptionPersistence = true;
            splitAnimationsCheck.Checked = false;
            suppressExportOptionPersistence = false;
        }
        if (customOptions && !suppressExportOptionPersistence && !initializing) SaveCustomExportOptions();
        UpdateCommandPreview();
    }

    private IEnumerable<CheckBox> GetExportOptionCheckBoxes()
    {
        yield return splitMotlistsCheck;
        yield return splitAnimationsCheck;
        yield return noTexturesCheck;
        yield return includeLodsCheck;
        yield return includeOcclusionCheck;
        yield return noPlaceholderBonesCheck;
        yield return allowMissingStreamingCheck;
    }

    private static void SetCheckEnabled(CheckBox checkBox, bool enabled)
    {
        checkBox.Enabled = true;
        checkBox.AutoCheck = enabled;
        checkBox.TabStop = enabled;
        checkBox.ForeColor = enabled ? DarkText : MutedText;
    }

    private static void SetControlTreeEnabled(Control? root, bool enabled)
    {
        if (root == null) return;
        root.Enabled = enabled;
        foreach (Control child in root.Controls) SetControlTreeEnabled(child, enabled);
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
            var exe = ResolveCliExecutablePath();
            commandPreviewText.Text = Quote(exe) + " " + string.Join(" ", BuildExportArgs().Select(Quote));
        }
        catch (Exception ex)
        {
            commandPreviewText.Text = ex.Message;
        }
    }

    private static string ResolveCliExecutablePath()
    {
        var cliPath = Path.Combine(AppContext.BaseDirectory, "REE-Content-Exporter-CLI.exe");
        if (File.Exists(cliPath)) return cliPath;
        return Environment.ProcessPath ?? cliPath;
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
            SetProgress(0);
            AppendLog(L("Starting export"));
            await RunExporterProcessAsync(args);
            SetProgress(100);
            AppendLog(L("Export completed."));
        }
        catch (Exception ex)
        {
            AppendLog(L("ERROR: ") + ex.Message);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateRunningState(false);
        }
    }

    private async Task RunExporterProcessAsync(IReadOnlyList<string> args)
    {
        var exe = ResolveCliExecutablePath();
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
        runningProcess.OutputDataReceived += (_, e) => { if (e.Data != null) BeginInvoke(() => AppendProcessLine(e.Data)); };
        runningProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) BeginInvoke(() => AppendProcessLine(e.Data)); };
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
            AppendLog(L("Export cancelled."));
        }
        catch (Exception ex)
        {
            AppendLog(L("Cancel failed: ") + ex.Message);
        }
    }

    private void UpdateRunningState(bool running)
    {
        runButton.Enabled = !running;
        cancelButton.Enabled = running;
        progressBar.Style = ProgressBarStyle.Blocks;
        if (!running && progressBar.Value != 100) SetProgress(0);
    }

    private void AppendProcessLine(string line)
    {
        AppendLog(line);
        UpdateProgressFromLine(line);
    }

    private void AppendLog(string line)
    {
        logText.AppendText(line + Environment.NewLine);
    }

    private void UpdateProgressFromLine(string line)
    {
        var percentMatch = Regex.Match(line, @"(?<!\d)(\d{1,3})\s*%");
        if (percentMatch.Success && int.TryParse(percentMatch.Groups[1].Value, out var percent))
        {
            SetProgress(percent);
            return;
        }

        var fractionMatches = Regex.Matches(line, @"(?<!\d)(\d{1,6})\s*/\s*(\d{1,6})(?!\d)");
        foreach (Match match in fractionMatches)
        {
            if (!int.TryParse(match.Groups[1].Value, out var current)) continue;
            if (!int.TryParse(match.Groups[2].Value, out var total) || total <= 0 || current < 0 || current > total) continue;
            SetProgress((int)Math.Round(current * 100.0 / total));
            return;
        }
    }

    private void SetProgress(int percent)
    {
        percent = Math.Clamp(percent, progressBar.Minimum, progressBar.Maximum);
        progressBar.Value = percent;
        progressPercentLabel.Text = percent.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
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
    private readonly bool korean;
    private readonly TextBox searchText = new();
    private readonly ListBox resultList = new();

    public string SelectedPath { get; private set; } = "";

    public AssetPickerForm(IReadOnlyList<string> entries, string extractRoot, AssetPickerKind kind, bool korean)
    {
        this.entries = entries;
        this.extractRoot = extractRoot;
        this.kind = kind;
        this.korean = korean;
        Text = kind switch
        {
            AssetPickerKind.Mesh => L("Find mesh"),
            AssetPickerKind.MotFile => L("Find MOT file"),
            AssetPickerKind.MotlistFile => L("Find MOTLIST file"),
            _ => L("Find MOTLIST folder"),
        };
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(960, 640);
        Size = new Size(1180, 760);
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
        searchText.PlaceholderText = L("Type part of a filename or path");
        searchText.TextChanged += (_, _) => RefreshResults();
        resultList.Dock = DockStyle.Fill;
        resultList.HorizontalScrollbar = true;
        resultList.IntegralHeight = false;
        resultList.DoubleClick += (_, _) => AcceptSelection();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var choose = new Button { Text = L("Choose"), Width = 100 };
        choose.Click += (_, _) => AcceptSelection();
        var cancel = new Button { Text = L("Cancel"), Width = 100 };
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
        resultList.HorizontalExtent = matches.Count == 0
            ? 0
            : matches.Max(match => TextRenderer.MeasureText(match, resultList.Font).Width) + 32;
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

    private string L(string text)
        => !korean ? text : text switch
        {
            "Find mesh" => "메시 찾기",
            "Find MOT file" => "MOT 파일 찾기",
            "Find MOTLIST file" => "MOTLIST 파일 찾기",
            "Find MOTLIST folder" => "MOTLIST 폴더 찾기",
            "Type part of a filename or path" => "파일 이름 또는 경로 일부 입력",
            "Choose" => "선택",
            "Cancel" => "취소",
            _ => text,
        };
}
