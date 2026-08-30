using DesktopPet.App.Views;
using DesktopPet.Core.Configuration;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;
using CoreHitTestMode = DesktopPet.Core.Configuration.HitTestMode;
using AppHitTestMode = DesktopPet.App.Models.HitTestMode;

namespace DesktopPet.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly PetSettingsCoordinator _settings;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Dictionary<AppHitTestMode, Forms.ToolStripMenuItem> _modeItems = new();
    private readonly Dictionary<double, Forms.ToolStripMenuItem> _scaleItems = new();
    private readonly Dictionary<double, Forms.ToolStripMenuItem> _opacityItems = new();
    private readonly Dictionary<double, Forms.ToolStripMenuItem> _frequencyItems = new();
    private Forms.ToolStripMenuItem _alwaysOnTopItem = null!;
    private Forms.ToolStripMenuItem _lockPositionItem = null!;
    private Forms.ToolStripMenuItem _reduceMotionItem = null!;
    private Forms.ToolStripMenuItem _startWithWindowsItem = null!;
    private bool _disposed;
    private bool _updatingChecks;

    public TrayIconService(
        MainWindow window,
        PetSettingsCoordinator settings,
        Action requestExit,
        Action toggleActionPicker,
        Action<PetSettings> applySettings)
    {
        _window = window;
        _settings = settings;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(CreateItem("恢复桌宠 (&R)", (_, _) => _window.RecoverControl()));
        menu.Items.Add(new Forms.ToolStripSeparator());

        var scaleMenu = new Forms.ToolStripMenuItem("大小");
        AddChoiceItems(
            scaleMenu,
            _scaleItems,
            [0.5, 0.75, 1, 1.25, 1.5, 2],
            value => value <= 1 ? $"{value:P0}" : $"{value:0.##}×",
            value =>
            Change(settings.Current with { Scale = value }));
        menu.Items.Add(scaleMenu);

        var opacityMenu = new Forms.ToolStripMenuItem("透明度");
        AddChoiceItems(
            opacityMenu,
            _opacityItems,
            [0.2, 0.4, 0.6, 0.8, 1],
            value => $"{value:P0}",
            value =>
            Change(settings.Current with { Opacity = value }));
        menu.Items.Add(opacityMenu);

        _alwaysOnTopItem = CreateCheckItem("始终置顶", (_, _) =>
            Change(settings.Current with { AlwaysOnTop = _alwaysOnTopItem.Checked }));
        _lockPositionItem = CreateCheckItem("锁定位置", (_, _) =>
            Change(settings.Current with { LockPosition = _lockPositionItem.Checked }));
        menu.Items.Add(_alwaysOnTopItem);
        menu.Items.Add(_lockPositionItem);

        var hitModeMenu = new Forms.ToolStripMenuItem("点击区域");
        AddModeItem(hitModeMenu, "仅人物像素", AppHitTestMode.CharacterPixels);
        AddModeItem(hitModeMenu, "整个窗口", AppHitTestMode.WholeWindow);
        AddModeItem(hitModeMenu, "完全穿透", AppHitTestMode.ClickThrough);
        menu.Items.Add(hitModeMenu);

        var frequencyMenu = new Forms.ToolStripMenuItem("动作频率");
        AddFrequencyItem(frequencyMenu, "悠闲 · 0.5×", 0.5);
        AddFrequencyItem(frequencyMenu, "正常 · 1×", 1);
        AddFrequencyItem(frequencyMenu, "活跃 · 2×", 2);
        menu.Items.Add(frequencyMenu);

        _reduceMotionItem = CreateCheckItem("减少动态效果", (_, _) =>
            Change(settings.Current with { ReduceMotion = _reduceMotionItem.Checked }));
        _startWithWindowsItem = CreateCheckItem("开机启动", (_, _) =>
        {
            var requested = _startWithWindowsItem.Checked;
            if (WindowsStartupService.TrySetEnabled(requested))
            {
                Change(settings.Current with { StartWithWindows = requested });
            }
            else
            {
                _startWithWindowsItem.Checked = !requested;
            }
        });
        menu.Items.Add(_reduceMotionItem);
        menu.Items.Add(_startWithWindowsItem);

#if DEBUG
        menu.Items.Add(CreateItem("动作选择器…", (_, _) => toggleActionPicker()));
#endif
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateItem("退出 (&X)", (_, _) => requestExit()));

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = (DrawingIcon)System.Drawing.SystemIcons.Application.Clone(),
            Text = "桌宠 · Ctrl+Alt+Shift+P 可恢复",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _window.RecoverControl();

        ApplySettings = applySettings;
        _window.HitTestModeChanged += OnWindowHitTestModeChanged;
        UpdateChecks(settings.Current);
    }

    private Action<PetSettings> ApplySettings { get; }

    public void UpdateChecks(PetSettings settings)
    {
        _updatingChecks = true;
        try
        {
            SetChoiceChecks(_scaleItems, settings.Scale);
            SetChoiceChecks(_opacityItems, settings.Opacity);
            SetChoiceChecks(_frequencyItems, settings.ActionFrequencyMultiplier);
            _alwaysOnTopItem.Checked = settings.AlwaysOnTop;
            _lockPositionItem.Checked = settings.LockPosition;
            _reduceMotionItem.Checked = settings.ReduceMotion;
            _startWithWindowsItem.Checked = WindowsStartupService.IsEnabled();
            UpdateModeChecks(FromCoreMode(settings.HitTestMode));
        }
        finally
        {
            _updatingChecks = false;
        }
    }

    private void Change(PetSettings candidate)
    {
        if (_updatingChecks)
        {
            return;
        }

        _settings.Update(_ => candidate);
        UpdateChecks(_settings.Current);
        ApplySettings(_settings.Current);
    }

    private static Forms.ToolStripMenuItem CreateItem(string text, EventHandler clickHandler)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += clickHandler;
        return item;
    }

    private static Forms.ToolStripMenuItem CreateCheckItem(string text, EventHandler clickHandler)
    {
        var item = CreateItem(text, clickHandler);
        item.CheckOnClick = true;
        return item;
    }

    private void AddChoiceItems(
        Forms.ToolStripMenuItem parent,
        IDictionary<double, Forms.ToolStripMenuItem> destination,
        IReadOnlyList<double> values,
        Func<double, string> formatLabel,
        Action<double> selected)
    {
        foreach (var value in values)
        {
            var item = CreateItem(formatLabel(value), (_, _) => selected(value));
            destination.Add(value, item);
            parent.DropDownItems.Add(item);
        }
    }

    private void AddFrequencyItem(Forms.ToolStripMenuItem parent, string label, double value)
    {
        var item = CreateItem(label, (_, _) =>
            Change(_settings.Current with { ActionFrequencyMultiplier = value }));
        _frequencyItems.Add(value, item);
        parent.DropDownItems.Add(item);
    }

    private void AddModeItem(Forms.ToolStripMenuItem parent, string label, AppHitTestMode mode)
    {
        var item = CreateItem(label, (_, _) =>
            Change(_settings.Current with { HitTestMode = ToCoreMode(mode) }));
        _modeItems.Add(mode, item);
        parent.DropDownItems.Add(item);
    }

    private void OnWindowHitTestModeChanged(object? sender, AppHitTestMode mode)
    {
        if (_updatingChecks)
        {
            return;
        }

        _settings.Update(settings => settings with { HitTestMode = ToCoreMode(mode) });
        UpdateChecks(_settings.Current);
    }

    private void UpdateModeChecks(AppHitTestMode selectedMode)
    {
        foreach (var pair in _modeItems)
        {
            pair.Value.Checked = pair.Key == selectedMode;
        }
    }

    private static void SetChoiceChecks(
        IReadOnlyDictionary<double, Forms.ToolStripMenuItem> items,
        double selected)
    {
        foreach (var pair in items)
        {
            pair.Value.Checked = Math.Abs(pair.Key - selected) < 0.001;
        }
    }

    public static AppHitTestMode FromCoreMode(CoreHitTestMode mode) => mode switch
    {
        CoreHitTestMode.CharacterPixels => AppHitTestMode.CharacterPixels,
        CoreHitTestMode.EntireWindow => AppHitTestMode.WholeWindow,
        CoreHitTestMode.ClickThrough => AppHitTestMode.ClickThrough,
        _ => AppHitTestMode.CharacterPixels
    };

    public static CoreHitTestMode ToCoreMode(AppHitTestMode mode) => mode switch
    {
        AppHitTestMode.CharacterPixels => CoreHitTestMode.CharacterPixels,
        AppHitTestMode.WholeWindow => CoreHitTestMode.EntireWindow,
        AppHitTestMode.ClickThrough => CoreHitTestMode.ClickThrough,
        _ => CoreHitTestMode.CharacterPixels
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.HitTestModeChanged -= OnWindowHitTestModeChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
