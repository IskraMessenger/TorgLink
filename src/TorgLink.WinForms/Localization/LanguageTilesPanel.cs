using TorgLink.Localization;

namespace TorgLink.WinForms.Localization;

/// <summary>3×2 flag tiles for language selection / settings.</summary>
internal sealed class LanguageTilesPanel : TableLayoutPanel
{
    private AppLanguage _selected;
    private readonly Action<AppLanguage>? _onSelected;
    private readonly Dictionary<AppLanguage, Panel> _tiles = new();

    public LanguageTilesPanel(AppLanguage selected, Action<AppLanguage>? onSelected = null)
    {
        _selected = selected;
        _onSelected = onSelected;
        ColumnCount = 3;
        RowCount = 2;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(4);

        for (var c = 0; c < 3; c++)
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (var r = 0; r < 2; r++)
            RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var langs = LanguageService.AllLanguages;
        for (var i = 0; i < langs.Length; i++)
        {
            var lang = langs[i];
            var tile = CreateTile(lang);
            _tiles[lang] = tile;
            Controls.Add(tile, i % 3, i / 3);
        }

        HighlightSelected();
    }

    public void SetSelected(AppLanguage language)
    {
        _selected = language;
        HighlightSelected();
    }

    private Panel CreateTile(AppLanguage language)
    {
        var panel = new Panel
        {
            Width = 120,
            Height = 88,
            Margin = new Padding(6),
            Cursor = Cursors.Hand,
            Tag = language
        };

        var flag = new PictureBox
        {
            Width = 54,
            Height = 36,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(33, 10),
            Image = FlagImages.Load(language)
        };
        var label = new Label
        {
            Text = LanguageService.NativeName(language),
            AutoSize = false,
            Width = 110,
            Height = 28,
            Location = new Point(5, 52),
            TextAlign = ContentAlignment.TopCenter
        };

        void ClickHandler(object? s, EventArgs e)
        {
            _selected = language;
            HighlightSelected();
            _onSelected?.Invoke(language);
        }

        panel.Click += ClickHandler;
        flag.Click += ClickHandler;
        label.Click += ClickHandler;
        panel.Controls.Add(flag);
        panel.Controls.Add(label);
        return panel;
    }

    private void HighlightSelected()
    {
        foreach (var kv in _tiles)
        {
            var selected = kv.Key == _selected;
            kv.Value.BackColor = selected ? Color.FromArgb(220, 235, 255) : SystemColors.Control;
            kv.Value.Padding = new Padding(selected ? 2 : 0);
            kv.Value.BorderStyle = selected ? BorderStyle.FixedSingle : BorderStyle.None;
        }
    }
}
