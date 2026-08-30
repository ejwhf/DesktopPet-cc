# DesktopPet 1.0 Refined

基于 `DesktopPet-0.1.0-win-x64` 的独立动画重制工程。当前 0.1.2 已完成第一个动作：直接使用原始 05、06、07 完整 PNG 的待机循环，并增加独立运行包与可见启动错误提示。

当前明确不包含：

- 其他自动行为调度
- 任何眨眼眼带或闭眼叠加层
- 全身呼吸缩放
- 人物旋转、弹跳或上下浮动
- 动作转场和生成中间帧
- 05、06、07 以外的姿势

运行：

```powershell
dotnet run --project src/DesktopPet1Refined.App/DesktopPet1Refined.App.csproj
```

待机顺序与 1.0 相同：`06 1600ms → 07 1200ms → 05 220ms → 06 1400ms`，但移除了完整人物缩放。托盘菜单可暂停待机动画，并可调整大小、透明度、置顶、位置锁定和点击区域。双击托盘图标或按 `Ctrl+Alt+Shift+R` 可恢复控制。

设置独立保存到 `%LOCALAPPDATA%\DesktopPet1Refined\settings.json`，不会读取或修改旧版 `%LOCALAPPDATA%\DesktopPet`。
