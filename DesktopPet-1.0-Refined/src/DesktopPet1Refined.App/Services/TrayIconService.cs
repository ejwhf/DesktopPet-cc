using DesktopPet1Refined.App.Models;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;

namespace DesktopPet1Refined.App.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<AppSettings> _applySettings;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Dictionary<double, Forms.ToolStripMenuItem> _scaleItems = new();
    private readonly Dictionary<double, Forms.ToolStripMenuItem> _opacityItems = new();
    private readonly Dictionary<HitTestMode, Forms.ToolStripMenuItem> _hitTestItems = new();
    private Forms.ToolStripMenuItem _alwaysOnTopItem = null!;
    private Forms.ToolStripMenuItem _lockPositionItem = null!;
    private Forms.ToolStripMenuItem _idleAnimationItem = null!;
    private bool _refreshing;
    private bool _disposed;

    public TrayIconService(
        MainWindow window,
        Func<AppSettings> getSettings,
        Action<AppSettings> applySettings,
        Action requestExit)
    {
        _window = window;
        _getSettings = getSettings;
        _applySettings = applySettings;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(CreateItem("恢复桌宠 (&R)", (_, _) => _window.RecoverControl()));
        _idleAnimationItem = CreateCheckItem("播放待机动画", (_, _) =>
            Change(_getSettings() with { IdleAnimationEnabled = _idleAnimationItem.Checked }));
        menu.Items.Add(_idleAnimationItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var scaleMenu = new Forms.ToolStripMenuItem("大小");
        foreach (var value in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 })
        {
            var label = value <= 1 ? $"{value:P0}" : $"{value:0.##}×";
            var item = CreateItem(label, (_, _) => Change(_getSettings() with { Scale = value }));
            _scaleItems.Add(value, item);
            scaleMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(scaleMenu);

        var opacityMenu = new Forms.ToolStripMenuItem("透明度");
        foreach (var value in new[] { 0.2, 0.4, 0.6, 0.8, 1.0 })
        {
            var item = CreateItem($"{value:P0}", (_, _) => Change(_getSettings() with { Opacity = value }));
            _opacityItems.Add(value, item);
            opacityMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(opacityMenu);

        _alwaysOnTopItem = CreateCheckItem("始终置顶", (_, _) =>
            Change(_getSettings() with { AlwaysOnTop = _alwaysOnTopItem.Checked }));
        _lockPositionItem = CreateCheckItem("锁定位置", (_, _) =>
            Change(_getSettings() with { LockPosition = _lockPositionItem.Checked }));
        menu.Items.Add(_alwaysOnTopItem);
        menu.Items.Add(_lockPositionItem);

        var hitTestMenu = new Forms.ToolStripMenuItem("点击区域");
        AddHitTestItem(hitTestMenu, "仅人物像素", HitTestMode.CharacterPixels);
        AddHitTestItem(hitTestMenu, "整个窗口", HitTestMode.EntireWindow);
        AddHitTestItem(hitTestMenu, "完全穿透", HitTestMode.ClickThrough);
        menu.Items.Add(hitTestMenu);

        menu.Items.Add(CreateItem("恢复默认位置", (_, _) => _window.ResetPlacement()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateItem("退出 (&X)", (_, _) => requestExit()));

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = (DrawingIcon)System.Drawing.SystemIcons.Application.Clone(),
            Text = "桌宠 1.0 Refined · 待机动画",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _window.RecoverControl();
        RefreshChecks(_getSettings());
    }

    public void RefreshChecks(AppSettings settings)
    {
        _refreshing = true;
        try
        {
            foreach (var pair in _scaleItems)
            {
                pair.Value.Checked = Math.Abs(pair.Key - settings.Scale) < 0.001;
            }
            foreach (var pair in _opacityItems)
            {
                pair.Value.Checked = Math.Abs(pair.Key - settings.Opacity) < 0.001;
            }
            foreach (var pair in _hitTestItems)
            {
                pair.Value.Checked = pair.Key == settings.HitTestMode;
            }

            _alwaysOnTopItem.Checked = settings.AlwaysOnTop;
            _lockPositionItem.Checked = settings.LockPosition;
            _idleAnimationItem.Checked = settings.IdleAnimationEnabled;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void AddHitTestItem(Forms.ToolStripMenuItem parent, string label, HitTestMode mode)
    {
        var item = CreateItem(label, (_, _) => Change(_getSettings() with { HitTestMode = mode }));
        _hitTestItems.Add(mode, item);
        parent.DropDownItems.Add(item);
    }

    private void Change(AppSettings settings)
    {
        if (!_refreshing)
        {
            _applySettings(settings.Normalize());
        }
    }

    private static Forms.ToolStripMenuItem CreateItem(string text, EventHandler handler)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += handler;
        return item;
    }

    private static Forms.ToolStripMenuItem CreateCheckItem(string text, EventHandler handler)
    {
        var item = CreateItem(text, handler);
        item.CheckOnClick = true;
        return item;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
