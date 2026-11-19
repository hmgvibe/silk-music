using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Silk.Ui;

/// <summary>Keeps playback reachable from the notification area while the window is hidden.</summary>
public sealed class TrayIcon : IDisposable
{
    private const int MaxTooltipLength = 120;

    private readonly AppModel _model;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _title = new() { Enabled = false };
    private readonly ToolStripMenuItem _toggle = new();

    public TrayIcon(AppModel model, Action show, Action exit)
    {
        _model = model;
        var menu = new ContextMenuStrip();
        _toggle.Click += (_, _) => model.Toggle();
        menu.Items.Add(_title);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggle);
        menu.Items.Add("上一首", null, (_, _) => model.Previous());
        menu.Items.Add("下一首", null, (_, _) => model.Next());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("開啟絲音", null, (_, _) => show());
        menu.Items.Add("結束", null, (_, _) => exit());
        menu.Opening += (_, _) => UpdateMenu();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) show();
        };
        model.PropertyChanged += OnModelChanged;
        UpdateTooltip();
    }

    public void ShowBackgroundHint() =>
        _icon.ShowBalloonTip(4000, "絲音仍在背景執行", "可從這個圖示重新開啟或結束。", ToolTipIcon.None);

    public void Dispose()
    {
        _model.PropertyChanged -= OnModelChanged;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.CurrentTitle)) UpdateTooltip();
    }

    private void UpdateMenu()
    {
        _title.Text = _model.HasTrack ? _model.CurrentTitle : "還沒有播放";
        _toggle.Text = _model.IsPlaying ? "暫停" : "播放";
        _toggle.Enabled = _model.HasTrack;
    }

    private void UpdateTooltip()
    {
        var text = _model.HasTrack ? $"{_model.CurrentTitle} - {_model.CurrentArtist}" : "絲音";
        _icon.Text = text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
    }

    private static Icon LoadIcon() =>
        (Environment.ProcessPath is { } path ? Icon.ExtractAssociatedIcon(path) : null) ?? SystemIcons.Application;
}
