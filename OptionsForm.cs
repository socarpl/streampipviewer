namespace CctvPip.App;

internal sealed class OptionsForm : Form
{
    private readonly AppConfig config;
    private readonly Action swapStreams;
    private readonly Action<AppSettings> previewSettings;
    private readonly Action<AppSettings> saveSettings;
    private readonly Action cancelSettings;
    private readonly Action openConfig;
    private readonly TextBox cctv1UrlTextBox = new();
    private readonly TextBox cctv2UrlTextBox = new();
    private readonly TrackBar pipScaleSlider = new();
    private readonly NumericUpDown pipXInput = new();
    private readonly NumericUpDown pipYInput = new();
    private readonly NumericUpDown borderSizeInput = new();
    private readonly Button borderColorButton = new();
    private readonly Label configPathLabel = new();
    private AppSettings draftSettings;
    private bool updatingControls;
    private bool closeHandled;

    /// <summary>
    /// Creates the non-modal settings dialog and wires callback actions used by the main form.
    /// </summary>
    /// <param name="initialSettings">The settings snapshot used to populate the initial dialog values.</param>
    /// <param name="config">The application configuration, used here to display the active config path.</param>
    /// <param name="swapStreams">The action that swaps MainWindow and PIP roles from the dialog.</param>
    /// <param name="previewSettings">The action that previews draft layout settings in the main window.</param>
    /// <param name="saveSettings">The action that persists confirmed settings.</param>
    /// <param name="cancelSettings">The action that restores config-backed settings when changes are cancelled.</param>
    /// <param name="openConfig">The action that opens the active configuration file.</param>
    public OptionsForm(
        AppSettings initialSettings,
        AppConfig config,
        Action swapStreams,
        Action<AppSettings> previewSettings,
        Action<AppSettings> saveSettings,
        Action cancelSettings,
        Action openConfig)
    {
        draftSettings = initialSettings;
        this.config = config;
        this.swapStreams = swapStreams;
        this.previewSettings = previewSettings;
        this.saveSettings = saveSettings;
        this.cancelSettings = cancelSettings;
        this.openConfig = openConfig;

        Text = "Stream PIP Viewer Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 318);
        BackColor = Color.FromArgb(30, 30, 30);

        BuildControls();
        RefreshFromSettings(draftSettings);
    }

    /// <summary>
    /// Updates the maximum allowed PIP X and Y coordinates based on the current main-stage size.
    /// </summary>
    /// <param name="maxX">The maximum allowed PIP X coordinate.</param>
    /// <param name="maxY">The maximum allowed PIP Y coordinate.</param>
    public void SetCoordinateLimits(int maxX, int maxY)
    {
        updatingControls = true;
        pipXInput.Maximum = Math.Max(0, maxX);
        pipYInput.Maximum = Math.Max(0, maxY);
        pipXInput.Value = Math.Clamp(draftSettings.PipX, (int)pipXInput.Minimum, (int)pipXInput.Maximum);
        pipYInput.Value = Math.Clamp(draftSettings.PipY, (int)pipYInput.Minimum, (int)pipYInput.Maximum);
        updatingControls = false;
    }

    /// <summary>
    /// Replaces the dialog draft settings and updates all controls without firing preview updates.
    /// </summary>
    /// <param name="settings">The settings snapshot that should be displayed by the dialog controls.</param>
    public void RefreshFromSettings(AppSettings settings)
    {
        draftSettings = settings;
        updatingControls = true;
        cctv1UrlTextBox.Text = draftSettings.Cctv1Url;
        cctv2UrlTextBox.Text = draftSettings.Cctv2Url;
        pipScaleSlider.Value = Math.Clamp((int)Math.Round(draftSettings.PipScale * 100), pipScaleSlider.Minimum, pipScaleSlider.Maximum);
        pipXInput.Value = Math.Clamp(draftSettings.PipX, (int)pipXInput.Minimum, (int)pipXInput.Maximum);
        pipYInput.Value = Math.Clamp(draftSettings.PipY, (int)pipYInput.Minimum, (int)pipYInput.Maximum);
        borderSizeInput.Value = Math.Clamp(draftSettings.PipBorderSize, (int)borderSizeInput.Minimum, (int)borderSizeInput.Maximum);
        borderColorButton.BackColor = draftSettings.PipBorderColor;
        borderColorButton.ForeColor = GetReadableTextColor(draftSettings.PipBorderColor);
        updatingControls = false;
    }

    /// <summary>
    /// Treats closing the dialog without an explicit save as a cancel operation.
    /// </summary>
    /// <param name="e">The WinForms close event arguments.</param>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!closeHandled)
        {
            closeHandled = true;
            cancelSettings();
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// Creates and arranges all settings controls, labels, buttons, and event handlers.
    /// </summary>
    private void BuildControls()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(14),
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sourceGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sourceGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        sourceGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        ConfigureSourceTextBox(cctv1UrlTextBox);
        ConfigureSourceTextBox(cctv2UrlTextBox);
        cctv1UrlTextBox.TextChanged += (_, _) => UpdateSourceDraft();
        cctv2UrlTextBox.TextChanged += (_, _) => UpdateSourceDraft();
        sourceGrid.Controls.Add(CreateCaption("CCTV1"), 0, 0);
        sourceGrid.Controls.Add(cctv1UrlTextBox, 1, 0);
        sourceGrid.Controls.Add(CreateCaption("CCTV2"), 0, 1);
        sourceGrid.Controls.Add(cctv2UrlTextBox, 1, 1);

        var pipRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        pipScaleSlider.Minimum = 10;
        pipScaleSlider.Maximum = 60;
        pipScaleSlider.TickFrequency = 5;
        pipScaleSlider.Width = 190;
        pipScaleSlider.Scroll += (_, _) =>
        {
            if (updatingControls)
            {
                return;
            }

            draftSettings = draftSettings with { PipScale = pipScaleSlider.Value / 100.0 };
            previewSettings(draftSettings);
        };

        ConfigureNumberInput(pipXInput);
        ConfigureNumberInput(pipYInput);
        ConfigureBorderSizeInput(borderSizeInput);
        pipXInput.ValueChanged += (_, _) => UpdatePipCoordinate();
        pipYInput.ValueChanged += (_, _) => UpdatePipCoordinate();
        borderSizeInput.ValueChanged += (_, _) => UpdateBorderSize();

        pipRow.Controls.Add(CreatePipCaption("PIP Size"));
        pipRow.Controls.Add(pipScaleSlider);
        pipRow.Controls.Add(CreatePipCaption("PIP X"));
        pipRow.Controls.Add(pipXInput);
        pipRow.Controls.Add(CreatePipCaption("PIP Y"));
        pipRow.Controls.Add(pipYInput);
        pipRow.Controls.Add(CreatePipCaption("Border"));
        pipRow.Controls.Add(borderSizeInput);

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        var swapButton = CreateButton("Swap Streams");
        swapButton.Click += (_, _) => swapStreams();

        var openConfigButton = CreateButton("Open Config");
        openConfigButton.Click += (_, _) => openConfig();

        borderColorButton.Text = "Border Color";
        borderColorButton.AutoSize = true;
        borderColorButton.Height = 32;
        borderColorButton.Margin = new Padding(0, 4, 8, 4);
        borderColorButton.Click += (_, _) => PickBorderColor();

        actionRow.Controls.Add(swapButton);
        actionRow.Controls.Add(borderColorButton);
        actionRow.Controls.Add(openConfigButton);

        var saveRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        var saveButton = CreateButton("Save");
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = CreateButton("Cancel");
        cancelButton.Click += (_, _) => CancelAndClose();

        saveRow.Controls.Add(saveButton);
        saveRow.Controls.Add(cancelButton);

        configPathLabel.Dock = DockStyle.Fill;
        configPathLabel.AutoEllipsis = true;
        configPathLabel.ForeColor = Color.Silver;
        configPathLabel.TextAlign = ContentAlignment.MiddleLeft;
        configPathLabel.Text = config.Path;

        root.Controls.Add(sourceGrid, 0, 0);
        root.Controls.Add(pipRow, 0, 1);
        root.Controls.Add(actionRow, 0, 2);
        root.Controls.Add(saveRow, 0, 3);
        root.Controls.Add(configPathLabel, 0, 4);
        Controls.Add(root);
    }

    /// <summary>
    /// Applies standard layout settings to a stream URL text box.
    /// </summary>
    /// <param name="textBox">The text box to configure.</param>
    private static void ConfigureSourceTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(4, 4, 0, 4);
    }

    /// <summary>
    /// Creates a standard dialog button with consistent sizing and margins.
    /// </summary>
    /// <param name="text">The button text to display.</param>
    /// <returns>A configured button instance.</returns>
    private static Button CreateButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            Margin = new Padding(0, 4, 8, 4)
        };
    }

    /// <summary>
    /// Creates a left-aligned caption label for source URL rows.
    /// </summary>
    /// <param name="text">The caption text to display.</param>
    /// <returns>A configured label instance.</returns>
    private static Label CreateCaption(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 4, 4)
        };
    }

    /// <summary>
    /// Creates a compact caption label for controls in the PIP settings row.
    /// </summary>
    /// <param name="text">The caption text to display.</param>
    /// <returns>A configured label instance.</returns>
    private static Label CreatePipCaption(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(10, 17, 4, 0)
        };
    }

    /// <summary>
    /// Applies standard range, alignment, and spacing settings to a numeric PIP coordinate input.
    /// </summary>
    /// <param name="input">The numeric input control to configure.</param>
    private static void ConfigureNumberInput(NumericUpDown input)
    {
        input.Minimum = 0;
        input.Maximum = 10000;
        input.Width = 78;
        input.Increment = 10;
        input.TextAlign = HorizontalAlignment.Right;
        input.Margin = new Padding(0, 12, 0, 0);
    }

    /// <summary>
    /// Applies range, alignment, and spacing settings to the PIP border-size input.
    /// </summary>
    /// <param name="input">The numeric input control to configure for border size in pixels.</param>
    private static void ConfigureBorderSizeInput(NumericUpDown input)
    {
        input.Minimum = 0;
        input.Maximum = 100;
        input.Width = 64;
        input.Increment = 1;
        input.TextAlign = HorizontalAlignment.Right;
        input.Margin = new Padding(0, 12, 0, 0);
    }

    /// <summary>
    /// Copies PIP coordinate input values into the draft settings and previews the layout.
    /// </summary>
    private void UpdatePipCoordinate()
    {
        if (updatingControls)
        {
            return;
        }

        draftSettings = draftSettings with
        {
            PipX = (int)pipXInput.Value,
            PipY = (int)pipYInput.Value
        };
        previewSettings(draftSettings);
    }

    /// <summary>
    /// Copies the PIP border-size input value into the draft settings and previews the layout.
    /// </summary>
    private void UpdateBorderSize()
    {
        if (updatingControls)
        {
            return;
        }

        draftSettings = draftSettings with { PipBorderSize = (int)borderSizeInput.Value };
        previewSettings(draftSettings);
    }

    /// <summary>
    /// Copies stream URL text box values into the draft settings and previews the current draft.
    /// </summary>
    private void UpdateSourceDraft()
    {
        if (updatingControls)
        {
            return;
        }

        draftSettings = draftSettings with
        {
            Cctv1Url = cctv1UrlTextBox.Text.Trim(),
            Cctv2Url = cctv2UrlTextBox.Text.Trim()
        };
        previewSettings(draftSettings);
    }

    /// <summary>
    /// Opens a color picker, stores the selected PIP border color, refreshes controls, and previews the change.
    /// </summary>
    private void PickBorderColor()
    {
        using var dialog = new ColorDialog
        {
            Color = draftSettings.PipBorderColor,
            FullOpen = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        draftSettings = draftSettings with { PipBorderColor = dialog.Color };
        RefreshFromSettings(draftSettings);
        previewSettings(draftSettings);
    }

    /// <summary>
    /// Validates source URLs, passes confirmed settings to the main form, and closes the dialog.
    /// </summary>
    private void SaveAndClose()
    {
        draftSettings = draftSettings with
        {
            Cctv1Url = cctv1UrlTextBox.Text.Trim(),
            Cctv2Url = cctv2UrlTextBox.Text.Trim()
        };

        if (string.IsNullOrWhiteSpace(draftSettings.Cctv1Url) || string.IsNullOrWhiteSpace(draftSettings.Cctv2Url))
        {
            MessageBox.Show(this, "Both CCTV source URLs must be filled in.", "Invalid Source", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        closeHandled = true;
        saveSettings(draftSettings);
        Close();
    }

    /// <summary>
    /// Cancels draft settings, restores the main form's config-backed settings, and closes the dialog.
    /// </summary>
    private void CancelAndClose()
    {
        closeHandled = true;
        cancelSettings();
        Close();
    }

    /// <summary>
    /// Chooses black or white foreground text for readable contrast against a background color.
    /// </summary>
    /// <param name="background">The background color to evaluate.</param>
    /// <returns>Black text for bright backgrounds, otherwise white text.</returns>
    private static Color GetReadableTextColor(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.55 ? Color.Black : Color.White;
    }
}
