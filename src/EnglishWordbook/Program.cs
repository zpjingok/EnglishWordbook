using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnglishWordbook;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class AppSettings
{
    [JsonPropertyName("api_provider")]
    public string ApiProvider { get; set; } = "自定义（OpenAI 兼容）";
    [JsonPropertyName("api_base")]
    public string ApiBase { get; set; } = "https://api.deepseek.com/v1";
    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = "";
    // API keys are kept in independent provider slots. ApiKey remains as a
    // compatibility field for configurations written by older versions and
    // always mirrors the currently selected provider's slot at runtime.
    [JsonPropertyName("api_keys")]
    public Dictionary<string, string> ApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("model")]
    public string Model { get; set; } = "deepseek-chat";
    [JsonPropertyName("thinking_mode")]
    public string ThinkingMode { get; set; } = ThinkingModes.Auto;
    [JsonPropertyName("prompt")]
    public string PromptTemplate { get; set; } = PromptTemplates.Default;
    [JsonPropertyName("monitoring")]
    public bool Monitoring { get; set; }
    [JsonPropertyName("auto_translate")]
    public bool AutoTranslate { get; set; } = true;
    [JsonPropertyName("always_on_top")]
    public bool AlwaysOnTop { get; set; } = true;
    [JsonPropertyName("auto_fill_input")]
    public bool AutoFillInput { get; set; }
    // Kept only to migrate existing local configurations from the old,
    // unsafe "paste translated text back into another app" behavior.
    [JsonPropertyName("auto_paste")]
    public bool LegacyAutoPaste { get; set; }
    [JsonPropertyName("dark_mode")]
    public bool DarkMode { get; set; }
    [JsonPropertyName("transparent_mode")]
    public bool TransparentMode { get; set; }
    [JsonPropertyName("transparency_percent")]
    public int TransparencyPercent { get; set; } = 88;
    [JsonPropertyName("word_book")]
    public string WordBook { get; set; } = "";
    [JsonPropertyName("global_hotkey")]
    public string GlobalHotKey { get; set; } = GlobalHotKeys.DefaultText;
}

internal static class ApiProviders
{
    public const string Custom = "custom";
    public const string DeepSeek = "deepseek";
    public const string Aliyun = "aliyun";

    public static string KeyFor(string? provider, string? apiBase)
    {
        if (string.Equals(provider, "DeepSeek", StringComparison.OrdinalIgnoreCase) ||
            (provider is null && apiBase?.Contains("api.deepseek.com", StringComparison.OrdinalIgnoreCase) == true))
            return DeepSeek;
        if (string.Equals(provider, "阿里百炼云（Qwen Flash）", StringComparison.OrdinalIgnoreCase) ||
            (provider is null && apiBase?.Contains("dashscope.aliyuncs.com", StringComparison.OrdinalIgnoreCase) == true))
            return Aliyun;
        return Custom;
    }
}

internal static class ThinkingModes
{
    public const string Auto = "自动";
    public const string Enabled = "开启";
    public const string Disabled = "关闭";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "开启" or "enabled" or "enable" or "on" => Enabled,
        "关闭" or "disabled" or "disable" or "off" => Disabled,
        _ => Auto,
    };
}

internal static class SettingsStore
{
    private static readonly string AppFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EnglishWordbook");
    private static readonly string SettingsPath = Path.Combine(AppFolder, "settings.json");

    public static string DefaultWordBook => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EnglishWordbook", "英语单词簿.md");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return Normalize(new AppSettings());
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
            return Normalize(settings ?? new AppSettings());
        }
        catch (JsonException)
        {
            return Normalize(new AppSettings());
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppFolder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Normalize(settings), new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public static AppSettings Normalize(AppSettings settings)
    {
        settings.ApiBase = string.IsNullOrWhiteSpace(settings.ApiBase) ? "https://api.deepseek.com/v1" : settings.ApiBase.Trim();
        settings.ApiProvider = string.IsNullOrWhiteSpace(settings.ApiProvider) ? "自定义（OpenAI 兼容）" : settings.ApiProvider.Trim();
        settings.Model = string.IsNullOrWhiteSpace(settings.Model) ? "deepseek-chat" : settings.Model.Trim();
        settings.ApiKeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentProviderKey = ApiProviders.KeyFor(settings.ApiProvider, settings.ApiBase);
        // Migrate the legacy single-key field into the currently selected
        // provider slot, without ever logging or displaying the secret.
        if ((!settings.ApiKeys.TryGetValue(currentProviderKey, out var existingApiKey) || string.IsNullOrWhiteSpace(existingApiKey)) &&
            !string.IsNullOrWhiteSpace(settings.ApiKey))
            settings.ApiKeys[currentProviderKey] = settings.ApiKey.Trim();
        foreach (var key in settings.ApiKeys.Keys.ToList())
            settings.ApiKeys[key] = settings.ApiKeys[key]?.Trim() ?? "";
        settings.ApiKey = settings.ApiKeys.TryGetValue(currentProviderKey, out var currentApiKey)
            ? currentApiKey
            : "";
        settings.ThinkingMode = ThinkingModes.Normalize(settings.ThinkingMode);
        settings.PromptTemplate = PromptTemplates.Normalize(settings.PromptTemplate);
        settings.WordBook = string.IsNullOrWhiteSpace(settings.WordBook) ? DefaultWordBook : settings.WordBook.Trim();
        settings.GlobalHotKey = GlobalHotKeys.Normalize(settings.GlobalHotKey);
        settings.AutoFillInput |= settings.LegacyAutoPaste;
        settings.LegacyAutoPaste = false;
        settings.TransparencyPercent = Math.Clamp(settings.TransparencyPercent, 70, 100);
        return settings;
    }
}

internal static class PromptTemplates
{
    // 仅用于把从前未改动过的内置提示词平滑升级为新版；用户自定义提示词不受影响。
    public const string LegacyDefault = """
You are an English teacher for Chinese learners. Explain the English text below in concise Simplified Chinese.

English text:
{source}

For a word, phrase, or expression, use exactly these sections:
【核心意思】
【怎么用】
【例句】
【易错点】 (only when useful)

For a full sentence or paragraph, provide a natural Chinese translation, then explain the 1-3 most useful expressions or grammar points.
Do not greet the user, do not ask for more text, do not invent information, and use Markdown headings and bullets where helpful.
""";

    // 2026-08-19 的前一版默认提示词；用于升级已保存但从未手动编辑的默认值。
    public const string PreviousDefault = """
You are an English teacher for Chinese learners. Explain the English text below in concise Simplified Chinese.

English text:
{source}

For a word, phrase, or expression, root or affix (词根/词缀),, use exactly these sections:
【核心意思】
【常见用法】
【同义/相近的英文单词或短语】
【相关单词】
【例句】
【易错点】 (only when useful)

For a full sentence or paragraph, provide a natural Chinese translation, then explain the 1-3 most useful expressions or grammar points.
Do not greet the user, do not ask for more text, do not invent information, and use Markdown headings and bullets where helpful.
""";

    // The default used immediately before the current template. Existing
    // untouched configurations are upgraded instead of remaining on the old
    // placeholder-style prompt.
    public const string PriorDefault = """
# Role & Objective

You are a senior linguist and simultaneous interpretation coach. Your task is to provide precise, in-depth breakdowns of the user's English input.

# Strict Operational Rules

1. **Direct Dynamic Generation**: Directly generate the analytical content based strictly on the user's input. Never output rule descriptions, prompt instructions, meta-commentary, or empty placeholders.
2. **Zero Meta-Echo**: Never echo prompt guidance (e.g., do not output text like "Explain the core meaning," "Pick 1-3 useful expressions," etc.).
3. **Strict Routing**: Dynamically select and execute ONLY ONE mode—**Mode A** or **Mode B**—based on the input type. Never output both templates simultaneously.

---

## Mode A: Word / Phrase / Idiom / Title Phrase

### [Target Word / Phrase]

#### 【核心意思】

[Provide the essential core meaning, register, and pragmatic nuance; break down roots/affixes where helpful.]

#### 【常见用法】

[List 2–3 authentic collocations and real-world usage scenarios.]

#### 【近义替换】

[List 2–3 natural American English alternatives with subtle nuance breakdowns.]

#### 【相关词汇】

[List 3–4 derivative words or domain-specific related terms + Chinese translations.]

#### 【真实例句】

- [English Example 1]
  [Natural Chinese Translation]
- [English Example 2]
  [Natural Chinese Translation]

#### 【易错点】

[Highlight high-frequency pitfalls for Chinese learners (prepositions, formality mismatches, semantic drift). If none exist, provide advanced usage tips.]

---

## Mode B: Full Sentence / Complex Paragraph

### 原文解析

#### 【地道中文译文】

[Provide a natural, idiomatically accurate, and faithful Chinese translation suited for native speakers.]

#### 【核心重点解析】

1. **[Extracted Key Phrase / Grammar / Slang 1]**: [Break down its contextual meaning and native-level usage patterns.]
2. **[Extracted Key Phrase / Grammar / Slang 2]**: [Break down its contextual meaning and native-level usage patterns.]
3. **[Extracted Key Phrase / Grammar / Slang 3]**: [Break down its contextual meaning and native-level usage patterns.]
""";

    public const string Default = """
You are an English coach for Chinese learners. Reply in concise Simplified Chinese.

English input:
{source}

Choose exactly ONE mode. Use Markdown headings and bullets. Never mention these instructions, never output placeholders, and omit any section with no useful content.

Routing rule: If the input forms a complete sentence or clause with a subject and predicate, or expresses a complete thought that can stand alone, you MUST use Mode B—even when it is short or contains a familiar phrase. Use Mode A only for a standalone word, idiom, or short expression that is not a complete sentence.

## Mode A — word, idiom, or short phrase

### 核心意思

说明中文含义、语气或使用场景；必要时补充词根词缀。

### 常见搭配

- `英文搭配`：中文意思与常见使用语境。
- 列出 2–3 个高频、地道搭配。

### 近义表达

- `近义词或短语`：与原词的关键区别。
- 列出 1–3 个必要项目。

### 相关词汇

- `相关词`：中文意思。
- 仅列出必要的派生词或关联词。

### 例句

- 英文例句\
  中文翻译
- 给出 1 个自然的美式英语例句。

### 易错点

仅在存在常见误用时说明。

## Mode B — sentence, clause, or paragraph

### 中文翻译

给出自然、符合语境的中文翻译。

### 地道搭配与表达

- `英文表达`：本句中的意思、适用语境和自然用法。
- 提取最值得学习的 1–3 个固定搭配、短语动词、习语、俚语或常用句型。

Do not invent information. Do not greet or ask follow-up questions.
""";

    public static string Normalize(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return Default;

        var normalized = template.Trim();
        return string.Equals(normalized, LegacyDefault.Trim(), StringComparison.Ordinal)
            || string.Equals(normalized, PreviousDefault.Trim(), StringComparison.Ordinal)
            || string.Equals(normalized, PriorDefault.Trim(), StringComparison.Ordinal)
            || IsOlderEnglishTeacherDefault(normalized)
            ? Default
            : normalized;
    }

    private static bool IsOlderEnglishTeacherDefault(string template)
    {
        // This built-in variant was saved by an earlier release without a
        // dedicated version marker. Recognize its stable headings so existing
        // installations receive the new routing prompt automatically.
        return template.StartsWith("# Role & Objective", StringComparison.Ordinal)
            && template.Contains("You are an English teacher for Chinese learners. Explain the English text below", StringComparison.Ordinal)
            && template.Contains("Mode A: Word / Phrase / Idiom / Title Phrase", StringComparison.Ordinal)
            && template.Contains("Mode B: Full Sentence / Complex Paragraph", StringComparison.Ordinal);
    }

    public static string Render(string? template, string source)
    {
        var normalized = Normalize(template);
        return normalized.Contains("{source}", StringComparison.Ordinal)
            ? normalized.Replace("{source}", source, StringComparison.Ordinal)
            : $"{normalized}\n\nEnglish text:\n{source}";
    }
}

// RichTextBox 默认会保留剪贴板 RTF 的字体和字号。英文输入区只接收纯文本，
// 以保证手动输入和从其他程序粘贴的文字始终使用同一套界面字体。
internal sealed class PlainTextRichTextBox : RichTextBox
{
    private const int WmPaste = 0x0302;

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        NormalizeCharacterFormatting();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmPaste && Clipboard.ContainsText(TextDataFormat.UnicodeText))
        {
            SelectedText = Clipboard.GetText(TextDataFormat.UnicodeText);
            return;
        }

        base.WndProc(ref message);
    }

    private void NormalizeCharacterFormatting()
    {
        if (TextLength == 0 || IsDisposed)
            return;

        var selectionStart = SelectionStart;
        var selectionLength = SelectionLength;
        SelectAll();
        SelectionFont = Font;
        SelectionColor = ForeColor;
        SelectionStart = Math.Min(selectionStart, TextLength);
        SelectionLength = Math.Min(selectionLength, TextLength - SelectionStart);
    }
}

internal sealed class MainForm : Form
{
    private const int GlobalHotKeyId = 0x4557;
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _clipboardTimer = new() { Interval = 700 };
    private readonly NotifyIcon _trayIcon = new();
    private readonly Icon _appIcon = AppIcon.Create();
    private readonly PlainTextRichTextBox _source = new();
    private readonly RichTextBox _result = new();
    private readonly Label _status = new();
    private readonly Label _providerStatus = new();
    private readonly Button _saveButton = new();
    private readonly CheckBox _monitorBox;
    private readonly CheckBox _autoTranslateBox;
    private readonly CheckBox _topMostBox;
    private readonly CheckBox _autoFillInputBox;
    private readonly CheckBox _darkBox;
    private readonly CheckBox _transparentBox;
    private string _lastClipboard = "";
    // Windows SAPI voice object. Created only when the user explicitly asks
    // the program to read the source text aloud.
    private object? _speechVoice;
    private bool _translating;
    private bool _allowClose;
    private bool _globalHotKeyRegistered;

    public MainForm()
    {
        _settings = SettingsStore.Load();

        Text = "英语单词簿";
        Icon = _appIcon;
        Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
        StartPosition = FormStartPosition.CenterScreen;
        // The header includes the provider selector and several quick toggles.
        // Start wide enough that those controls are visible at the default DPI,
        // while retaining a sensible minimum for smaller displays.
        ClientSize = new Size(900, 650);
        MinimumSize = new Size(760, 500);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        TopMost = _settings.AlwaysOnTop;

        _monitorBox = NewCheckBox("监听剪贴板", _settings.Monitoring);
        _autoTranslateBox = NewCheckBox("自动翻译", _settings.AutoTranslate);
        _topMostBox = NewCheckBox("始终置顶", _settings.AlwaysOnTop);
        _autoFillInputBox = NewCheckBox("自动填入输入框", _settings.AutoFillInput);
        _darkBox = NewCheckBox("暗黑", _settings.DarkMode);
        _transparentBox = NewCheckBox("透明", _settings.TransparentMode);

        BuildInterface();
        HookEvents();
        ApplyTheme();
        ApplyTransparency();
        CreateTrayIcon();
        _clipboardTimer.Start();
    }

    private static CheckBox NewCheckBox(string text, bool isChecked) => new()
    {
        Text = text,
        AutoSize = true,
        Checked = isChecked,
        Margin = new Padding(0, 5, 12, 0),
    };

    private static Button NewButton(string text, EventHandler? handler = null)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 28),
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(7, 1, 7, 1),
        };
        if (handler is not null)
            button.Click += handler;
        return button;
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 3,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var toolbarLeft = NewFlow();
        toolbarLeft.Controls.AddRange([_monitorBox, _autoTranslateBox, _topMostBox]);
        var toolbarRight = NewFlow(FlowDirection.RightToLeft);
        toolbarRight.Controls.Add(NewButton("设置", (_, _) => OpenSettings()));
        toolbarRight.Controls.Add(_transparentBox);
        toolbarRight.Controls.Add(_darkBox);
        _providerStatus.AutoSize = true;
        _providerStatus.Margin = new Padding(0, 5, 12, 0);
        toolbarRight.Controls.Add(_providerStatus);
        toolbar.Controls.Add(toolbarLeft, 0, 0);
        toolbar.Controls.Add(toolbarRight, 1, 0);
        root.Controls.Add(toolbar, 0, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.None,
            IsSplitterFixed = false,
            SplitterWidth = 7,
            Margin = new Padding(0, 0, 0, 8),
        };

        var sourceArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        sourceArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sourceArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sourceArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sourceArea.Controls.Add(NewSectionLabel("英文单词、短语或句子"), 0, 0);
        ConfigureEditor(_source, readOnly: false);
        _source.Font = new Font("Arial", 10f, FontStyle.Regular, GraphicsUnit.Point);
        sourceArea.Controls.Add(_source, 0, 1);
        split.Panel1.Controls.Add(sourceArea);

        var resultArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        resultArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        resultArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actionBar = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 8, 0, 8) };
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var actionLeft = NewFlow();
        actionLeft.Controls.AddRange([
            NewButton("翻译", async (_, _) => await TranslateCurrentAsync()),
            NewButton("翻译剪贴板", async (_, _) => await TranslateClipboardAsync()),
            NewButton("清空", (_, _) => ClearAll()),
            NewButton("发音", (_, _) => SpeakSource()),
        ]);
        var actionRight = NewFlow(FlowDirection.RightToLeft);
        _saveButton.Text = "保存到 Markdown";
        _saveButton.AutoSize = true;
        _saveButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _saveButton.MinimumSize = new Size(0, 28);
        _saveButton.Padding = new Padding(7, 1, 7, 1);
        _saveButton.Enabled = false;
        _saveButton.Click += (_, _) => SaveWordBook();
        actionRight.Controls.Add(_saveButton);
        actionRight.Controls.Add(_autoFillInputBox);
        actionBar.Controls.Add(actionLeft, 0, 0);
        actionBar.Controls.Add(actionRight, 1, 0);
        resultArea.Controls.Add(actionBar, 0, 0);

        resultArea.Controls.Add(NewSectionLabel("翻译与学习讲解"), 0, 1);
        ConfigureEditor(_result, readOnly: true);
        resultArea.Controls.Add(_result, 0, 2);
        split.Panel2.Controls.Add(resultArea);
        root.Controls.Add(split, 0, 1);
        Shown += (_, _) =>
        {
            split.Panel1MinSize = 125;
            split.Panel2MinSize = 170;
            var maximum = split.Height - split.Panel2MinSize - split.SplitterWidth;
            var initial = Math.Min(230, maximum);
            if (initial >= split.Panel1MinSize)
                split.SplitterDistance = initial;
        };

        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 8, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status.Text = "就绪：粘贴英文，或打开剪贴板监听。";
        _status.AutoEllipsis = true;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(NewButton("打开单词簿", (_, _) => OpenWordBook()), 1, 0);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);
        UpdateProviderIndicator();
    }

    private static FlowLayoutPanel NewFlow(FlowDirection direction = FlowDirection.LeftToRight) => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
        FlowDirection = direction,
        WrapContents = false,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
    };

    private static Label NewSectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 3),
    };

    private static void ConfigureEditor(RichTextBox editor, bool readOnly)
    {
        editor.Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
        editor.Dock = DockStyle.Fill;
        editor.ReadOnly = readOnly;
        editor.BorderStyle = BorderStyle.FixedSingle;
        editor.Multiline = true;
        editor.WordWrap = true;
        editor.ScrollBars = RichTextBoxScrollBars.Vertical;
        editor.DetectUrls = false;
        editor.Margin = Padding.Empty;
    }

    private void HookEvents()
    {
        _monitorBox.CheckedChanged += (_, _) =>
        {
            _settings.Monitoring = _monitorBox.Checked;
            SaveSettings();
            SetStatus($"剪贴板监听{(_monitorBox.Checked ? "已开启" : "已关闭")}。");
        };
        _autoTranslateBox.CheckedChanged += (_, _) =>
        {
            _settings.AutoTranslate = _autoTranslateBox.Checked;
            SaveSettings();
            SetStatus($"自动翻译{(_autoTranslateBox.Checked ? "已开启" : "已关闭")}。");
        };
        _topMostBox.CheckedChanged += (_, _) =>
        {
            TopMost = _topMostBox.Checked;
            _settings.AlwaysOnTop = _topMostBox.Checked;
            SaveSettings();
        };
        _autoFillInputBox.CheckedChanged += (_, _) =>
        {
            _settings.AutoFillInput = _autoFillInputBox.Checked;
            SaveSettings();
            SetStatus($"自动填入输入框{(_autoFillInputBox.Checked ? "已开启" : "已关闭")}。");
        };
        _darkBox.CheckedChanged += (_, _) =>
        {
            _settings.DarkMode = _darkBox.Checked;
            SaveSettings();
            ApplyTheme();
        };
        _transparentBox.CheckedChanged += (_, _) =>
        {
            _settings.TransparentMode = _transparentBox.Checked;
            SaveSettings();
            ApplyTransparency();
        };
        _clipboardTimer.Tick += async (_, _) => await PollClipboardAsync();
        _source.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter && !eventArgs.Shift)
            {
                eventArgs.SuppressKeyPress = true;
                eventArgs.Handled = true;
                await TranslateCurrentAsync();
            }
        };
        _source.MouseDown += async (_, _) => await FillSourceFromClipboardAsync();
        FormClosing += OnFormClosing;
    }

    private async Task PollClipboardAsync()
    {
        if (!_monitorBox.Checked || _translating || !TryGetClipboardText(out var text))
            return;
        text = text.Trim();
        if (!IsTranslatable(text) || text == _lastClipboard)
            return;
        _lastClipboard = text;
        _source.Text = text;
        if (_autoTranslateBox.Checked)
            await TranslateAsync(text);
        else
            SetStatus("已粘贴到输入框；点击翻译或按 Enter。");
    }

    private static bool IsTranslatable(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 3000 && value.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private async Task TranslateCurrentAsync() => await TranslateAsync(_source.Text.Trim());

    private async Task TranslateClipboardAsync()
    {
        if (!TryGetClipboardText(out var text))
        {
            MessageBox.Show(this, "剪贴板中没有可读取的文本。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _source.Text = text.Trim();
        await TranslateAsync(_source.Text);
    }

    private async Task FillSourceFromClipboardAsync()
    {
        if (!_autoFillInputBox.Checked || _translating)
            return;
        if (!TryGetClipboardText(out var text))
        {
            SetStatus("剪贴板中没有可读取的文本。");
            return;
        }

        text = text.Trim();
        _source.Clear();
        _source.Text = text;
        _source.SelectionStart = _source.TextLength;
        _source.SelectionLength = 0;

        if (_autoTranslateBox.Checked && IsTranslatable(text))
        {
            SetStatus("已自动填入剪贴板内容，正在翻译……");
            await TranslateAsync(text);
        }
        else
        {
            SetStatus("已自动填入剪贴板内容。");
        }
    }

    private async Task TranslateAsync(string source)
    {
        if (_translating)
        {
            SetStatus("正在翻译，请稍候。");
            return;
        }
        if (!IsTranslatable(source))
        {
            MessageBox.Show(this, "请输入 1—3000 个字符的英文内容。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            OpenSettings();
            MessageBox.Show(this, $"请先填写 {_settings.ApiProvider} 的 API Key，再点击保存。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _translating = true;
        _saveButton.Enabled = false;
        _result.Text = $"正在向 {_settings.ApiProvider} 请求翻译与讲解……";
        SetStatus($"正在使用 {_settings.ApiProvider} 翻译……");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            using var request = new HttpRequestMessage(HttpMethod.Post, GetChatEndpoint());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey.Trim());
            var payload = new Dictionary<string, object?>
            {
                ["model"] = _settings.Model,
                ["messages"] = new[] { new { role = "user", content = PromptTemplates.Render(_settings.PromptTemplate, source) } },
                ["temperature"] = 0.3,
                ["max_tokens"] = 1200,
                ["stream"] = false,
            };
            AddThinkingModeParameter(payload);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"API 返回 HTTP {(int)response.StatusCode}：{responseText[..Math.Min(responseText.Length, 500)]}");
            using var document = JsonDocument.Parse(responseText);
            var translated = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(translated))
                throw new InvalidOperationException("模型返回了空内容。\n");
            _result.Text = translated;
            _saveButton.Enabled = true;
            SetStatus($"{_settings.ApiProvider} 翻译完成，可保存到 Markdown。");
            _source.Focus();
            _source.SelectAll();
        }
        catch (Exception error)
        {
            _result.Clear();
            SetStatus("翻译失败。请检查 API Key、模型名称和网络。");
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _translating = false;
        }
    }

    private void SaveWordBook()
    {
        if (string.IsNullOrWhiteSpace(_source.Text) || string.IsNullOrWhiteSpace(_result.Text))
        {
            MessageBox.Show(this, "请先完成一次翻译再保存。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            var wordBook = _settings.WordBook;
            Directory.CreateDirectory(Path.GetDirectoryName(wordBook) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            var title = string.Join(' ', _source.Lines.Where(line => !string.IsNullOrWhiteSpace(line))).Trim();
            var learningNotes = RemoveModeRoutingHeader(RemoveLeadingSourceEcho(_result.Text, title));
            var entry = $"\n## {title}\n\n- 保存时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n### 翻译与讲解\n\n{learningNotes}\n\n---\n";
            File.AppendAllText(wordBook, entry, Encoding.UTF8);
            SetStatus($"已保存到：{wordBook}");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string RemoveLeadingSourceEcho(string result, string source)
    {
        var lines = result.Replace("\r\n", "\n").Split('\n').ToList();
        var firstContentIndex = lines.FindIndex(line => !string.IsNullOrWhiteSpace(line));
        if (firstContentIndex < 0)
            return result.Trim();

        var firstLine = lines[firstContentIndex].Trim();
        var normalizedFirstLine = firstLine
            .TrimStart('#', '>', '-', '*', ' ')
            .Trim(' ', '*', '_', '`');

        if (!string.Equals(normalizedFirstLine, source.Trim(), StringComparison.OrdinalIgnoreCase))
            return result.Trim();

        lines.RemoveAt(firstContentIndex);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            lines.RemoveAt(0);
        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static string RemoveModeRoutingHeader(string result)
    {
        var lines = result.Replace("\r\n", "\n").Split('\n');
        var filtered = lines.Where(line =>
        {
            var normalized = line.Trim().TrimStart('#', '>', '-', '*', ' ').Trim(' ', '*', '_', '`');
            return !normalized.StartsWith("Mode A — word, idiom, or short phrase", StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith("Mode B — sentence, clause, or paragraph", StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith("Mode A - word, idiom, or short phrase", StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith("Mode B - sentence, clause, or paragraph", StringComparison.OrdinalIgnoreCase);
        });
        return string.Join(Environment.NewLine, filtered).Trim();
    }

    private void SpeakSource()
    {
        var text = _source.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "请输入要朗读的英文内容。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            _speechVoice ??= voiceType is null
                ? throw new InvalidOperationException("Windows 语音服务不可用。")
                : Activator.CreateInstance(voiceType) ?? throw new InvalidOperationException("无法创建 Windows 语音服务。");

            dynamic voice = _speechVoice;
            TrySelectEnglishVoice(voice);
            // 1 = asynchronous; 2 = cancel any previous pronunciation first.
            voice.Speak(text, 3);
            SetStatus("正在朗读英文原文。");
        }
        catch (Exception error)
        {
            SetStatus("无法调用 Windows 英语语音。请检查系统语音设置。");
            MessageBox.Show(this,
                $"无法朗读英文原文。请在 Windows 的“设置 > 时间和语言 > 语音”中安装英语语音包。\n\n{error.Message}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void TrySelectEnglishVoice(dynamic voice)
    {
        // 0409 = English (United States); 0809 = English (United Kingdom).
        // If Windows has neither, keep the user's default SAPI voice.
        foreach (var language in new[] { "Language=409", "Language=809" })
        {
            try
            {
                dynamic voices = voice.GetVoices(language, "");
                if (voices.Count > 0)
                {
                    voice.Voice = voices.Item(0);
                    return;
                }
            }
            catch
            {
                // A particular SAPI implementation may not support filtering;
                // its default voice is still a valid fallback.
            }
        }
    }

    private void OpenWordBook()
    {
        try
        {
            var wordBook = _settings.WordBook;
            Directory.CreateDirectory(Path.GetDirectoryName(wordBook) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            if (!File.Exists(wordBook))
                File.WriteAllText(wordBook, "# 英语单词簿\n", Encoding.UTF8);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(wordBook) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenSettings()
    {
        var previousGlobalHotKey = _settings.GlobalHotKey;
        using var dialog = new SettingsForm(_settings, _darkBox.Checked);
        dialog.TopMost = TopMost;
        dialog.Owner = this;
        dialog.Shown += (_, _) =>
        {
            dialog.TopMost = TopMost;
            dialog.BringToFront();
            dialog.Activate();
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        dialog.ApplyTo(_settings);
        SettingsStore.Normalize(_settings);
        if (!TryChangeGlobalHotKey(previousGlobalHotKey, _settings.GlobalHotKey))
            _settings.GlobalHotKey = previousGlobalHotKey;
        SaveSettings();
        ApplyTransparency();
        UpdateProviderIndicator();
        SetStatus($"设置已保存：API 供应商为 {_settings.ApiProvider}；全局快捷键为 {_settings.GlobalHotKey}。");
    }

    private string GetChatEndpoint()
    {
        var configured = _settings.ApiBase.Trim().TrimEnd('/');
        return configured.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? configured
            : configured + "/chat/completions";
    }

    private void AddThinkingModeParameter(Dictionary<string, object?> payload)
    {
        switch (_settings.ThinkingMode)
        {
            case ThinkingModes.Enabled:
                if (string.Equals(_settings.ApiProvider, "DeepSeek", StringComparison.OrdinalIgnoreCase))
                    payload["thinking"] = new { type = "enabled" };
                else if (string.Equals(_settings.ApiProvider, "阿里百炼云（Qwen Flash）", StringComparison.OrdinalIgnoreCase))
                    payload["enable_thinking"] = true;
                break;
            case ThinkingModes.Disabled:
                if (string.Equals(_settings.ApiProvider, "DeepSeek", StringComparison.OrdinalIgnoreCase))
                    payload["thinking"] = new { type = "disabled" };
                else if (string.Equals(_settings.ApiProvider, "阿里百炼云（Qwen Flash）", StringComparison.OrdinalIgnoreCase))
                    payload["enable_thinking"] = false;
                break;
        }
    }

    private void ClearAll()
    {
        _source.Clear();
        _result.Clear();
        _saveButton.Enabled = false;
        SetStatus("已清空。");
    }

    private void CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => RestoreWindow());
        menu.Items.Add("切换剪贴板监听", null, (_, _) => _monitorBox.Checked = !_monitorBox.Checked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        _trayIcon.Icon = _appIcon;
        _trayIcon.Text = "英语单词簿";
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _trayIcon.Visible = false;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose)
        {
            _clipboardTimer.Stop();
            ReleaseSpeechVoice();
            _trayIcon.Dispose();
            return;
        }
        eventArgs.Cancel = true;
        Hide();
        SetStatus("程序仍在托盘运行。双击托盘图标可恢复窗口。");
    }

    private void ReleaseSpeechVoice()
    {
        if (_speechVoice is not null && Marshal.IsComObject(_speechVoice))
            Marshal.FinalReleaseComObject(_speechVoice);
        _speechVoice = null;
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        RegisterGlobalHotKey(_settings.GlobalHotKey, showSuccess: false);
    }

    private bool TryChangeGlobalHotKey(string previousText, string requestedText)
    {
        if (string.Equals(previousText, requestedText, StringComparison.Ordinal) && _globalHotKeyRegistered)
            return true;

        if (_globalHotKeyRegistered)
        {
            Native.UnregisterHotKey(Handle, GlobalHotKeyId);
            _globalHotKeyRegistered = false;
        }

        if (RegisterGlobalHotKey(requestedText, showSuccess: true))
            return true;

        var restored = RegisterGlobalHotKey(previousText, showSuccess: false);
        SetStatus(restored
            ? $"{requestedText} 注册失败：可能已被其他程序占用，继续使用 {previousText}。"
            : $"{requestedText} 注册失败，原快捷键 {previousText} 也暂时无法注册。请关闭占用快捷键的程序后重试。" );
        return false;
    }

    private bool RegisterGlobalHotKey(string hotKeyText, bool showSuccess)
    {
        if (!GlobalHotKeys.TryParse(hotKeyText, out var binding, out _))
        {
            SetStatus("全局快捷键设置无效，已使用 Ctrl + Q。");
            binding = GlobalHotKeys.Default;
            _settings.GlobalHotKey = binding.Text;
        }

        _globalHotKeyRegistered = Native.RegisterHotKey(
            Handle,
            GlobalHotKeyId,
            binding.Modifiers | Native.ModNoRepeat,
            binding.VirtualKey);
        if (_globalHotKeyRegistered)
        {
            if (showSuccess)
                SetStatus($"全局快捷键已改为 {binding.Text}。" );
            return true;
        }

        SetStatus($"{binding.Text} 注册失败：可能已被其他程序占用。" );
        return false;
    }

    protected override void OnHandleDestroyed(EventArgs eventArgs)
    {
        if (_globalHotKeyRegistered)
        {
            Native.UnregisterHotKey(Handle, GlobalHotKeyId);
            _globalHotKeyRegistered = false;
        }
        base.OnHandleDestroyed(eventArgs);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.WmHotKey && message.WParam.ToInt32() == GlobalHotKeyId)
        {
            ToggleMainWindow();
            return;
        }
        base.WndProc(ref message);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        // RichTextBox consumes Ctrl combinations before its KeyDown event.
        // Handle Ctrl + S here so it still works while the user is typing,
        // without registering a Windows-wide hotkey.
        if (keyData == (Keys.Control | Keys.S))
        {
            SaveWordBook();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private void ToggleMainWindow()
    {
        var shortcut = _settings.GlobalHotKey;
        if (!Visible)
        {
            RestoreWindow();
            SetStatus($"已通过 {shortcut} 调出主界面。");
        }
        else if (WindowState == FormWindowState.Minimized)
        {
            RestoreWindow();
            SetStatus($"已通过 {shortcut} 恢复主界面。");
        }
        else
        {
            Hide();
            SetStatus($"已通过 {shortcut} 隐藏到系统托盘。");
        }
    }

    private void ApplyTheme()
    {
        var dark = _darkBox.Checked;
        var background = dark ? Color.FromArgb(32, 33, 36) : Color.FromArgb(243, 243, 243);
        var surface = dark ? Color.FromArgb(43, 45, 49) : Color.White;
        var text = dark ? Color.FromArgb(232, 234, 237) : Color.FromArgb(32, 33, 36);
        var muted = dark ? Color.FromArgb(189, 193, 198) : Color.FromArgb(95, 99, 104);
        var button = dark ? Color.FromArgb(57, 60, 66) : Color.FromArgb(233, 234, 236);
        var border = dark ? Color.FromArgb(80, 84, 90) : Color.FromArgb(201, 205, 210);
        ApplyThemeTo(this, background, surface, text, muted, button, border);
        Invalidate(true);
    }

    private static void ApplyThemeTo(Control control, Color background, Color surface, Color text, Color muted, Color button, Color border)
    {
        control.ForeColor = text;
        control.BackColor = control is RichTextBox or TextBoxBase or NumericUpDown ? surface : background;
        if (control is Button buttonControl)
        {
            buttonControl.UseVisualStyleBackColor = false;
            buttonControl.FlatStyle = FlatStyle.Flat;
            buttonControl.FlatAppearance.BorderColor = border;
            buttonControl.BackColor = button;
            buttonControl.ForeColor = text;
        }
        else if (control is RichTextBox editor)
        {
            editor.BackColor = surface;
            editor.ForeColor = text;
        }
        else if (control is TextBox textBox)
        {
            textBox.BackColor = surface;
            textBox.ForeColor = text;
        }
        else if (control is NumericUpDown numberBox)
        {
            numberBox.BackColor = surface;
            numberBox.ForeColor = text;
        }
        else if (control is Label label && label.Text.StartsWith("自动粘贴会", StringComparison.Ordinal))
        {
            label.ForeColor = muted;
        }
        foreach (Control child in control.Controls)
            ApplyThemeTo(child, background, surface, text, muted, button, border);
    }

    private void ApplyTransparency() => Opacity = _transparentBox.Checked ? _settings.TransparencyPercent / 100d : 1d;

    private void SaveSettings() => SettingsStore.Save(_settings);

    private void SetStatus(string value) => _status.Text = value;

    private void UpdateProviderIndicator()
    {
        _providerStatus.Text = $"当前：{_settings.ApiProvider}";
    }

    private static bool TryGetClipboardText(out string text)
    {
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            return !string.IsNullOrWhiteSpace(text);
        }
        catch (ExternalException)
        {
            text = "";
            return false;
        }
    }

}

internal sealed class SettingsForm : Form
{
    private const string CustomProvider = "自定义（OpenAI 兼容）";
    private const string DeepSeekProvider = "DeepSeek";
    private const string AliyunProvider = "阿里百炼云（Qwen Flash）";

    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _apiBase = new();
    private readonly TextBox _apiKey = new() { UseSystemPasswordChar = true };
    private readonly TextBox _model = new();
    private readonly TextBox _prompt = new()
    {
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true,
        MinimumSize = new Size(0, 130),
    };
    private readonly TextBox _wordBook = new();
    private readonly TextBox _globalHotKey = new();
    private readonly ComboBox _thinkingMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _opacity = new() { Minimum = 70, Maximum = 100, Increment = 1 };
    private readonly AppSettings _targetSettings;
    private readonly Dictionary<string, string> _apiKeys = new(StringComparer.OrdinalIgnoreCase);
    private string _currentProviderKey = ApiProviders.Custom;

    public SettingsForm(AppSettings settings, bool dark)
    {
        _targetSettings = settings;
        foreach (var pair in settings.ApiKeys ?? new Dictionary<string, string>())
            _apiKeys[pair.Key] = pair.Value ?? "";
        // A caller may provide an AppSettings instance created before the
        // per-provider key migration. Preserve that key in its current slot.
        _currentProviderKey = ApiProviders.KeyFor(settings.ApiProvider, settings.ApiBase);
        if ((!_apiKeys.TryGetValue(_currentProviderKey, out var existingApiKey) || string.IsNullOrWhiteSpace(existingApiKey)) &&
            !string.IsNullOrWhiteSpace(settings.ApiKey))
            _apiKeys[_currentProviderKey] = settings.ApiKey.Trim();
        Text = "设置";
        Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(720, 680);
        MinimumSize = new Size(660, 560);
        AutoScaleMode = AutoScaleMode.Dpi;

        _provider.Items.AddRange([CustomProvider, DeepSeekProvider, AliyunProvider]);
        _apiBase.Text = settings.ApiBase;
        _model.Text = settings.Model;
        _prompt.Text = settings.PromptTemplate;
        _wordBook.Text = settings.WordBook;
        _globalHotKey.Text = settings.GlobalHotKey;
        _thinkingMode.Items.AddRange([ThinkingModes.Auto, ThinkingModes.Enabled, ThinkingModes.Disabled]);
        _thinkingMode.SelectedItem = ThinkingModes.Normalize(settings.ThinkingMode);
        _opacity.Value = settings.TransparencyPercent;
        SelectProvider(settings.ApiProvider, settings.ApiBase, settings.Model);
        _currentProviderKey = CurrentProviderKey();
        _apiKey.Text = GetCurrentApiKey();
        _provider.SelectedIndexChanged += (_, _) => ApplyProviderPreset();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 3, RowCount = 13, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var index = 0; index < 13; index++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddField(layout, 0, "API 供应商", _provider);
        AddField(layout, 1, "API 地址", _apiBase);
        AddField(layout, 2, "API Key", _apiKey);
        AddField(layout, 3, "模型 / 引擎", _model);
        AddField(layout, 4, "思考模式", _thinkingMode);
        AddField(layout, 5, "翻译提示词", _prompt);
        AddField(layout, 6, "Markdown 单词簿", _wordBook, NewButton("选择文件", (_, _) => SelectWordBook()));
        AddField(layout, 7, "全局显示 / 隐藏快捷键", _globalHotKey);
        AddField(layout, 8, "透明度（%）", _opacity);
        var hotKeyHint = new Label { Text = "示例：Ctrl + Q、Ctrl + Shift + W。必须包含 Ctrl、Alt、Shift 或 Win；若被占用，将继续使用原快捷键。", AutoSize = true, Margin = new Padding(0, 5, 0, 4) };
        layout.Controls.Add(hotKeyHint, 0, 9);
        layout.SetColumnSpan(hotKeyHint, 3);
        var promptHint = new Label { Text = "提示词中的 {source} 会自动替换为当前英文文本；若省略，软件会自动补在末尾。", AutoSize = true, Margin = new Padding(0, 5, 0, 4) };
        layout.Controls.Add(promptHint, 0, 10);
        layout.SetColumnSpan(promptHint, 3);
        var warning = new Label { Text = "“自动填入输入框”只会在点击英文输入框时读取剪贴板，不会修改剪贴板，也不会向其他程序粘贴。", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        layout.Controls.Add(warning, 0, 11);
        layout.SetColumnSpan(warning, 3);
        var note = new Label { Text = "API Key 和词库位置仅保存在这台电脑当前用户的本地配置中。", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        layout.Controls.Add(note, 0, 12);
        layout.SetColumnSpan(note, 3);
        var saveButton = NewButton("保存", (_, _) =>
        {
            if (!GlobalHotKeys.TryParse(_globalHotKey.Text, out _, out var error))
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _globalHotKey.Focus();
                _globalHotKey.SelectAll();
                return;
            }
            ApplyTo(_targetSettings);
            DialogResult = DialogResult.OK;
            Close();
        });
        var cancelButton = NewButton("取消", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Right, Padding = new Padding(0, 4, 0, 0) };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        var buttonBar = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(12, 0, 12, 0) };
        buttonBar.Controls.Add(buttons);
        Controls.Add(layout);
        Controls.Add(buttonBar);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        ApplyTheme(dark);
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.ApiProvider = _provider.SelectedItem?.ToString() ?? CustomProvider;
        switch (settings.ApiProvider)
        {
            case DeepSeekProvider:
                settings.ApiBase = "https://api.deepseek.com/v1";
                settings.Model = "deepseek-chat";
                break;
            case AliyunProvider:
                settings.ApiBase = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
                settings.Model = "qwen-flash";
                break;
            default:
                settings.ApiBase = _apiBase.Text.Trim();
                settings.Model = _model.Text.Trim();
                break;
        }
        SaveCurrentApiKey();
        settings.ApiKeys = new Dictionary<string, string>(_apiKeys, StringComparer.OrdinalIgnoreCase);
        var selectedProviderKey = ApiProviders.KeyFor(settings.ApiProvider, settings.ApiBase);
        settings.ApiKey = settings.ApiKeys.TryGetValue(selectedProviderKey, out var selectedApiKey)
            ? selectedApiKey
            : "";
        settings.ThinkingMode = ThinkingModes.Normalize(_thinkingMode.SelectedItem?.ToString());
        settings.PromptTemplate = _prompt.Text;
        settings.WordBook = _wordBook.Text.Trim();
        settings.GlobalHotKey = GlobalHotKeys.Parse(_globalHotKey.Text).Text;
        settings.TransparencyPercent = Decimal.ToInt32(_opacity.Value);
    }

    private void SelectProvider(string provider, string apiBase, string model)
    {
        // Prefer the persisted provider label. The API base is only a
        // fallback for very old configurations that did not store one.
        if (provider == AliyunProvider ||
            (string.IsNullOrWhiteSpace(provider) && apiBase.Contains("dashscope.aliyuncs.com", StringComparison.OrdinalIgnoreCase)))
        {
            _provider.SelectedItem = AliyunProvider;
        }
        else if (provider == DeepSeekProvider ||
                 (string.IsNullOrWhiteSpace(provider) && apiBase.Contains("api.deepseek.com", StringComparison.OrdinalIgnoreCase)))
        {
            _provider.SelectedItem = DeepSeekProvider;
        }
        else
        {
            _provider.SelectedItem = CustomProvider;
        }
    }

    private void ApplyProviderPreset()
    {
        SaveCurrentApiKey();
        switch (_provider.SelectedItem?.ToString())
        {
            case DeepSeekProvider:
                _apiBase.Text = "https://api.deepseek.com/v1";
                _model.Text = "deepseek-chat";
                break;
            case AliyunProvider:
                _apiBase.Text = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
                _model.Text = "qwen-flash";
                break;
        }
        _currentProviderKey = CurrentProviderKey();
        _apiKey.Text = GetCurrentApiKey();
    }

    private string CurrentProviderKey() => ApiProviders.KeyFor(_provider.SelectedItem?.ToString(), _apiBase.Text);

    private string GetCurrentApiKey() => _apiKeys.TryGetValue(_currentProviderKey, out var value) ? value : "";

    private void SaveCurrentApiKey() => _apiKeys[_currentProviderKey] = _apiKey.Text.Trim();

    private static void AddField(TableLayoutPanel layout, int row, string labelText, Control field, Control? trailing = null)
    {
        var label = new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) };
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(0, 3, 0, 3);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(field, 1, row);
        if (trailing is not null)
        {
            trailing.Margin = new Padding(6, 3, 0, 3);
            layout.Controls.Add(trailing, 2, row);
        }
    }

    private static Button NewButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 28), Padding = new Padding(7, 1, 7, 1) };
        button.Click += handler;
        return button;
    }

    private void SelectWordBook()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "选择 Markdown 单词簿位置",
            Filter = "Markdown 文件|*.md|所有文件|*.*",
            DefaultExt = "md",
            AddExtension = true,
            FileName = Path.GetFileName(_wordBook.Text),
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _wordBook.Text = dialog.FileName;
    }

    private void ApplyTheme(bool dark)
    {
        var background = dark ? Color.FromArgb(32, 33, 36) : Color.FromArgb(243, 243, 243);
        var surface = dark ? Color.FromArgb(43, 45, 49) : Color.White;
        var text = dark ? Color.FromArgb(232, 234, 237) : Color.FromArgb(32, 33, 36);
        var button = dark ? Color.FromArgb(57, 60, 66) : Color.FromArgb(233, 234, 236);
        BackColor = background;
        ForeColor = text;
        ApplyThemeTo(this);

        void ApplyThemeTo(Control control)
        {
            control.ForeColor = text;
            control.BackColor = control is TextBox or NumericUpDown or ComboBox ? surface : background;
            if (control is Button buttonControl)
            {
                buttonControl.UseVisualStyleBackColor = false;
                buttonControl.FlatStyle = FlatStyle.Flat;
                buttonControl.BackColor = button;
                buttonControl.ForeColor = text;
            }
            else if (control is ComboBox comboBox)
            {
                comboBox.BackColor = surface;
                comboBox.ForeColor = text;
            }
            foreach (Control child in control.Controls)
                ApplyThemeTo(child);
        }
    }
}

internal static class Native
{
    public const int WmHotKey = 0x0312;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

}

internal readonly record struct GlobalHotKeyBinding(uint Modifiers, uint VirtualKey, string Text);

internal static class GlobalHotKeys
{
    public const string DefaultText = "Ctrl + Q";
    public static readonly GlobalHotKeyBinding Default = new(Native.ModControl, (uint)Keys.Q, DefaultText);

    public static string Normalize(string? text) => TryParse(text, out var binding, out _) ? binding.Text : DefaultText;

    public static GlobalHotKeyBinding Parse(string? text) => TryParse(text, out var binding, out _) ? binding : Default;

    public static bool TryParse(string? text, out GlobalHotKeyBinding binding, out string error)
    {
        binding = Default;
        error = "";
        var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            error = "全局快捷键格式不正确。请使用例如 Ctrl + Q 或 Ctrl + Shift + W 的格式。";
            return false;
        }

        uint modifiers = 0;
        Keys key = Keys.None;
        foreach (var part in parts)
        {
            switch (part.Trim().ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    if (!AddModifier(ref modifiers, Native.ModControl, out error)) return false;
                    continue;
                case "ALT":
                    if (!AddModifier(ref modifiers, Native.ModAlt, out error)) return false;
                    continue;
                case "SHIFT":
                    if (!AddModifier(ref modifiers, Native.ModShift, out error)) return false;
                    continue;
                case "WIN":
                case "WINDOWS":
                    if (!AddModifier(ref modifiers, Native.ModWin, out error)) return false;
                    continue;
            }

            if (key != Keys.None || !TryParseKey(part, out key))
            {
                error = "全局快捷键只能包含一个主按键，例如 Q、1、F2 或 Space。";
                return false;
            }
        }

        if (modifiers == 0)
        {
            error = "全局快捷键必须包含 Ctrl、Alt、Shift 或 Win 中的至少一个修饰键，避免拦截普通输入。";
            return false;
        }
        if (key == Keys.None)
        {
            error = "全局快捷键缺少主按键。";
            return false;
        }

        var labels = new List<string>();
        if ((modifiers & Native.ModControl) != 0) labels.Add("Ctrl");
        if ((modifiers & Native.ModAlt) != 0) labels.Add("Alt");
        if ((modifiers & Native.ModShift) != 0) labels.Add("Shift");
        if ((modifiers & Native.ModWin) != 0) labels.Add("Win");
        labels.Add(FormatKey(key));
        binding = new GlobalHotKeyBinding(modifiers, (uint)key, string.Join(" + ", labels));
        return true;
    }

    private static bool AddModifier(ref uint modifiers, uint modifier, out string error)
    {
        if ((modifiers & modifier) == 0)
        {
            modifiers |= modifier;
            error = "";
            return true;
        }
        error = "全局快捷键中包含重复的修饰键。";
        return false;
    }

    private static bool TryParseKey(string text, out Keys key)
    {
        key = Keys.None;
        var trimmed = text.Trim();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            key = (Keys)char.ToUpperInvariant(trimmed[0]);
            return true;
        }
        if (trimmed.Length == 1 && char.IsDigit(trimmed[0]))
        {
            key = (Keys)((int)Keys.D0 + (trimmed[0] - '0'));
            return true;
        }
        if (!Enum.TryParse(trimmed, true, out key))
            return false;
        key &= Keys.KeyCode;
        return key is not (Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin);
    }

    private static string FormatKey(Keys key) => key is >= Keys.D0 and <= Keys.D9
        ? ((int)key - (int)Keys.D0).ToString()
        : key.ToString();
}

internal static class AppIcon
{
    // Uses the icon embedded in the EXE, so the window, tray icon, shortcut,
    // main program file, and installer all share one source icon asset.
    public static Icon Create()
    {
        using var embeddedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        return embeddedIcon is null ? (Icon)SystemIcons.Application.Clone() : (Icon)embeddedIcon.Clone();
    }
}
