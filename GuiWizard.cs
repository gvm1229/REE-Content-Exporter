using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private static readonly Color DarkBack = Color.FromArgb(11, 13, 17);
    private static readonly Color DarkPanel = Color.FromArgb(24, 28, 35);
    private static readonly Color DarkPanelAlt = Color.FromArgb(31, 36, 45);
    private static readonly Color DarkInput = Color.FromArgb(14, 17, 22);
    private static readonly Color DarkBorder = Color.FromArgb(62, 70, 84);
    private static readonly Color DarkText = Color.FromArgb(244, 247, 251);
    private static readonly Color MutedText = Color.FromArgb(158, 166, 181);
    private static readonly Color Accent = Color.FromArgb(118, 185, 255);
    private static readonly Color AccentHover = Color.FromArgb(164, 210, 255);
    private static readonly Color Success = Color.FromArgb(111, 220, 165);
    private static readonly Color Warning = Color.FromArgb(255, 204, 102);
    private static readonly Color Danger = Color.FromArgb(255, 128, 128);
    private static readonly Color ButtonBase = Color.FromArgb(36, 43, 54);
    private static readonly Color ButtonHover = Color.FromArgb(49, 61, 77);
    private static readonly Color ButtonPressed = Color.FromArgb(57, 95, 133);
    private static readonly List<GCHandle> PinnedFontHandles = new();
    private static readonly Color DisabledBack = Color.FromArgb(34, 38, 45);
    private static readonly PrivateFontCollection AppFonts = LoadAppFonts();

    private readonly string configPath;
    private WizardConfig config;
    private Process? runningProcess;
    private string? currentGuiLogPath;

    private readonly Label currentGameLabel = new();
    private readonly Label savedGameValueLabel = new();
    private readonly ThemedComboBox gameCombo = new();
    private readonly TextBox extractRootText = new();
    private readonly TextBox exportRootText = new();
    private readonly TextBox blenderPathText = new();
    private readonly TextBox meshText = new();
    private readonly ListBox additionalMeshList = new();
    private readonly CheckBox includeAnimationsCheck = new ThemedCheckBox() { Text = "Include animations" };
    private readonly ThemedComboBox animationSourceCombo = new();
    private readonly TextBox motlistDirText = new();
    private readonly ListBox animationFileList = new();
    private readonly TextBox animationFilterText = new();
    private readonly TextBox sceneActorText = new();
    private readonly CheckBox allowMixedSceneAnimationsCheck = new ThemedCheckBox() { Text = "Allow mixed scene actors" };
    private readonly ThemedComboBox outputFormatCombo = new();
    private readonly ThemedComboBox textureFormatCombo = new();
    private readonly ThemedNumericUpDown fbxScaleInput = new();
    private readonly CheckBox boneSpacingRepairCheck = new ThemedCheckBox() { Text = "Bone spacing repair" };
    private readonly TextBox boneSpacingReferenceFbxText = new();
    private readonly TextBox boneSpacingReferenceActionText = new();
    private readonly TextBox boneSpacingAllowTranslationText = new();
    private readonly ThemedComboBox exportOptionsModeCombo = new();
    private readonly ThemedComboBox languageCombo = new();
    private readonly CheckBox splitMotlistsCheck = new ThemedCheckBox() { Text = "Split by MOTLIST" };
    private readonly CheckBox splitAnimationsCheck = new ThemedCheckBox() { Text = "Split animations" };
    private readonly CheckBox noTexturesCheck = new ThemedCheckBox() { Text = "No textures" };
    private readonly CheckBox includeLodsCheck = new ThemedCheckBox() { Text = "Include LODs" };
    private readonly CheckBox includeOcclusionCheck = new ThemedCheckBox() { Text = "Include occlusion" };
    private readonly CheckBox noPlaceholderBonesCheck = new ThemedCheckBox() { Text = "Skip missing bone channels" };
    private readonly CheckBox allowMissingStreamingCheck = new ThemedCheckBox() { Text = "Allow missing streaming buffers" };
    private readonly TextBox outputPathText = new();
    private readonly TextBox commandPreviewText = new();
    private readonly TextBox logText = new();
    private readonly ThemedProgressBar progressBar = new();
    private readonly Label progressPercentLabel = new() { Text = "0%", TextAlign = ContentAlignment.MiddleRight };
    private readonly Label readinessLabel = new();
    private readonly Label readinessDetailLabel = new();
    private readonly ToolTip tooltips = new();
    private readonly Button runButton = new ThemedButton() { Text = "Run Export", AccentButton = true };
    private readonly Button cancelButton = new ThemedButton() { Text = "Cancel", Enabled = false };
    private Button? savePathsButton;
    private Button? copyCommandButton;
    private Button? saveGameButton;
    private Button? changeGameButton;
    private FlowLayoutPanel? exportOptionChecksPanel;
    private Control? motlistDirRow;
    private Control? animationFileRow;
    private Control? sceneActorRow;
    private Control? boneSpacingReferenceRow;
    private Control? boneSpacingOptionsRow;
    private bool suppressExportOptionPersistence;
    private bool suppressLanguagePersistence;
    private bool initializing = true;

    private const int ControlHeight = 44;
    private const int FieldRowHeight = 64;
    private const int LabelColumnWidth = 220;
    private const int UtilityButtonWidth = 76;
    private const int RowVerticalMargin = 10;
    private const int RowHorizontalGap = 8;
    private int GroupOverhead => Math.Max(58, Font.Height + 40);
    private int GroupPanelHeight(int rows) => rows * FieldRowHeight + GroupOverhead;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public GuiWizardForm(string? configPathOverride)
    {
        configPath = ResolveConfigPath(configPathOverride);
        config = LoadConfig(configPath) ?? new WizardConfig();

        Text = "REE-Content-Exporter Wizard";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(1360, 900);
        Size = new Size(1760, 1400);
        BackColor = DarkBack;
        ForeColor = DarkText;
        AutoScaleMode = AutoScaleMode.Font;
        Font = UiFont(9F);

        BuildLayout();
        LoadConfigIntoControls();
        UpdateGameUi();
        ApplyLocalization();
        ApplyTooltips();
        UpdateCommandPreview();
        ApplyDarkTheme(this);
        initializing = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyNativeWindowStyling();
    }

    private void ApplyNativeWindowStyling()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        TrySetDwmAttribute(DwmwaUseImmersiveDarkMode, 1);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TrySetDwmAttribute(DwmwaWindowCornerPreference, 2);
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            TrySetDwmAttribute(DwmwaSystemBackdropType, 2);
        }
    }

    private void TrySetDwmAttribute(int attribute, int value)
    {
        try
        {
            _ = DwmSetWindowAttribute(Handle, attribute, ref value, Marshal.SizeOf<int>());
        }
        catch
        {
            // Older Windows builds simply ignore the visual polish path.
        }
    }

    private static PrivateFontCollection LoadAppFonts()
    {
        var fonts = new PrivateFontCollection();
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;
            var buffer = new byte[stream.Length];
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0) break;
                offset += read;
            }
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            PinnedFontHandles.Add(handle);
            fonts.AddMemoryFont(handle.AddrOfPinnedObject(), buffer.Length);
        }
        return fonts;
    }

    private static Font UiFont(float size, FontStyle style = FontStyle.Regular)
    {
        var familyName = AppFonts.Families.FirstOrDefault()?.Name ?? "Segoe UI";
        try
        {
            return new Font(familyName, size, style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }
    }

    private static Font IconFont(float size)
        => new("Segoe MDL2 Assets", size, FontStyle.Regular, GraphicsUnit.Point);

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            BackColor = DarkBack,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        Controls.Add(root);

        root.Controls.Add(BuildWorkspace(), 0, 0);
        root.Controls.Add(BuildActionPanel(), 0, 1);
    }

    private Control BuildWorkspace()
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(24, 20, 24, 12),
            BackColor = DarkBack,
        };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = DarkBack,
            Padding = new Padding(0),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        body.Controls.Add(BuildWorkflowColumn(), 0, 0);
        body.Controls.Add(BuildRunColumn(), 1, 0);

        workspace.Controls.Add(BuildAppHeader(), 0, 0);
        workspace.Controls.Add(body, 0, 1);
        AttachPreviewEvents(workspace);
        return workspace;
    }

    private Control BuildAppHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = DarkBack,
            Padding = new Padding(2, 0, 0, 8),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        var title = new Label
        {
            Text = "REE-Content-Exporter",
            Dock = DockStyle.Fill,
            Font = UiFont(17F, FontStyle.Bold),
            ForeColor = DarkText,
            BackColor = DarkBack,
            TextAlign = ContentAlignment.BottomLeft,
        };
        var subtitle = new Label
        {
            Text = L("Build Unreal-ready assets with guided export paths, validation, and persistent logs."),
            Dock = DockStyle.Fill,
            Font = UiFont(9.5F),
            ForeColor = MutedText,
            BackColor = DarkBack,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.TopLeft,
        };
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        return header;
    }

    private Control BuildWorkflowColumn()
    {
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = DarkBack, Padding = new Padding(0, 0, 16, 18) };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = DarkBack,
            Margin = new Padding(0),
        };
        scroller.Controls.Add(flow);
        scroller.Resize += (_, _) => ResizeWorkflowSections(scroller, flow);

        AddWorkflowSection(flow, BuildGamePanel(), GroupPanelHeight(2) + 28);
        AddWorkflowSection(flow, BuildPathPanel(), GroupPanelHeight(3) + 34);
        AddWorkflowSection(flow, BuildCoreAssetPanel(), 282);
        AddWorkflowSection(flow, BuildAnimationPanel(), 440);
        AddWorkflowSection(flow, BuildSceneDiagnosticsPanel(), 132);
        AddWorkflowSection(flow, BuildOutputPanel(), GroupPanelHeight(4) + 40);
        AddWorkflowSection(flow, BuildOptionsPanel(), 156);
        AddWorkflowSection(flow, BuildBoneSpacingPanel(), 292);
        flow.Padding = new Padding(0, 0, 0, 96);
        UpdateBoneSpacingUi();
        ResizeWorkflowSections(scroller, flow);
        return scroller;
    }

    private Control BuildRunColumn()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = DarkBack, Padding = new Padding(16, 0, 0, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(BuildReadinessPanel(), 0, 0);
        panel.Controls.Add(BuildCommandPanel(), 0, 1);
        panel.Controls.Add(BuildLogPanel(), 0, 2);
        return panel;
    }

    private static void AddWorkflowSection(FlowLayoutPanel flow, Control section, int height)
    {
        section.Dock = DockStyle.None;
        section.Margin = new Padding(0, 0, 0, 12);
        if (section is DisclosurePanel disclosure)
        {
            disclosure.CollapsedHeight = 54;
        }
        else
        {
            section.Height = height;
        }
        flow.Controls.Add(section);
    }

    private static void ResizeWorkflowSections(Panel scroller, FlowLayoutPanel flow)
    {
        var width = Math.Max(360, scroller.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
        flow.Width = width;
        foreach (Control child in flow.Controls)
        {
            child.Width = width;
        }
    }

    private Control BuildReadinessPanel()
    {
        var panel = CreateGroup("Export readiness");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = DarkPanel, Padding = new Padding(8, 8, 8, 6) };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(grid);

        readinessLabel.Dock = DockStyle.Fill;
        readinessLabel.Font = UiFont(10.5F, FontStyle.Bold);
        readinessLabel.ForeColor = Warning;
        readinessLabel.BackColor = DarkPanel;
        readinessLabel.TextAlign = ContentAlignment.MiddleLeft;
        readinessDetailLabel.Dock = DockStyle.Fill;
        readinessDetailLabel.Font = UiFont(8.75F);
        readinessDetailLabel.ForeColor = MutedText;
        readinessDetailLabel.BackColor = DarkPanel;
        readinessDetailLabel.AutoEllipsis = true;
        readinessDetailLabel.TextAlign = ContentAlignment.TopLeft;
        grid.Controls.Add(readinessLabel, 0, 0);
        grid.Controls.Add(readinessDetailLabel, 0, 1);
        return panel;
    }

    private Control BuildCommandPanel()
    {
        var panel = CreateGroup("Command preview");
        commandPreviewText.Dock = DockStyle.Fill;
        commandPreviewText.Multiline = true;
        commandPreviewText.ReadOnly = true;
        commandPreviewText.ScrollBars = ScrollBars.None;
        commandPreviewText.WordWrap = true;
        commandPreviewText.Font = new Font(FontFamily.GenericMonospace, 9F);
        commandPreviewText.Margin = new Padding(8, 8, 8, 8);
        panel.Controls.Add(commandPreviewText);
        return panel;
    }

    private Control BuildCoreAssetPanel()
    {
        var panel = CreateGroup("Assets");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = DarkPanel, Padding = new Padding(4, 8, 4, 4) };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(grid);

        var findMeshButton = CreateCompactButton("Find", 78);
        tooltips.SetToolTip(findMeshButton, L("Search the saved game list for a primary mesh path."));
        findMeshButton.Click += (_, _) => PickAssetFromList(meshText, AssetPickerKind.Mesh);
        grid.Controls.Add(CreateWidePathRow("Primary mesh", meshText, () => BrowseFile(meshText, "RE Engine mesh|*.mesh*|All files|*.*"), findMeshButton), 0, 0);

        var addMeshButton = CreateCompactButton("+", 48);
        tooltips.SetToolTip(addMeshButton, L("Add an additional mesh from disk."));
        addMeshButton.Click += (_, _) => AddAdditionalMesh();
        var removeMeshButton = CreateCompactButton("-", 48);
        tooltips.SetToolTip(removeMeshButton, L("Remove the selected additional mesh."));
        removeMeshButton.Click += (_, _) => RemoveSelectedAdditionalMesh();
        grid.Controls.Add(CreateWideListRow("Additional meshes", additionalMeshList, addMeshButton, removeMeshButton), 0, 1);
        return panel;
    }

    private Control BuildAnimationPanel()
    {
        var panel = CreateGroup("Animation");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = DarkPanel, Padding = new Padding(4, 8, 4, 4) };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        panel.Controls.Add(grid);

        includeAnimationsCheck.CheckedChanged += (_, _) => UpdateAnimationSourceUi();
        animationSourceCombo.Items.AddRange(["MOTLIST folder", "MOTLIST files", "MOT files"]);
        animationSourceCombo.SelectedIndex = 0;
        animationSourceCombo.SelectedIndexChanged += (_, _) => UpdateAnimationSourceUi();
        grid.Controls.Add(CreateAnimationHeaderRow(), 0, 0);

        var findMotlistButton = CreateCompactButton("Find", 78);
        tooltips.SetToolTip(findMotlistButton, L("Search the saved game list for a MOTLIST folder."));
        findMotlistButton.Click += (_, _) => PickAssetFromList(motlistDirText, AssetPickerKind.MotlistDirectory);
        motlistDirRow = CreateWidePathRow("MOTLIST folder", motlistDirText, () => BrowseFolder(motlistDirText), findMotlistButton);
        grid.Controls.Add(motlistDirRow, 0, 1);

        var addAnimationFileButton = CreateCompactButton("+", 48);
        tooltips.SetToolTip(addAnimationFileButton, L("Add a MOTLIST or MOT file from disk."));
        addAnimationFileButton.Click += (_, _) => AddAnimationFileFromDisk();
        var findAnimationFileButton = CreateCompactButton("Find", 78);
        tooltips.SetToolTip(findAnimationFileButton, L("Search the saved game list for animation files."));
        findAnimationFileButton.Click += (_, _) => PickAnimationFileFromList();
        var removeAnimationFileButton = CreateCompactButton("-", 48);
        tooltips.SetToolTip(removeAnimationFileButton, L("Remove the selected animation file."));
        removeAnimationFileButton.Click += (_, _) => RemoveSelectedAnimationFile();
        animationFileRow = CreateWideListRow("Animation files", animationFileList, addAnimationFileButton, findAnimationFileButton, removeAnimationFileButton);
        grid.Controls.Add(animationFileRow, 0, 2);

        grid.Controls.Add(CreateWidePathRow("Name filter", animationFilterText, () => { }), 0, 3);
        var hint = new Label { Text = L("Optional. Maps to --animation-name <contains> and filters exported animation names after sources are selected."), Dock = DockStyle.Fill, ForeColor = MutedText, BackColor = DarkPanel, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        grid.Controls.Add(hint, 0, 4);
        return panel;
    }

    private TableLayoutPanel CreateSceneActorRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = DarkPanel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sceneActorInput = CreateInputFrame(sceneActorText, 14);
        allowMixedSceneAnimationsCheck.AutoSize = true;
        allowMixedSceneAnimationsCheck.Margin = new Padding(0, RowVerticalMargin, 0, 0);

        row.Controls.Add(CreateFieldLabel("Scene actor"), 0, 0);
        row.Controls.Add(sceneActorInput, 1, 0);
        row.Controls.Add(allowMixedSceneAnimationsCheck, 2, 0);
        return row;
    }

    private Control BuildSceneDiagnosticsPanel()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 1, AutoSize = true, BackColor = DarkPanel, Padding = new Padding(4, 8, 4, 4) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        sceneActorRow = CreateSceneActorRow();
        body.Controls.Add(sceneActorRow, 0, 0);
        return CreateDisclosureSection("Scene actor diagnostics", body, expanded: false);
    }

    private Control BuildOutputPanel()
    {
        var panel = CreateGroup("Output");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = DarkPanel, Padding = new Padding(4, 8, 4, 4) };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        panel.Controls.Add(grid);

        outputFormatCombo.Items.AddRange(["fbx", "glb"]);
        outputFormatCombo.SelectedIndex = 0;
        outputFormatCombo.SelectedIndexChanged += (_, _) => UpdateBoneSpacingUi();
        textureFormatCombo.Items.AddRange(["png", "dds"]);
        textureFormatCombo.SelectedIndex = 0;
        fbxScaleInput.DecimalPlaces = 2;
        fbxScaleInput.Minimum = 0.01M;
        fbxScaleInput.Maximum = 1000M;
        fbxScaleInput.Value = 100M;
        boneSpacingReferenceActionText.Text = "ch0100_General_0100_Stan_Loop";
        boneSpacingAllowTranslationText.Text = "root,Hip,Null_Offset";
        boneSpacingRepairCheck.CheckedChanged += (_, _) => UpdateBoneSpacingUi();

        grid.Controls.Add(CreatePickerRow("Format", outputFormatCombo), 0, 0);
        grid.Controls.Add(CreatePickerRow("Textures", textureFormatCombo), 0, 1);
        grid.Controls.Add(CreateNumberRow("FBX scale", fbxScaleInput), 0, 2);
        grid.Controls.Add(CreateWidePathRow("Output path", outputPathText, () => BrowseSaveOutput()), 0, 3);
        UpdateBoneSpacingUi();
        return panel;
    }

    private TableLayoutPanel CreateBoneSpacingToggleRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = DarkPanel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        boneSpacingRepairCheck.AutoSize = true;
        boneSpacingRepairCheck.Margin = new Padding(0, RowVerticalMargin, 12, 0);

        row.Controls.Add(CreateFieldLabel("Repair mode"), 0, 0);
        row.Controls.Add(boneSpacingRepairCheck, 1, 0);
        return row;
    }

    private TableLayoutPanel CreateBoneSpacingReferenceRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = DarkPanel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UtilityButtonWidth));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var inputFrame = CreateInputFrame(boneSpacingReferenceFbxText, RowHorizontalGap);
        var browseButton = CreateCompactButton("...");
        browseButton.Dock = DockStyle.Fill;
        tooltips.SetToolTip(browseButton, L("Browse on disk."));
        browseButton.Click += (_, _) => BrowseFile(boneSpacingReferenceFbxText, "FBX|*.fbx|All files|*.*");

        row.Controls.Add(CreateFieldLabel("Reference FBX"), 0, 0);
        row.Controls.Add(inputFrame, 1, 0);
        row.Controls.Add(browseButton, 2, 0);
        return row;
    }

    private TableLayoutPanel CreateBoneSpacingOptionsRow()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = DarkPanel };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.Controls.Add(CreateTextFieldRow("Reference action", boneSpacingReferenceActionText), 0, 0);
        grid.Controls.Add(CreateTextFieldRow("Allowed bones", boneSpacingAllowTranslationText), 0, 1);
        return grid;
    }

    private Control BuildBoneSpacingPanel()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 4, AutoSize = true, BackColor = DarkPanel, Padding = new Padding(4, 8, 4, 4) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        var toggleRow = CreateBoneSpacingToggleRow();
        boneSpacingReferenceRow = CreateBoneSpacingReferenceRow();
        boneSpacingOptionsRow = CreateBoneSpacingOptionsRow();
        body.Controls.Add(toggleRow, 0, 0);
        body.Controls.Add(boneSpacingReferenceRow, 0, 1);
        body.Controls.Add(boneSpacingOptionsRow, 0, 2);
        body.SetRowSpan(boneSpacingOptionsRow, 2);
        return CreateDisclosureSection("Bone spacing repair", body, expanded: false);
    }

    private Control BuildOptionsPanel()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, AutoSize = true, BackColor = DarkPanel, Padding = new Padding(4, 8, 4, 4) };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));

        exportOptionsModeCombo.Items.AddRange(["Default", "Custom"]);
        exportOptionsModeCombo.SelectedIndexChanged += (_, _) => OnExportOptionsModeChanged();
        grid.Controls.Add(CreatePickerRow("Mode", exportOptionsModeCombo), 0, 0);
        exportOptionChecksPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = false, WrapContents = true, BackColor = DarkPanel, Padding = new Padding(118, 4, 0, 0) };
        exportOptionChecksPanel.Controls.AddRange([splitMotlistsCheck, splitAnimationsCheck, noTexturesCheck, includeLodsCheck, includeOcclusionCheck, noPlaceholderBonesCheck, allowMissingStreamingCheck]);
        foreach (var checkBox in GetExportOptionCheckBoxes())
        {
            checkBox.Margin = new Padding(0, 0, 14, 8);
            checkBox.CheckedChanged += (_, _) => OnExportOptionCheckChanged(checkBox);
        }
        grid.Controls.Add(exportOptionChecksPanel, 0, 1);
        return CreateDisclosureSection("Advanced export flags", grid, expanded: false);
    }

    private Control CreateDisclosureSection(string title, Control body, bool expanded)
    {
        var panel = new DisclosurePanel(title, body, expanded)
        {
            Dock = DockStyle.Top,
            BackColor = DarkPanel,
            ForeColor = DarkText,
            Font = Font,
        };
        return panel;
    }

    private Button CreateCompactButton(string text, int width)
    {
        var button = new ThemedButton { Width = Math.Max(width, UtilityButtonWidth), Height = ControlHeight, Margin = new Padding(RowHorizontalGap, RowVerticalMargin, 0, RowVerticalMargin), IconButton = true };
        ApplyUtilityGlyph(button, text);
        return button;
    }

    private Button CreateCompactButton(string text)
        => CreateCompactButton(text, UtilityButtonWidth);

    private static void ApplyUtilityGlyph(Button button, string text)
    {
        var (glyph, name) = text switch
        {
            "Find" => ("\uE721", "Find"),
            "+" => ("\uE710", "Add"),
            "-" => ("\uE738", "Remove"),
            "..." => ("...", "Browse"),
            _ => (text, text),
        };
        button.Text = glyph;
        button.AccessibleName = name;
        button.Font = text == glyph ? button.Font : IconFont(12F);
    }

    private Label CreateFieldLabel(string text)
        => new() { Text = L(text), Width = LabelColumnWidth - 10, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DarkText, BackColor = DarkPanel, AutoEllipsis = true };

    private TableLayoutPanel CreateWidePathRow(string label, TextBox textBox, Action browse, params Button[] extraButtons)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 + extraButtons.Length, RowCount = 1, BackColor = DarkPanel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UtilityButtonWidth));
        foreach (var _ in extraButtons) row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UtilityButtonWidth + 24));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var inputFrame = CreateInputFrame(textBox, RowHorizontalGap);
        var browseButton = CreateCompactButton("...");
        browseButton.Dock = DockStyle.Fill;
        tooltips.SetToolTip(browseButton, L("Browse on disk."));
        browseButton.Click += (_, _) => browse();

        row.Controls.Add(CreateFieldLabel(label), 0, 0);
        row.Controls.Add(inputFrame, 1, 0);
        row.Controls.Add(browseButton, 2, 0);
        for (var i = 0; i < extraButtons.Length; i++)
        {
            extraButtons[i].Dock = DockStyle.Fill;
            row.Controls.Add(extraButtons[i], 3 + i, 0);
        }
        return row;
    }

    private TableLayoutPanel CreateWideListRow(string label, ListBox listBox, params Button[] buttons)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = DarkPanel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(UtilityButtonWidth * 2 + RowHorizontalGap * 3, buttons.Length * (UtilityButtonWidth + RowHorizontalGap) + RowHorizontalGap)));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listBox.Dock = DockStyle.Fill;
        listBox.Margin = new Padding(0, RowVerticalMargin, RowHorizontalGap, RowVerticalMargin);
        var buttonFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = DarkPanel, Padding = new Padding(0) };
        buttonFlow.Controls.AddRange(buttons);

        row.Controls.Add(CreateFieldLabel(label), 0, 0);
        row.Controls.Add(listBox, 1, 0);
        row.Controls.Add(buttonFlow, 2, 0);
        return row;
    }

    private TableLayoutPanel CreatePickerRow(string label, ThemedComboBox picker)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = DarkPanel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        picker.Dock = DockStyle.Fill;
        picker.Height = ControlHeight;
        picker.Margin = new Padding(0, RowVerticalMargin, 0, RowVerticalMargin);
        row.Controls.Add(CreateFieldLabel(label), 0, 0);
        row.Controls.Add(picker, 1, 0);
        return row;
    }

    private TableLayoutPanel CreateNumberRow(string label, ThemedNumericUpDown number)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = DarkPanel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        number.Dock = DockStyle.Left;
        number.Width = 126;
        number.Height = ControlHeight;
        number.Margin = new Padding(0, RowVerticalMargin, 0, RowVerticalMargin);
        row.Controls.Add(CreateFieldLabel(label), 0, 0);
        row.Controls.Add(number, 1, 0);
        return row;
    }

    private TableLayoutPanel CreateTextFieldRow(string label, TextBox textBox)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = DarkPanel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var inputFrame = CreateInputFrame(textBox);
        row.Controls.Add(CreateFieldLabel(label), 0, 0);
        row.Controls.Add(inputFrame, 1, 0);
        return row;
    }

    private ThemedInputFrame CreateInputFrame(TextBox textBox, int rightMargin = 0)
    {
        textBox.BorderStyle = BorderStyle.None;
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0);
        textBox.Multiline = false;
        var frame = new ThemedInputFrame
        {
            Dock = DockStyle.Fill,
            Height = ControlHeight,
            Margin = new Padding(0, RowVerticalMargin, rightMargin, RowVerticalMargin),
        };
        frame.Controls.Add(textBox);
        return frame;
    }

    private TableLayoutPanel CreateAnimationHeaderRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = DarkPanel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        includeAnimationsCheck.Dock = DockStyle.Fill;
        includeAnimationsCheck.Margin = new Padding(0, RowVerticalMargin, RowHorizontalGap, 0);
        animationSourceCombo.Dock = DockStyle.Fill;
        animationSourceCombo.Height = ControlHeight;
        animationSourceCombo.Margin = new Padding(0, RowVerticalMargin, 0, RowVerticalMargin);
        row.Controls.Add(CreateFieldLabel("Include"), 0, 0);
        row.Controls.Add(includeAnimationsCheck, 1, 0);
        row.Controls.Add(new Label { Text = L("Source"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DarkText, BackColor = DarkPanel }, 2, 0);
        row.Controls.Add(animationSourceCombo, 3, 0);
        return row;
    }

    private void AttachPreviewEvents(Control root)
    {
        foreach (Control control in EnumerateControls(root))
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.TextChanged += (_, _) => UpdateCommandPreview();
                    break;
                case ThemedComboBox comboBox:
                    comboBox.SelectedIndexChanged += (_, _) => UpdateCommandPreview();
                    break;
                case CheckBox checkBox:
                    checkBox.CheckedChanged += (_, _) => UpdateCommandPreview();
                    break;
                case ThemedNumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => UpdateCommandPreview();
                    break;
            }
        }
    }

    private TabPage BuildSetupTab()
    {
        var page = CreateTabPage("Setup");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), BackColor = DarkBack };
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
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), BackColor = DarkBack };
        var exportPanel = BuildExportPanel();
        exportPanel.Dock = DockStyle.Top;
        exportPanel.Height = 650;
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
        using var back = new SolidBrush(selected ? DarkPanelAlt : DarkBack);
        e.Graphics.FillRectangle(back, bounds);
        if (selected)
        {
            using var accent = new Pen(Accent, 2);
            e.Graphics.DrawLine(accent, bounds.Left + 14, bounds.Bottom - 3, bounds.Right - 14, bounds.Bottom - 3);
        }
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, bounds, selected ? DarkText : MutedText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private Control BuildGamePanel()
    {
        var panel = CreateGroup("Game configuration");
        var grid = CreateGrid(2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        panel.Controls.Add(grid);

        currentGameLabel.AutoSize = false;
        currentGameLabel.AutoEllipsis = true;
        currentGameLabel.Dock = DockStyle.Fill;
        currentGameLabel.TextAlign = ContentAlignment.MiddleLeft;
        gameCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        gameCombo.Dock = DockStyle.Fill;
        gameCombo.Height = ControlHeight;
        gameCombo.Margin = new Padding(0, RowVerticalMargin, 0, RowVerticalMargin);
        gameCombo.DisplayMember = nameof(WizardGameDefinition.DisplayName);
        gameCombo.ValueMember = nameof(WizardGameDefinition.Id);
        gameCombo.Items.AddRange(WizardGames.Definitions.Cast<object>().ToArray());
        gameCombo.SelectedIndexChanged += (_, _) => UpdateCommandPreview();
        savedGameValueLabel.AutoSize = false;
        savedGameValueLabel.AutoEllipsis = true;
        savedGameValueLabel.Dock = DockStyle.Fill;
        savedGameValueLabel.Margin = gameCombo.Margin;
        savedGameValueLabel.Padding = new Padding(6, 0, 6, 0);
        savedGameValueLabel.TextAlign = ContentAlignment.MiddleLeft;
        savedGameValueLabel.BorderStyle = BorderStyle.FixedSingle;
        savedGameValueLabel.BackColor = DarkInput;
        savedGameValueLabel.ForeColor = MutedText;
        savedGameValueLabel.Visible = false;

        saveGameButton = new ThemedButton { Text = "Set", Dock = DockStyle.Fill, Margin = new Padding(6, RowVerticalMargin, 0, RowVerticalMargin), AccentButton = true };
        saveGameButton.Click += async (_, _) => await SaveSelectedGameAsync();
        changeGameButton = new ThemedButton { Text = "Edit", Dock = DockStyle.Fill, Margin = new Padding(6, RowVerticalMargin, 0, RowVerticalMargin) };
        changeGameButton.Click += (_, _) => ClearSelectedGame();

        grid.Controls.Add(new Label { Text = "Current", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(currentGameLabel, 1, 0);
        grid.Controls.Add(changeGameButton, 2, 0);
        grid.Controls.Add(saveGameButton, 3, 0);
        grid.Controls.Add(new Label { Text = "Select game", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(gameCombo, 1, 1);
        grid.SetColumnSpan(gameCombo, 3);
        grid.Controls.Add(savedGameValueLabel, 1, 1);
        grid.SetColumnSpan(savedGameValueLabel, 3);
        return panel;
    }

    private Control BuildPathPanel()
    {
        var panel = CreateGroup("Paths");
        var grid = CreateGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UtilityButtonWidth));
        panel.Controls.Add(grid);

        AddPathRow(grid, 0, "Extract root", extractRootText, () => BrowseFolder(extractRootText));
        AddPathRow(grid, 1, "Export folder", exportRootText, () => BrowseFolder(exportRootText));
        AddPathRow(grid, 2, "Blender 4.5.9", blenderPathText, () => BrowseFile(blenderPathText, "blender.exe|blender.exe|Executable|*.exe|All files|*.*"));
        blenderPathText.TextChanged += (_, _) => UpdateBoneSpacingUi();
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
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.Controls.Add(grid);

        var findMeshButton = CreateCompactButton("Find", 58);
        tooltips.SetToolTip(findMeshButton, L("Search the saved game list for a primary mesh path."));
        findMeshButton.Click += (_, _) => PickAssetFromList(meshText, AssetPickerKind.Mesh);
        grid.Controls.Add(CreatePathRow("Primary mesh", meshText, () => BrowseFile(meshText, "RE Engine mesh|*.mesh*|All files|*.*"), findMeshButton), 0, 0);

        var addMeshButton = CreateCompactButton("+", 44);
        tooltips.SetToolTip(addMeshButton, L("Add an additional mesh from disk."));
        addMeshButton.Click += (_, _) => AddAdditionalMesh();
        var removeMeshButton = CreateCompactButton("-", 44);
        tooltips.SetToolTip(removeMeshButton, L("Remove the selected additional mesh."));
        removeMeshButton.Click += (_, _) => RemoveSelectedAdditionalMesh();
        grid.Controls.Add(CreateListRow("Additional meshes", additionalMeshList, addMeshButton, removeMeshButton), 0, 1);

        includeAnimationsCheck.CheckedChanged += (_, _) => UpdateAnimationSourceUi();
        animationSourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        animationSourceCombo.Items.AddRange(["MOTLIST folder", "MOTLIST files", "MOT files"]);
        animationSourceCombo.SelectedIndex = 0;
        animationSourceCombo.SelectedIndexChanged += (_, _) => UpdateAnimationSourceUi();
        grid.Controls.Add(CreateAnimationSourceRow(), 0, 2);

        var findMotlistButton = CreateCompactButton("Find", 58);
        tooltips.SetToolTip(findMotlistButton, L("Search the saved game list for a MOTLIST folder."));
        findMotlistButton.Click += (_, _) => PickAssetFromList(motlistDirText, AssetPickerKind.MotlistDirectory);
        motlistDirRow = CreatePathRow("MOTLIST folder", motlistDirText, () => BrowseFolder(motlistDirText), findMotlistButton);
        grid.Controls.Add(motlistDirRow, 0, 3);

        var addAnimationFileButton = CreateCompactButton("+", 44);
        tooltips.SetToolTip(addAnimationFileButton, L("Add a MOTLIST or MOT file from disk."));
        addAnimationFileButton.Click += (_, _) => AddAnimationFileFromDisk();
        var findAnimationFileButton = CreateCompactButton("Find", 58);
        tooltips.SetToolTip(findAnimationFileButton, L("Search the saved game list for animation files."));
        findAnimationFileButton.Click += (_, _) => PickAnimationFileFromList();
        var removeAnimationFileButton = CreateCompactButton("-", 44);
        tooltips.SetToolTip(removeAnimationFileButton, L("Remove the selected animation file."));
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
                case ThemedComboBox comboBox:
                    comboBox.SelectedIndexChanged += (_, _) => UpdateCommandPreview();
                    break;
                case CheckBox checkBox:
                    checkBox.CheckedChanged += (_, _) => UpdateCommandPreview();
                    break;
                case ThemedNumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => UpdateCommandPreview();
                    break;
            }
        }

        UpdateAnimationSourceUi();
        return panel;

        Button CreateCompactButton(string text, int width)
            => new ThemedButton() { Text = text, Width = Math.Max(width, UtilityButtonWidth), Height = ControlHeight, Margin = new Padding(RowHorizontalGap, RowVerticalMargin, 0, RowVerticalMargin) };

        Label CreateRowLabel(string text)
            => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 0) };

        TableLayoutPanel CreatePathRow(string label, TextBox textBox, Action browse, params Button[] extraButtons)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 + extraButtons.Length, RowCount = 1, BackColor = DarkPanel };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            foreach (var _ in extraButtons) row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 5, 6, 5);
            var browseButton = CreateCompactButton("...", 46);
            browseButton.Dock = DockStyle.Fill;
            tooltips.SetToolTip(browseButton, L("Browse on disk."));
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
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(190, buttons.Length * 86)));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            listBox.Dock = DockStyle.Fill;
            listBox.Margin = new Padding(0, 5, 6, 5);
            var buttonFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = DarkPanel, Padding = new Padding(0, 4, 0, 0) };
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

            var optionFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = false, WrapContents = false, BackColor = DarkPanel, Padding = new Padding(0, 6, 0, 0) };
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

            exportOptionChecksPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = false, WrapContents = true, BackColor = DarkPanel, Padding = new Padding(0, 8, 0, 0) };
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
        var panel = CreateGroup("Progress and log");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
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
        logText.Dock = DockStyle.Fill;
        logText.Multiline = true;
        logText.ReadOnly = true;
        logText.ScrollBars = ScrollBars.None;
        logText.WordWrap = true;
        logText.Font = new Font(FontFamily.GenericMonospace, 9F);
        logText.Margin = new Padding(8, 4, 8, 8);

        grid.Controls.Add(progressRow, 0, 0);
        grid.Controls.Add(logText, 0, 1);
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
        languageCombo.Height = ControlHeight;
        languageCombo.Margin = new Padding(0, RowVerticalMargin, 0, RowVerticalMargin);
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
        savePathsButton = new ThemedButton { Text = "Save Paths" };
        ConfigureActionButton(savePathsButton, leftMargin: 0);
        savePathsButton.Click += (_, _) => SavePathConfig();
        copyCommandButton = new ThemedButton { Text = "Copy Command" };
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
            button.Height = ControlHeight;
            button.Margin = new Padding(leftMargin, RowVerticalMargin, 0, RowVerticalMargin);
        }
    }

    private sealed class ThemedButton : Button
    {
        private bool hover;
        private bool pressed;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AccentButton { get; init; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IconButton { get; init; }

        public ThemedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            BackColor = ButtonBase;
            ForeColor = DarkText;
            MinimumSize = new Size(0, ControlHeight);
            Padding = new Padding(10, 0, 10, 0);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var back = ResolveBackColor();
            using var path = RoundedRect(rect, 8);
            using var fill = new SolidBrush(back);
            e.Graphics.FillPath(fill, path);
            using var border = new Pen(Enabled ? (AccentButton ? AccentHover : Accent) : DarkBorder, AccentButton ? 1.7f : 1.2f);
            e.Graphics.DrawPath(border, path);
            var textColor = !Enabled ? MutedText : AccentButton ? DarkBack : ForeColor;
            var flags = TextAlign == ContentAlignment.MiddleLeft
                ? TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                : TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var textRect = IconButton ? ClientRectangle : new Rectangle(Padding.Left, 0, Math.Max(1, Width - Padding.Horizontal), Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                textColor,
                flags);
        }

        private Color ResolveBackColor()
        {
            if (!Enabled) return DisabledBack;
            if (pressed) return AccentButton ? ButtonPressed : Color.FromArgb(50, 45, 40);
            if (hover) return AccentButton ? AccentHover : ButtonHover;
            return AccentButton ? Accent : ButtonBase;
        }
    }

    private sealed class ThemedInputFrame : Panel
    {
        public ThemedInputFrame()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = DarkInput;
            ForeColor = DarkText;
            Padding = new Padding(10, 11, 10, 7);
            MinimumSize = new Size(0, ControlHeight);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, 8);
            using var fill = new SolidBrush(Enabled ? DarkInput : DisabledBack);
            using var border = new Pen(Enabled ? DarkBorder : Color.FromArgb(48, 54, 64));
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class ThemedProgressBar : Control
    {
        private int minimum;
        private int maximum = 100;
        private int value;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minimum
        {
            get => minimum;
            set
            {
                minimum = value;
                if (maximum < minimum) maximum = minimum;
                Value = Math.Clamp(this.value, minimum, maximum);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => maximum;
            set
            {
                maximum = Math.Max(value, minimum);
                Value = Math.Clamp(this.value, minimum, maximum);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => value;
            set
            {
                this.value = Math.Clamp(value, minimum, maximum);
                Invalidate();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ProgressBarStyle Style { get; set; } = ProgressBarStyle.Blocks;

        public ThemedProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = DarkInput;
            ForeColor = Accent;
            MinimumSize = new Size(0, 18);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var backPath = RoundedRect(rect, 7);
            using var back = new SolidBrush(DarkInput);
            using var border = new Pen(DarkBorder);
            e.Graphics.FillPath(back, backPath);
            e.Graphics.DrawPath(border, backPath);

            var range = Math.Max(1, maximum - minimum);
            var fillWidth = (int)Math.Round((Width - 4) * ((value - minimum) / (double)range));
            if (fillWidth <= 0) return;
            var fillRect = new Rectangle(2, 2, Math.Min(fillWidth, Width - 4), Math.Max(1, Height - 4));
            using var fillPath = RoundedRect(fillRect, 6);
            using var fill = new LinearGradientBrush(fillRect, Accent, AccentHover, LinearGradientMode.Horizontal);
            e.Graphics.FillPath(fill, fillPath);
        }
    }

    private sealed class ThemedComboBox : Control
    {
        private int selectedIndex = -1;
        private ContextMenuStrip? openMenu;

        public event EventHandler? SelectedIndexChanged;
        public ThemedComboBoxItemCollection Items { get; } = new();
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string DisplayMember { get; set; } = "";
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ValueMember { get; set; } = "";
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ComboBoxStyle DropDownStyle { get; set; } = ComboBoxStyle.DropDownList;
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public FlatStyle FlatStyle { get; set; } = FlatStyle.Flat;
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ItemHeight { get; set; } = 22;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                var next = Items.Count == 0 ? -1 : Math.Clamp(value, -1, Items.Count - 1);
                if (selectedIndex == next) return;
                selectedIndex = next;
                Text = SelectedItem == null ? "" : GetItemText(SelectedItem);
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public object? SelectedItem
        {
            get => selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null;
            set
            {
                if (value == null)
                {
                    SelectedIndex = -1;
                    return;
                }

                for (var i = 0; i < Items.Count; i++)
                {
                    if (ReferenceEquals(Items[i], value) || string.Equals(GetItemText(Items[i]), value.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        public ThemedComboBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = DarkInput;
            ForeColor = DarkText;
            Height = ControlHeight;
            Cursor = Cursors.Hand;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Height = ControlHeight;
        }

        public void BeginUpdate()
        {
        }

        public void EndUpdate()
        {
            Invalidate();
        }

        public string GetItemText(object item)
        {
            if (item == null) return "";
            if (!string.IsNullOrWhiteSpace(DisplayMember))
            {
                var property = item.GetType().GetProperty(DisplayMember);
                if (property?.GetValue(item) is { } value) return value.ToString() ?? "";
            }
            return item.ToString() ?? "";
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            ShowDropDown();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space || e.KeyCode == Keys.Down && e.Alt)
            {
                ShowDropDown();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && selectedIndex > 0)
            {
                SelectedIndex--;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down && selectedIndex < Items.Count - 1)
            {
                SelectedIndex++;
                e.Handled = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                openMenu?.Dispose();
                openMenu = null;
            }
            base.Dispose(disposing);
        }

        private void ShowDropDown()
        {
            if (!Enabled || Items.Count == 0) return;
            if (openMenu is { IsDisposed: false })
            {
                openMenu.Close();
                return;
            }

            var menu = new ContextMenuStrip
            {
                BackColor = DarkPanelAlt,
                ForeColor = DarkText,
                Renderer = new DarkMenuRenderer(),
                ShowImageMargin = false,
            };
            menu.MinimumSize = new Size(Width, 0);
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(openMenu, menu)) openMenu = null;
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(() =>
                    {
                        if (!menu.IsDisposed) menu.Dispose();
                    });
                }
                else if (!menu.IsDisposed)
                {
                    menu.Dispose();
                }
            };
            for (var i = 0; i < Items.Count; i++)
            {
                var index = i;
                var item = new ToolStripMenuItem(GetItemText(Items[i])) { Checked = i == selectedIndex };
                item.Click += (_, _) => SelectedIndex = index;
                menu.Items.Add(item);
            }
            openMenu = menu;
            menu.Show(this, new Point(0, Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var back = new SolidBrush(Enabled ? DarkInput : DisabledBack);
            using var border = new Pen(Enabled ? DarkBorder : Color.FromArgb(48, 52, 60));
            e.Graphics.FillRectangle(back, rect);
            e.Graphics.DrawRectangle(border, rect);
            var arrowRect = new Rectangle(Width - 28, 1, 26, Height - 2);
            using var arrowBack = new SolidBrush(Enabled ? DarkPanelAlt : DisabledBack);
            e.Graphics.FillRectangle(arrowBack, arrowRect);
            DrawArrow(e.Graphics, arrowRect, Enabled ? DarkText : MutedText);
            var textRect = new Rectangle(8, 1, Math.Max(1, Width - 38), Height - 2);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Enabled ? DarkText : MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static void DrawArrow(Graphics graphics, Rectangle rect, Color color)
        {
            var midX = rect.Left + rect.Width / 2;
            var midY = rect.Top + rect.Height / 2 + 1;
            using var brush = new SolidBrush(color);
            graphics.FillPolygon(brush, [new Point(midX - 4, midY - 2), new Point(midX + 4, midY - 2), new Point(midX, midY + 3)]);
        }

        private static int PreferredControlHeight(Font font)
            => Math.Max(34, font.Height + 14);

        public sealed class ThemedComboBoxItemCollection : List<object>
        {
            public void AddRange(params object[] items) => base.AddRange(items);
        }

        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var selected = e.Item.Selected || (e.Item is ToolStripMenuItem { Checked: true });
                using var brush = new SolidBrush(selected ? ButtonHover : DarkPanelAlt);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using var pen = new Pen(DarkBorder);
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }
    }

    private sealed class ThemedCheckBox : CheckBox
    {
        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            BackColor = DarkPanel;
            ForeColor = DarkText;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(1, Math.Max(1, (Height - 17) / 2), 16, 16);
            using var boxBack = new SolidBrush(Enabled ? DarkInput : DisabledBack);
            using var border = new Pen(Enabled ? Accent : DarkBorder, 1.4f);
            e.Graphics.FillRectangle(boxBack, box);
            e.Graphics.DrawRectangle(border, box);
            if (Checked)
            {
                using var check = new Pen(Enabled ? AccentHover : MutedText, 2f);
                e.Graphics.DrawLines(check, [new Point(box.Left + 3, box.Top + 8), new Point(box.Left + 7, box.Top + 12), new Point(box.Right - 3, box.Top + 4)]);
            }
            var textRect = new Rectangle(24, 0, Math.Max(1, Width - 24), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Enabled ? ForeColor : MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private sealed class ThemedNumericUpDown : Control
    {
        private decimal minimum;
        private decimal maximum = 100;
        private decimal value;

        public event EventHandler? ValueChanged;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int DecimalPlaces { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Minimum
        {
            get => minimum;
            set
            {
                minimum = value;
                if (maximum < minimum) maximum = minimum;
                Value = Math.Clamp(this.value, minimum, maximum);
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Maximum
        {
            get => maximum;
            set
            {
                maximum = Math.Max(value, minimum);
                Value = Math.Clamp(this.value, minimum, maximum);
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Value
        {
            get => value;
            set
            {
                var next = Math.Clamp(value, minimum, maximum);
                if (this.value == next) return;
                this.value = next;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ThemedNumericUpDown()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = DarkInput;
            ForeColor = DarkText;
            Cursor = Cursors.Hand;
            Height = ControlHeight;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Height = ControlHeight;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Step(e.Delta > 0 ? 1 : -1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var buttonRect = new Rectangle(Width - 26, 1, 25, Height - 2);
            if (!buttonRect.Contains(e.Location)) return;
            Step(e.Y < Height / 2 ? 1 : -1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up) Step(1);
            if (e.KeyCode == Keys.Down) Step(-1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var back = new SolidBrush(DarkInput);
            using var border = new Pen(DarkBorder);
            e.Graphics.FillRectangle(back, rect);
            e.Graphics.DrawRectangle(border, rect);
            var textRect = new Rectangle(8, 1, Math.Max(1, Width - 34), Height - 2);
            TextRenderer.DrawText(e.Graphics, Value.ToString(DecimalPlaces == 0 ? "0" : "0." + new string('0', DecimalPlaces)), Font, textRect, DarkText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            var buttonRect = new Rectangle(Width - 26, 1, 25, Height - 2);
            using var buttonBack = new SolidBrush(DarkPanelAlt);
            e.Graphics.FillRectangle(buttonBack, buttonRect);
            DrawSpinner(e.Graphics, buttonRect);
        }

        private void Step(int direction)
        {
            var increment = DecimalPlaces <= 0 ? 1M : 1M / (decimal)Math.Pow(10, DecimalPlaces);
            Value += increment * direction;
        }

        private static void DrawSpinner(Graphics g, Rectangle rect)
        {
            var topY = rect.Top + rect.Height / 3;
            var bottomY = rect.Top + (rect.Height * 2 / 3) + 1;
            var midX = rect.Left + rect.Width / 2;
            using var brush = new SolidBrush(DarkText);
            g.FillPolygon(brush, [new Point(midX, topY - 3), new Point(midX - 4, topY + 2), new Point(midX + 4, topY + 2)]);
            g.FillPolygon(brush, [new Point(midX, bottomY + 3), new Point(midX - 4, bottomY - 2), new Point(midX + 4, bottomY - 2)]);
        }

        private static int PreferredControlHeight(Font font)
            => Math.Max(34, font.Height + 14);
    }

    private static ModernSectionPanel CreateGroup(string title)
        => new(title) { Dock = DockStyle.Fill, Padding = new Padding(14, 50, 14, 14) };

    private sealed class ModernSectionPanel : Panel
    {
        public ModernSectionPanel(string title)
        {
            Text = title;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = DarkPanel;
            ForeColor = DarkText;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? DarkBack);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, 12);
            using var fill = new SolidBrush(DarkPanel);
            using var border = new Pen(DarkBorder);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            var titleRect = new Rectangle(16, 14, Math.Max(1, Width - 32), 28);
            using var titleFont = UiFont(10F, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, Text, titleFont, titleRect, DarkText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private sealed class DisclosurePanel : Panel
    {
        private readonly ThemedButton header;
        private readonly Panel bodyHost;
        private readonly string titleKey;
        private bool expanded;
        private int collapsedHeight = 54;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CollapsedHeight
        {
            get => collapsedHeight;
            set
            {
                collapsedHeight = Math.Max(44, value);
                UpdatePanelHeight();
            }
        }

        public DisclosurePanel(string title, Control body, bool expanded)
        {
            titleKey = title;
            this.expanded = expanded;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            BackColor = DarkPanel;
            ForeColor = DarkText;
            Padding = new Padding(0);

            header = new ThemedButton { Dock = DockStyle.Top, Height = 46, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), Padding = new Padding(14, 0, 14, 0) };
            header.Click += (_, _) => Toggle();
            bodyHost = new Panel { Dock = DockStyle.Top, Padding = new Padding(10, 0, 10, 12), BackColor = DarkPanel };
            body.Dock = DockStyle.Top;
            bodyHost.Controls.Add(body);
            Controls.Add(bodyHost);
            Controls.Add(header);
            SetHeaderText(title);
            SetExpanded(expanded);
        }

        public void ApplyLocalizedTitle(Func<string, string> localize)
            => SetHeaderText(localize(titleKey));

        private void Toggle() => SetExpanded(!expanded);

        private void SetExpanded(bool value)
        {
            expanded = value;
            bodyHost.Visible = expanded;
            SetHeaderText(header.AccessibleName ?? header.Text);
            UpdatePanelHeight();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdatePanelHeight();
        }

        private void UpdatePanelHeight()
        {
            if (!expanded)
            {
                Height = collapsedHeight;
                return;
            }

            var body = bodyHost.Controls.Count > 0 ? bodyHost.Controls[0] : null;
            var bodyHeight = body?.Height > 0 ? body.Height : body?.PreferredSize.Height ?? 0;
            bodyHost.Height = bodyHeight + bodyHost.Padding.Vertical;
            Height = header.Height + bodyHost.Height + 2;
        }

        private void SetHeaderText(string title)
        {
            var cleanTitle = title.TrimStart('v', '>', ' ');
            header.AccessibleName = cleanTitle;
            header.Text = (expanded ? "v  " : ">  ") + cleanTitle;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? DarkBack);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, 12);
            using var fill = new SolidBrush(DarkPanel);
            using var border = new Pen(DarkBorder);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
    }

    private TableLayoutPanel CreateGrid(int rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = rows, BackColor = DarkPanel };
        for (var i = 0; i < rows; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, FieldRowHeight));
        return grid;
    }

    private static void ApplyDarkTheme(Control root)
    {
        root.BackColor = root is TextBoxBase or ListBox or ThemedComboBox or ThemedNumericUpDown ? DarkInput : root is GroupBox ? DarkPanel : root.BackColor == SystemColors.Control ? DarkBack : root.BackColor;
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
                textBox.BorderStyle = textBox.Parent is ThemedInputFrame ? BorderStyle.None : BorderStyle.FixedSingle;
                break;
            case ListBox listBox:
                listBox.BackColor = DarkInput;
                listBox.ForeColor = DarkText;
                listBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ThemedComboBox comboBox:
                comboBox.BackColor = DarkInput;
                comboBox.ForeColor = DarkText;
                break;
            case ThemedNumericUpDown numeric:
                numeric.BackColor = DarkInput;
                numeric.ForeColor = DarkText;
                numeric.Height = ControlHeight;
                break;
            case ThemedInputFrame inputFrame:
                inputFrame.BackColor = DarkInput;
                inputFrame.ForeColor = DarkText;
                break;
            case Button button:
                button.BackColor = button is ThemedButton ? button.BackColor : ButtonBase;
                button.ForeColor = DarkText;
                if (button is not ThemedButton)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Accent;
                    button.FlatAppearance.MouseOverBackColor = ButtonHover;
                    button.FlatAppearance.MouseDownBackColor = ButtonPressed;
                }
                button.UseCompatibleTextRendering = false;
                button.TextAlign = ContentAlignment.MiddleCenter;
                button.MinimumSize = new Size(0, ControlHeight);
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

    private void AddPathRow(TableLayoutPanel grid, int row, string label, TextBox textBox, Action browse, int startColumn = 0)
    {
        var inputFrame = CreateInputFrame(textBox, RowHorizontalGap);
        var browseButton = new ThemedButton { Dock = DockStyle.Fill, Margin = new Padding(RowHorizontalGap, RowVerticalMargin, 0, RowVerticalMargin), Height = ControlHeight, IconButton = true };
        ApplyUtilityGlyph(browseButton, "...");
        browseButton.Click += (_, _) => browse();
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, startColumn, row);
        grid.Controls.Add(inputFrame, startColumn + 1, row);
        grid.SetColumnSpan(inputFrame, startColumn == 0 ? 1 : 2);
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
            currentGameLabel.Text = $"{game.DisplayName} ({game.Id})";
            savedGameValueLabel.Text = game.DisplayName;
            savedGameValueLabel.Visible = true;
            gameCombo.Visible = false;
            gameCombo.Enabled = false;
            if (saveGameButton != null) saveGameButton.Enabled = false;
            if (changeGameButton != null) changeGameButton.Enabled = true;
        }
        else if (!string.IsNullOrWhiteSpace(config.Game))
        {
            currentGameLabel.Text = IsKorean()
                ? $"지원하지 않는 저장 게임: {config.Game}"
                : $"Unsupported saved game: {config.Game}";
            savedGameValueLabel.Text = config.Game;
            savedGameValueLabel.Visible = true;
            gameCombo.Visible = false;
            gameCombo.Enabled = false;
            if (saveGameButton != null) saveGameButton.Enabled = false;
            if (changeGameButton != null) changeGameButton.Enabled = true;
        }
        else
        {
            currentGameLabel.Text = L("No game saved. Select a game, then click Set.");
            savedGameValueLabel.Visible = false;
            gameCombo.Visible = true;
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
        if (root is DisclosurePanel disclosure)
        {
            disclosure.ApplyLocalizedTitle(L);
        }
        if (root is not ThemedComboBox && !string.IsNullOrWhiteSpace(root.Text))
        {
            root.Text = L(root.Text);
        }
        foreach (Control child in root.Controls) LocalizeControlTree(child);
    }

    private static void UpdateComboItems(ThemedComboBox comboBox, string[] englishItems, string[] localizedItems)
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
        tooltips.AutoPopDelay = 20000;
        tooltips.InitialDelay = 250;
        tooltips.ReshowDelay = 80;
        tooltips.ShowAlways = true;

        tooltips.SetToolTip(languageCombo, L("Choose the GUI language. The setting is saved immediately."));
        tooltips.SetToolTip(exportOptionsModeCombo, L("Default uses the legacy CLI wizard preferences. Custom enables and saves these checkboxes."));
        tooltips.SetToolTip(animationFilterText, L("Optional. Maps to --animation-name <contains> and filters exported animation names after sources are selected."));
        tooltips.SetToolTip(sceneActorText, L("Optional. Maps to --scene-actor <actor-id>, for example ch0100 or ch0000."));
        tooltips.SetToolTip(allowMixedSceneAnimationsCheck, L("Diagnostic only. Maps to --allow-mixed-scene-animations and allows multiple scene actors on one armature."));
        tooltips.SetToolTip(boneSpacingRepairCheck, L("Opt-in scene animation repair. Requires animated FBX output and Blender."));
        tooltips.SetToolTip(boneSpacingReferenceFbxText, L("Reference Unreal-ready FBX used to clamp non-allowlisted local bone translations."));
        tooltips.SetToolTip(boneSpacingReferenceActionText, L("Optional reference action name filter for the bone-spacing reference FBX."));
        tooltips.SetToolTip(boneSpacingAllowTranslationText, L("Comma-separated bones allowed to keep local translation curves, such as root,Hip,Null_Offset."));
        SetPathPreviewTooltip(extractRootText, L("Extract root"));
        SetPathPreviewTooltip(exportRootText, L("Export folder"));
        SetPathPreviewTooltip(blenderPathText, L("Blender 4.5.9"));
        SetPathPreviewTooltip(meshText, L("Primary mesh"));
        SetPathPreviewTooltip(motlistDirText, L("MOTLIST folder"));
        SetPathPreviewTooltip(outputPathText, L("Output path"));
        SetListPreviewTooltip(additionalMeshList, L("Additional meshes"));
        SetListPreviewTooltip(animationFileList, L("Animation files"));
        if (savePathsButton != null) tooltips.SetToolTip(savePathsButton, L("Save extract, export, Blender, texture, language, and GUI option settings."));
        if (copyCommandButton != null) tooltips.SetToolTip(copyCommandButton, L("Copy the generated CLI command preview to the clipboard."));
        tooltips.SetToolTip(cancelButton, L("Cancel the running export process."));
        tooltips.SetToolTip(runButton, L("Run the export with the current GUI settings."));
    }

    private void SetPathPreviewTooltip(TextBox textBox, string label)
    {
        void Refresh()
        {
            var value = string.IsNullOrWhiteSpace(textBox.Text) ? L("No path selected.") : textBox.Text.Trim();
            tooltips.SetToolTip(textBox, $"{label}: {value}");
        }

        Refresh();
        textBox.TextChanged += (_, _) => Refresh();
        textBox.MouseEnter += (_, _) => Refresh();
    }

    private void SetListPreviewTooltip(ListBox listBox, string label)
    {
        listBox.MouseMove += (_, e) =>
        {
            var index = listBox.IndexFromPoint(e.Location);
            if (index >= 0 && index < listBox.Items.Count)
            {
                tooltips.SetToolTip(listBox, $"{label}: {listBox.Items[index]}");
            }
            else
            {
                tooltips.SetToolTip(listBox, label);
            }
        };
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
        ["Build Unreal-ready assets with guided export paths, validation, and persistent logs."] = "가이드형 경로 설정, 검증, 영구 로그로 Unreal-ready 에셋을 내보냅니다.",
        ["Export readiness"] = "내보내기 준비 상태",
        ["Ready to export"] = "내보낼 준비 완료",
        ["The required fields are complete. Review the command, then run the export."] = "필수 항목이 완료되었습니다. 명령을 확인한 뒤 내보내기를 실행하세요.",
        ["Needs attention"] = "확인이 필요합니다",
        ["Command preview"] = "명령 미리보기",
        ["Progress and log"] = "진행률 및 로그",
        ["Current"] = "현재",
        ["Select game"] = "게임 선택",
        ["Set"] = "설정",
        ["Edit"] = "편집",
        ["Paths"] = "경로",
        ["Extract root"] = "추출 루트",
        ["Export folder"] = "내보내기 폴더",
        ["Blender 4.5.9"] = "Blender 4.5.9",
        ["Assets"] = "에셋",
        ["Animation"] = "애니메이션",
        ["Export setup"] = "내보내기 설정",
        ["Primary mesh"] = "기본 메시",
        ["Additional meshes"] = "추가 메시",
        ["Animations"] = "애니메이션",
        ["Include"] = "포함",
        ["Include animations"] = "애니메이션 포함",
        ["Source"] = "소스",
        ["MOTLIST folder"] = "MOTLIST 폴더",
        ["MOTLIST files"] = "MOTLIST 파일",
        ["MOT files"] = "MOT 파일",
        ["Animation files"] = "애니메이션 파일",
        ["Animation name filter"] = "애니메이션 이름 필터",
        ["Name filter"] = "이름 필터",
        ["Scene actor"] = "씬 액터",
        ["Allow mixed scene actors"] = "혼합 씬 액터 허용",
        ["Scene actor diagnostics"] = "씬 액터 진단",
        ["Output"] = "출력",
        ["Format"] = "형식",
        ["Textures"] = "텍스처",
        ["FBX scale"] = "FBX 스케일",
        ["Bone reference"] = "본 기준",
        ["Bone spacing repair"] = "본 간격 복구",
        ["Reference action"] = "기준 액션",
        ["Allow translate"] = "이동 허용",
        ["Options"] = "옵션",
        ["Mode"] = "모드",
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
        ["Advanced export flags"] = "고급 내보내기 옵션",
        ["Output path"] = "출력 경로",
        ["Progress and command"] = "진행 및 명령",
        ["Language"] = "언어",
        ["Save Paths"] = "경로 저장",
        ["Copy Command"] = "명령 복사",
        ["Cancel"] = "취소",
        ["Run Export"] = "내보내기 실행",
        ["Find"] = "찾기",
        ["Choose"] = "선택",
        ["Search"] = "검색",
        ["Type part of a filename or path"] = "파일 이름 또는 경로 일부 입력",
        ["No path selected."] = "선택된 경로가 없습니다.",
        ["Search the saved game list for a primary mesh path."] = "저장된 게임 목록에서 기본 메시 경로를 검색합니다.",
        ["Search the saved game list for a MOTLIST folder."] = "저장된 게임 목록에서 MOTLIST 폴더를 검색합니다.",
        ["Search the saved game list for animation files."] = "저장된 게임 목록에서 애니메이션 파일을 검색합니다.",
        ["Add an additional mesh from disk."] = "디스크에서 추가 메시를 더합니다.",
        ["Remove the selected additional mesh."] = "선택한 추가 메시를 제거합니다.",
        ["Add a MOTLIST or MOT file from disk."] = "디스크에서 MOTLIST 또는 MOT 파일을 더합니다.",
        ["Remove the selected animation file."] = "선택한 애니메이션 파일을 제거합니다.",
        ["Browse on disk."] = "디스크에서 찾아봅니다.",
        ["No game saved. Select a game, then click Set."] = "저장된 게임이 없습니다. 게임을 선택한 뒤 설정을 누르세요.",
        ["Cleared game configuration. Select a game and click Set."] = "게임 구성을 지웠습니다. 게임을 선택한 뒤 설정을 누르세요.",
        ["Saved path and texture settings."] = "경로 및 텍스처 설정을 저장했습니다.",
        ["Game configuration failed: "] = "게임 구성 실패: ",
        ["Choose the GUI language. The setting is saved immediately."] = "GUI 언어를 선택합니다. 설정은 즉시 저장됩니다.",
        ["Default uses the legacy CLI wizard preferences. Custom enables and saves these checkboxes."] = "기본값은 기존 CLI 마법사 선호 설정을 사용합니다. 사용자 지정은 체크박스를 활성화하고 저장합니다.",
        ["Optional. Maps to --animation-name <contains> and filters exported animation names after sources are selected."] = "선택 사항입니다. --animation-name <contains>에 대응하며 소스 선택 후 내보낼 애니메이션 이름을 필터링합니다.",
        ["Optional. Maps to --scene-actor <actor-id>, for example ch0100 or ch0000."] = "선택 사항입니다. --scene-actor <actor-id>에 대응합니다. 예: ch0100 또는 ch0000.",
        ["Diagnostic only. Maps to --allow-mixed-scene-animations and allows multiple scene actors on one armature."] = "진단 전용입니다. --allow-mixed-scene-animations에 대응하며 여러 씬 액터를 하나의 아마추어에 허용합니다.",
        ["Opt-in scene animation repair. Requires animated FBX output and Blender."] = "선택식 씬 애니메이션 복구입니다. 애니메이션 FBX 출력과 Blender가 필요합니다.",
        ["Reference Unreal-ready FBX used to clamp non-allowlisted local bone translations."] = "허용 목록 밖의 로컬 본 이동을 고정하는 데 쓰는 Unreal-ready 기준 FBX입니다.",
        ["Optional reference action name filter for the bone-spacing reference FBX."] = "본 간격 기준 FBX에서 사용할 기준 액션 이름 필터입니다.",
        ["Comma-separated bones allowed to keep local translation curves, such as root,Hip,Null_Offset."] = "로컬 이동 커브를 유지할 본을 쉼표로 구분합니다. 예: root,Hip,Null_Offset.",
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
        http.DefaultRequestHeaders.UserAgent.ParseAdd("REE-Content-Exporter/0.6");
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
        if (includeAnimationsCheck.Checked && !string.IsNullOrWhiteSpace(sceneActorText.Text))
        {
            args.Add("--scene-actor");
            args.Add(sceneActorText.Text.Trim());
        }
        if (includeAnimationsCheck.Checked && allowMixedSceneAnimationsCheck.Checked)
        {
            args.Add("--allow-mixed-scene-animations");
        }
        if (includeAnimationsCheck.Checked
            && string.Equals(outputFormatCombo.SelectedItem?.ToString(), "fbx", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(blenderPathText.Text))
        {
            args.Add("--unreal-ready-fbx");
            args.Add("--blender");
            args.Add(blenderPathText.Text.Trim());
        }
        if (boneSpacingRepairCheck.Checked)
        {
            if (!includeAnimationsCheck.Checked)
                throw new InvalidOperationException("Bone spacing repair requires animations.");
            if (!string.Equals(outputFormatCombo.SelectedItem?.ToString(), "fbx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Bone spacing repair requires FBX output.");
            if (string.IsNullOrWhiteSpace(blenderPathText.Text))
                throw new InvalidOperationException("Bone spacing repair requires Blender 4.5.9.");
            if (string.IsNullOrWhiteSpace(boneSpacingReferenceFbxText.Text))
                throw new InvalidOperationException("Select a bone spacing reference FBX.");
            args.Add("--bone-spacing-reference-fbx");
            args.Add(boneSpacingReferenceFbxText.Text.Trim());
            if (!string.IsNullOrWhiteSpace(boneSpacingReferenceActionText.Text))
            {
                args.Add("--bone-spacing-reference-action");
                args.Add(boneSpacingReferenceActionText.Text.Trim());
            }
            if (!string.IsNullOrWhiteSpace(boneSpacingAllowTranslationText.Text))
            {
                args.Add("--bone-spacing-allow-translation");
                args.Add(boneSpacingAllowTranslationText.Text.Trim());
            }
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
        SetControlTreeEnabled(sceneActorRow, enabled);
        UpdateBoneSpacingUi();
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

    private void UpdateBoneSpacingUi()
    {
        var fbxOutput = string.Equals(outputFormatCombo.SelectedItem?.ToString(), "fbx", StringComparison.OrdinalIgnoreCase);
        var canEnable = includeAnimationsCheck.Checked && fbxOutput && !string.IsNullOrWhiteSpace(blenderPathText.Text);
        if (!canEnable && boneSpacingRepairCheck.Checked)
        {
            boneSpacingRepairCheck.Checked = false;
        }
        SetCheckEnabled(boneSpacingRepairCheck, canEnable);
        SetControlTreeEnabled(boneSpacingReferenceRow, canEnable && boneSpacingRepairCheck.Checked);
        SetControlTreeEnabled(boneSpacingOptionsRow, canEnable && boneSpacingRepairCheck.Checked);
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
        if (root is Label label)
        {
            label.Enabled = true;
            label.ForeColor = enabled ? DarkText : MutedText;
        }
        else if (root is ThemedInputFrame)
        {
            root.Enabled = enabled;
        }
        else if (root is Panel or TableLayoutPanel or FlowLayoutPanel)
        {
            root.Enabled = true;
        }
        else
        {
            root.Enabled = enabled;
        }
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
            readinessLabel.Text = L("Ready to export");
            readinessLabel.ForeColor = Success;
            readinessDetailLabel.Text = L("The required fields are complete. Review the command, then run the export.");
        }
        catch (Exception ex)
        {
            commandPreviewText.Text = ex.Message;
            readinessLabel.Text = L("Needs attention");
            readinessLabel.ForeColor = Warning;
            readinessDetailLabel.Text = ex.Message;
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
        var success = false;
        try
        {
            SavePathConfig();
            var args = BuildExportArgs();
            outputPathText.Text = ResolveOutputPath();
            UpdateRunningState(true);
            logText.Clear();
            StartGuiExportLog(outputPathText.Text, args);
            SetProgress(0);
            AppendLog(L("Starting export"));
            await RunExporterProcessAsync(args);
            SetProgress(100);
            AppendLog(L("Export completed."));
            success = true;
        }
        catch (Exception ex)
        {
            AppendLog(L("ERROR: ") + ex.Message);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            CompleteGuiExportLog(success);
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
        if (!string.IsNullOrWhiteSpace(currentGuiLogPath))
        {
            try
            {
                File.AppendAllText(currentGuiLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                currentGuiLogPath = null;
            }
        }
        UpdateLogScrollbars();
    }

    private void StartGuiExportLog(string outputPath, IReadOnlyList<string> args)
    {
        currentGuiLogPath = null;
        var logDirectory = ResolveGuiLogDirectory(outputPath);
        Directory.CreateDirectory(logDirectory);
        var baseName = ResolveGuiLogBaseName(outputPath);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        currentGuiLogPath = MakeUniquePath(Path.Combine(logDirectory, $"{baseName}-GUI-RUN__{stamp}.log"));
        var exe = ResolveCliExecutablePath();
        var lines = new[]
        {
            "REE-Content-Exporter GUI export log",
            $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"Executable: {exe}",
            $"Command: {Quote(exe)} {string.Join(" ", args.Select(Quote))}",
            $"Requested output: {outputPath}",
            "",
        };
        File.WriteAllLines(currentGuiLogPath, lines, Encoding.UTF8);
        AppendLog($"GUI_LOG={currentGuiLogPath}");
    }

    private void CompleteGuiExportLog(bool success)
    {
        if (string.IsNullOrWhiteSpace(currentGuiLogPath)) return;
        var source = currentGuiLogPath;
        var final = source.Replace("-GUI-RUN__", success ? "-GUI-SUCCESS__" : "-GUI-FAIL__");
        final = MakeUniquePath(final);
        try
        {
            File.AppendAllText(source, $"{Environment.NewLine}Finished: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}Status: {(success ? "SUCCESS" : "FAIL")}{Environment.NewLine}", Encoding.UTF8);
            if (!source.Equals(final, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(source, final);
            }
            currentGuiLogPath = final;
            AppendLog($"GUI_LOG_FINAL={final}");
        }
        finally
        {
            currentGuiLogPath = null;
        }
    }

    private static string ResolveGuiLogDirectory(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var extension = Path.GetExtension(outputPath);
        if (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }
        return Path.GetFullPath(outputPath);
    }

    private static string ResolveGuiLogBaseName(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return "gui-export";
        var extension = Path.GetExtension(outputPath);
        if (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            return SanitizeFileName(Path.GetFileNameWithoutExtension(outputPath));
        }
        return "gui-export";
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{stem}_{i}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private void UpdateLogScrollbars()
    {
        if (logText.ClientSize.Height <= 0) return;
        var visibleLines = Math.Max(1, logText.ClientSize.Height / Math.Max(1, logText.Font.Height));
        var needsScroll = logText.Lines.Length > visibleLines;
        var desired = needsScroll ? ScrollBars.Vertical : ScrollBars.None;
        if (logText.ScrollBars != desired) logText.ScrollBars = desired;
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
    private static readonly Color DarkBack = Color.FromArgb(18, 20, 24);
    private static readonly Color DarkPanel = Color.FromArgb(28, 31, 37);
    private static readonly Color DarkInput = Color.FromArgb(13, 15, 18);
    private static readonly Color DarkBorder = Color.FromArgb(70, 72, 78);
    private static readonly Color DarkText = Color.FromArgb(240, 242, 245);
    private static readonly Color MutedText = Color.FromArgb(166, 171, 181);
    private static readonly Color Accent = Color.FromArgb(154, 207, 255);
    private static readonly Color AccentHover = Color.FromArgb(190, 226, 255);
    private static readonly Color ButtonBase = Color.FromArgb(43, 47, 55);
    private static readonly Color ButtonHover = Color.FromArgb(48, 59, 70);
    private static readonly Color ButtonPressed = Color.FromArgb(54, 86, 116);
    private static readonly Color DisabledBack = Color.FromArgb(35, 38, 44);

    private readonly IReadOnlyList<string> entries;
    private readonly string extractRoot;
    private readonly AssetPickerKind kind;
    private readonly bool korean;
    private readonly TextBox searchText = new();
    private readonly ListBox resultList = new();
    private readonly Label selectedPathLabel = new();
    private readonly Label resultCountLabel = new();
    private readonly ToolTip tooltips = new();
    private string lastHoverPath = "";

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
        MinimumSize = new Size(1040, 760);
        Size = new Size(1220, 860);
        BackColor = DarkBack;
        ForeColor = DarkText;
        Font = new Font("Segoe UI", 9F);
        BuildLayout();
        RefreshResults();
    }

    private void BuildLayout()
    {
        tooltips.AutoPopDelay = 20000;
        tooltips.InitialDelay = 250;
        tooltips.ReshowDelay = 80;
        tooltips.ShowAlways = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16), BackColor = DarkBack };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        Controls.Add(root);

        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = DarkBack };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var searchLabel = new Label { Text = L("Search"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DarkText, BackColor = DarkBack };
        searchText.Dock = DockStyle.Fill;
        searchText.Margin = new Padding(0, 4, 0, 6);
        searchText.PlaceholderText = L("Type part of a filename or path");
        searchText.BackColor = DarkInput;
        searchText.ForeColor = DarkText;
        searchText.BorderStyle = BorderStyle.FixedSingle;
        searchText.TextChanged += (_, _) => RefreshResults();
        tooltips.SetToolTip(searchText, L("Type part of a filename or path"));
        searchRow.Controls.Add(searchLabel, 0, 0);
        searchRow.Controls.Add(searchText, 1, 0);

        selectedPathLabel.Dock = DockStyle.Fill;
        selectedPathLabel.AutoEllipsis = true;
        selectedPathLabel.TextAlign = ContentAlignment.MiddleLeft;
        selectedPathLabel.ForeColor = MutedText;
        selectedPathLabel.BackColor = DarkPanel;
        selectedPathLabel.Padding = new Padding(10, 0, 10, 0);
        selectedPathLabel.Margin = new Padding(0, 0, 0, 8);

        resultList.Dock = DockStyle.Fill;
        resultList.BackColor = DarkInput;
        resultList.ForeColor = DarkText;
        resultList.BorderStyle = BorderStyle.FixedSingle;
        resultList.HorizontalScrollbar = true;
        resultList.IntegralHeight = false;
        resultList.DoubleClick += (_, _) => AcceptSelection();
        resultList.SelectedIndexChanged += (_, _) => UpdateSelectedPathPreview();
        resultList.MouseMove += (_, e) => UpdateHoverTooltip(e.Location);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = DarkBack, Padding = new Padding(0, 8, 0, 12) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        resultCountLabel.Dock = DockStyle.Fill;
        resultCountLabel.TextAlign = ContentAlignment.MiddleLeft;
        resultCountLabel.ForeColor = MutedText;
        resultCountLabel.BackColor = DarkBack;
        var choose = new PickerButton { Text = L("Choose"), Width = 112, Height = 42, Anchor = AnchorStyles.Top | AnchorStyles.Right, AccentButton = true, Margin = new Padding(6, 0, 0, 0) };
        choose.Click += (_, _) => AcceptSelection();
        var cancel = new PickerButton { Text = L("Cancel"), Width = 112, Height = 42, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(6, 0, 0, 0) };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        actions.Controls.Add(resultCountLabel, 0, 0);
        actions.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = DarkBack }, 1, 0);
        actions.Controls.Add(cancel, 2, 0);
        actions.Controls.Add(choose, 3, 0);

        root.Controls.Add(searchRow, 0, 0);
        root.Controls.Add(selectedPathLabel, 0, 1);
        root.Controls.Add(resultList, 0, 2);
        root.Controls.Add(actions, 0, 3);
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
        resultCountLabel.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, L("{0} result(s)"), matches.Count);
        UpdateSelectedPathPreview();
    }

    private void UpdateSelectedPathPreview()
    {
        var selected = resultList.SelectedItem?.ToString();
        selectedPathLabel.Text = string.IsNullOrWhiteSpace(selected)
            ? L("Hover or select a result to preview the full path.")
            : selected;
        tooltips.SetToolTip(selectedPathLabel, selectedPathLabel.Text);
    }

    private void UpdateHoverTooltip(Point location)
    {
        var index = resultList.IndexFromPoint(location);
        var path = index >= 0 && index < resultList.Items.Count
            ? resultList.Items[index]?.ToString() ?? ""
            : "";
        if (string.Equals(path, lastHoverPath, StringComparison.Ordinal)) return;
        lastHoverPath = path;
        if (!string.IsNullOrWhiteSpace(path))
        {
            selectedPathLabel.Text = path;
            tooltips.SetToolTip(resultList, path);
            tooltips.SetToolTip(selectedPathLabel, path);
        }
        else
        {
            tooltips.SetToolTip(resultList, L("Hover over a result to preview the full path."));
        }
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
        var stmRel = AddNativesStmPrefix(directRel);
        var candidateRoots = new List<string> { extractRoot };
        if (EndsWithSegments(extractRoot, "natives", "stm"))
        {
            candidateRoots.Add(Path.GetFullPath(Path.Combine(extractRoot, "..", "..")));
        }
        candidateRoots.Add(Path.Combine(extractRoot, "re_chunk_000"));
        foreach (var candidate in candidateRoots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(root => new[] { rel, directRel, stmRel }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(candidateRel => Path.Combine(root, candidateRel))))
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

    private static string AddNativesStmPrefix(string rel)
    {
        var prefix = "natives" + Path.DirectorySeparatorChar + "stm" + Path.DirectorySeparatorChar;
        return rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? rel : prefix + rel;
    }

    private static bool EndsWithSegments(string path, params string[] segments)
    {
        var parts = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < segments.Length) return false;
        for (var i = 0; i < segments.Length; i++)
        {
            if (!parts[parts.Length - segments.Length + i].Equals(segments[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
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
            "Search" => "검색",
            "Type part of a filename or path" => "파일 이름 또는 경로 일부 입력",
            "Hover or select a result to preview the full path." => "결과 위에 마우스를 올리거나 선택하면 전체 경로를 미리 볼 수 있습니다.",
            "Hover over a result to preview the full path." => "결과 위에 마우스를 올리면 전체 경로를 미리 볼 수 있습니다.",
            "{0} result(s)" => "{0}개 결과",
            "Choose" => "선택",
            "Cancel" => "취소",
            _ => text,
        };

    private sealed class PickerButton : Button
    {
        private bool hover;
        private bool pressed;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AccentButton { get; init; }

        public PickerButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            BackColor = ButtonBase;
            ForeColor = DarkText;
            MinimumSize = new Size(0, 34);
            Padding = new Padding(10, 0, 10, 0);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var back = !Enabled ? DisabledBack : pressed ? (AccentButton ? ButtonPressed : Color.FromArgb(50, 45, 40)) : hover ? (AccentButton ? AccentHover : ButtonHover) : AccentButton ? Accent : ButtonBase;
            using var path = RoundedRect(rect, 8);
            using var fill = new SolidBrush(back);
            using var border = new Pen(Enabled ? (AccentButton ? AccentHover : Accent) : DarkBorder, AccentButton ? 1.7f : 1.2f);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : MutedText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
