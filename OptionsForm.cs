using System.Diagnostics;
using System.Reflection;

namespace CctvPip.App;

internal sealed class OptionsForm : Form
{
    private readonly AppConfig config;
    private readonly Action<AppSettings> previewSettings;
    private readonly Action<AppSettings> saveSettings;
    private readonly Action cancelSettings;
    private readonly TextBox cctv1UrlTextBox = new();
    private readonly TextBox cctv2UrlTextBox = new();
    private readonly TrackBar pipScaleSlider = new();
    private readonly NumericUpDown pipScaleInput = new();
    private readonly TrackBar pipLeftSlider = new();
    private readonly NumericUpDown pipLeftInput = new();
    private readonly TrackBar pipTopSlider = new();
    private readonly NumericUpDown pipTopInput = new();
    private readonly TextBox borderColorTextBox = new();
    private readonly Button borderColorButton = new();
    private readonly ToolStrip toolbar = new();
    private readonly ToolStripStatusLabel configPathLabel = new();
    private readonly List<Image> toolbarImages = new();
    private AppSettings draftSettings;
    private string lastValidHex = "FFFFFF";
    private bool updatingControls;
    private bool closeHandled;

    public OptionsForm(
        AppSettings initialSettings,
        AppConfig config,
        Action<AppSettings> previewSettings,
        Action<AppSettings> saveSettings,
        Action cancelSettings)
    {
        this.config = config;
        this.previewSettings = previewSettings;
        this.saveSettings = saveSettings;
        this.cancelSettings = cancelSettings;
        draftSettings = initialSettings;

        Text = "PIP Settings";
        Icon = AppIcon.Value;
        Font = SystemFonts.MessageBoxFont;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 410);
        MinimumSize = new Size(680, 420);
        ShowInTaskbar = false;
        BuildControls();
        RefreshFromSettings(initialSettings);
    }

    public void RefreshFromSettings(AppSettings settings)
    {
        updatingControls = true;
        try
        {
            draftSettings = settings;
            cctv1UrlTextBox.Text = settings.Cctv1Url;
            cctv2UrlTextBox.Text = settings.Cctv2Url;
            pipScaleInput.Value = PercentValue(settings.PipScale * 100);
            pipLeftInput.Value = PercentValue(settings.PipLeftPercent);
            pipTopInput.Value = PercentValue(settings.PipTopPercent);
            pipScaleSlider.Value = SliderValue(pipScaleInput);
            pipLeftSlider.Value = SliderValue(pipLeftInput);
            pipTopSlider.Value = SliderValue(pipTopInput);
            lastValidHex = SettingsFileOperations.HexColor(settings.PipBorderColor);
            borderColorTextBox.Text = lastValidHex;
            borderColorButton.BackColor = settings.PipBorderColor;
            draftSettings = ReadControls();
        }
        finally { updatingControls = false; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!closeHandled)
        {
            cancelSettings();
            closeHandled = true;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var image in toolbarImages) image.Dispose();
            toolbarImages.Clear();
        }
        base.Dispose(disposing);
    }

    private void BuildControls()
    {
        SuspendLayout();
        toolbar.GripStyle = ToolStripGripStyle.Visible;
        toolbar.RenderMode = ToolStripRenderMode.System;
        toolbar.ImageScalingSize = new Size(16, 16);
        toolbar.Items.Add(ToolbarButton("icon_open16.png", "Open containing folder", OpenContainingFolder));
        toolbar.Items.Add(ToolbarButton("icon_reload16.png", "Reload from disk", ReloadFromDisk));
        toolbar.Items.Add(ToolbarButton("icon_export16.png", "Export settings to file", ExportSettings));
        toolbar.Items.Add(ToolbarButton("icon_import16.png", "Import settings from file", ImportSettings));
        toolbar.Dock = DockStyle.Top;

        var sources = new GroupBox { Text = "Video Sources", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), Margin = Padding.Empty };
        var sourceGrid = Grid(2, 2);
        sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sourceGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        sourceGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        foreach (var textBox in new[] { cctv1UrlTextBox, cctv2UrlTextBox })
        {
            textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            textBox.Margin = new Padding(0, 3, 4, 3);
            textBox.TextChanged += (_, _) => UpdateDraft();
        }
        sourceGrid.Controls.Add(Caption("Source 1"), 0, 0);
        sourceGrid.Controls.Add(cctv1UrlTextBox, 1, 0);
        sourceGrid.Controls.Add(Caption("Source 2"), 0, 1);
        sourceGrid.Controls.Add(cctv2UrlTextBox, 1, 1);
        sources.Controls.Add(sourceGrid);

        ConfigurePercentage(pipScaleInput, pipScaleSlider);
        ConfigurePercentage(pipLeftInput, pipLeftSlider);
        ConfigurePercentage(pipTopInput, pipTopSlider);
        var sizeGroup = new GroupBox { Text = "PIP Size && Border", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(12, 8, 12, 8) };
        var sizeContents = Grid(1, 2);
        sizeContents.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sizeContents.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        sizeContents.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        sizeContents.Controls.Add(PercentageRow("Size", pipScaleSlider, pipScaleInput), 0, 0);

        var colorRow = Grid(3, 1);
        colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        colorRow.Controls.Add(Caption("Border color"), 0, 0);
        borderColorTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        borderColorTextBox.TextAlign = HorizontalAlignment.Center;
        borderColorTextBox.MaxLength = 7;
        borderColorTextBox.Margin = new Padding(0, 3, 6, 3);
        borderColorTextBox.TextChanged += (_, _) => UpdateBorderColor();
        borderColorTextBox.Leave += (_, _) => CommitColorText();
        colorRow.Controls.Add(borderColorTextBox, 1, 0);
        borderColorButton.Size = new Size(28, 23);
        borderColorButton.Anchor = AnchorStyles.Left;
        borderColorButton.Margin = new Padding(2, 3, 0, 3);
        borderColorButton.UseVisualStyleBackColor = false;
        borderColorButton.AccessibleName = "Choose border color";
        borderColorButton.Click += (_, _) => PickBorderColor();
        colorRow.Controls.Add(borderColorButton, 2, 0);
        sizeContents.Controls.Add(colorRow, 0, 1);
        sizeGroup.Controls.Add(sizeContents);

        var positionGroup = new GroupBox { Text = "PIP Position", Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0), Padding = new Padding(12, 8, 12, 8) };
        var positionContents = Grid(1, 3);
        positionContents.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        positionContents.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        positionContents.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        positionContents.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        positionContents.Controls.Add(Caption("Margin size from..."), 0, 0);
        positionContents.Controls.Add(PercentageRow("Left", pipLeftSlider, pipLeftInput), 0, 1);
        positionContents.Controls.Add(PercentageRow("Top", pipTopSlider, pipTopInput), 0, 2);
        positionGroup.Controls.Add(positionContents);

        var groups = Grid(2, 1);
        groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        groups.Controls.Add(sizeGroup, 0, 0);
        groups.Controls.Add(positionGroup, 1, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var saveButton = new Button { Text = "Save", Size = new Size(75, 25), Margin = new Padding(4, 4, 0, 0) };
        var cancelButton = new Button { Text = "Cancel", Size = new Size(75, 25), Margin = new Padding(4, 4, 4, 0), CausesValidation = false };
        saveButton.Click += (_, _) => SaveAndClose();
        cancelButton.Click += (_, _) => CancelAndClose();
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var body = Grid(1, 5);
        body.Padding = new Padding(20);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        body.Controls.Add(sources, 0, 0);
        body.Controls.Add(groups, 0, 2);
        body.Controls.Add(buttons, 0, 4);

        var statusBar = new StatusStrip { SizingGrip = true, Dock = DockStyle.Bottom };
        configPathLabel.Text = config.Path;
        configPathLabel.ToolTipText = config.Path;
        configPathLabel.IsLink = true;
        configPathLabel.Spring = true;
        configPathLabel.TextAlign = ContentAlignment.MiddleLeft;
        configPathLabel.Click += (_, _) => RunFileAction(config.OpenInNotepad);
        statusBar.Items.Add(configPathLabel);
        Controls.Add(body);
        Controls.Add(toolbar);
        Controls.Add(statusBar);
        ResumeLayout(true);
    }

    private ToolStripButton ToolbarButton(string resourceName, string tooltip, Action action)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CctvPip.App.assets." + resourceName)
            ?? throw new InvalidOperationException("Missing toolbar icon: " + resourceName);
        using var original = Image.FromStream(stream);
        var image = new Bitmap(original);
        toolbarImages.Add(image);
        var button = new ToolStripButton
        {
            Image = image, DisplayStyle = ToolStripItemDisplayStyle.Image,
            Text = tooltip, ToolTipText = tooltip, AccessibleName = tooltip,
            ImageScaling = ToolStripItemImageScaling.None
        };
        button.Click += (_, _) => RunFileAction(action);
        return button;
    }

    private static TableLayoutPanel Grid(int columns, int rows) =>
        new() { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty };

    private static Label Caption(string text) =>
        new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, AutoSize = true };

    private static TableLayoutPanel PercentageRow(string caption, TrackBar slider, NumericUpDown input)
    {
        var row = Grid(4, 1);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
        row.Controls.Add(Caption(caption), 0, 0);
        row.Controls.Add(slider, 1, 0);
        row.Controls.Add(input, 2, 0);
        row.Controls.Add(Caption("%"), 3, 0);
        return row;
    }

    private void ConfigurePercentage(NumericUpDown input, TrackBar slider)
    {
        input.Minimum = slider.Minimum = 0;
        input.Maximum = slider.Maximum = 100;
        input.DecimalPlaces = 2;
        input.Increment = 0.1M;
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        input.TextAlign = HorizontalAlignment.Right;
        input.Margin = new Padding(3, 3, 6, 3);
        slider.Dock = DockStyle.Fill;
        slider.AutoSize = false;
        slider.TickFrequency = 10;
        slider.TickStyle = TickStyle.Both;
        slider.Margin = new Padding(0, 3, 3, 3);
        input.ValueChanged += (_, _) =>
        {
            if (updatingControls) return;
            updatingControls = true;
            try { slider.Value = SliderValue(input); }
            finally { updatingControls = false; }
            UpdateDraft();
        };
        slider.ValueChanged += (_, _) =>
        {
            if (updatingControls) return;
            updatingControls = true;
            try { input.Value = slider.Value; }
            finally { updatingControls = false; }
            UpdateDraft();
        };
    }

    private static decimal PercentValue(double value) =>
        Math.Round((decimal)(double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0), 2);

    private static int SliderValue(NumericUpDown input) => (int)Math.Round(input.Value, MidpointRounding.AwayFromZero);

    private AppSettings ReadControls() => draftSettings with
    {
        Cctv1Url = cctv1UrlTextBox.Text.Trim(),
        Cctv2Url = cctv2UrlTextBox.Text.Trim(),
        PipScale = (double)pipScaleInput.Value / 100,
        PipLeftPercent = (double)pipLeftInput.Value,
        PipTopPercent = (double)pipTopInput.Value
    };

    private void UpdateDraft()
    {
        if (updatingControls) return;
        draftSettings = ReadControls();
        previewSettings(draftSettings);
    }

    private void UpdateBorderColor()
    {
        if (updatingControls || !SettingsFileOperations.TryParseHexColor(borderColorTextBox.Text, out var color)) return;
        lastValidHex = SettingsFileOperations.HexColor(color);
        borderColorButton.BackColor = color;
        draftSettings = draftSettings with { PipBorderColor = color };
        UpdateDraft();
    }

    private void CommitColorText()
    {
        if (SettingsFileOperations.TryParseHexColor(borderColorTextBox.Text, out _)) UpdateBorderColor();
        borderColorTextBox.Text = lastValidHex;
    }

    private void PickBorderColor()
    {
        CommitColorText();
        using var dialog = new ColorDialog { Color = draftSettings.PipBorderColor, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            borderColorTextBox.Text = SettingsFileOperations.HexColor(dialog.Color);
        }
    }

    private void ReloadFromDisk()
    {
        var settings = SettingsFileOperations.ReadVisibleSettings(config.Path, AppSettings.FromConfig(config));
        RefreshFromSettings(settings);
        previewSettings(draftSettings);
    }

    private void ImportFromFile(string path)
    {
        var settings = SettingsFileOperations.ReadVisibleSettings(path, ReadControls());
        RefreshFromSettings(settings);
        previewSettings(draftSettings);
    }

    private void ExportToFile(string path)
    {
        CommitColorText();
        draftSettings = ReadControls();
        SettingsFileOperations.Export(config.Path, path, draftSettings);
    }

    private void ImportSettings()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import settings from file", Filter = "Properties files (*.properties)|*.properties",
            DefaultExt = "properties", CheckFileExists = true, Multiselect = false,
            InitialDirectory = Path.GetDirectoryName(config.Path)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) ImportFromFile(dialog.FileName);
    }

    private void ExportSettings()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export settings to file", Filter = "Properties files (*.properties)|*.properties",
            DefaultExt = "properties", AddExtension = true, OverwritePrompt = true,
            FileName = Path.GetFileNameWithoutExtension(config.Path) + "-backup.properties",
            InitialDirectory = Path.GetDirectoryName(config.Path)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) ExportToFile(dialog.FileName);
    }

    private void OpenContainingFolder()
    {
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add(Path.GetDirectoryName(config.Path)!);
        using var _ = Process.Start(startInfo);
    }

    private void SaveAndClose()
    {
        CommitColorText();
        draftSettings = ReadControls();
        if (string.IsNullOrWhiteSpace(draftSettings.Cctv1Url) || string.IsNullOrWhiteSpace(draftSettings.Cctv2Url))
        {
            MessageBox.Show(this, "Both source URLs must be filled in.", "Invalid Source", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try { saveSettings(draftSettings); }
        catch (Exception ex) { ShowError(ex); return; }
        closeHandled = true;
        Close();
    }

    private void CancelAndClose()
    {
        closeHandled = true;
        cancelSettings();
        Close();
    }

    private void RunFileAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex) =>
        MessageBox.Show(this, ex.Message, "PIP Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
