# DesktopPet.App

这是桌宠的 Windows/WPF 外壳，目标框架为 `net8.0-windows`，不使用第三方 NuGet 包。

## 已实现（M1–M3）

- 无边框、透明、置顶、不显示在任务栏的桌宠窗口；
- 按住人物拖动，窗口位置与其他设置统一保存到 `%LOCALAPPDATA%\DesktopPet\settings.json`；
- 单实例，第二次启动会恢复已有桌宠；
- 托盘菜单、双击托盘恢复和退出；
- `Ctrl+Alt+Shift+P` 全局恢复热键；
- 三种命中模式：人物 alpha 像素、整个窗口、完全点击穿透；
- Per-Monitor V2 DPI manifest；
- Debug 构建中的 18 动作选择器入口（托盘菜单）。
- 从输出目录 `assets/manifest/assets.json` 按语义 ID 加载并缓存 18 张正式精灵；
- 接入 Core 行为状态机和动作时间轴，以约 30 FPS 播放启动、待机和自动行为；
- 点击触发爱心/挥手，拖动显示伸手姿势，释放播放下落回弹。
- 托盘可调 50%–200% 大小、20%–100% 透明度、置顶、位置锁定、动作频率、减少动态效果和开机启动；
- 设置统一写入 `%LOCALAPPDATA%\DesktopPet\settings.json`，支持 schema 迁移、原子写入和 `.bak` 回退；
- 按已保存显示器恢复位置，显示器被拔除或布局改变时回收到最近的实际工作区。

正式素材随构建复制到输出目录。v0.3.0 读取 `animations.json`，但状态变化时直接切到目标片段，不排队播放进入、退出、启动或落地转场；整套动画缺失、缺帧或清单无效时自动退回 `assets.json` 的 18 姿势模式。素材准备完成前窗口保持全透明。动画帧与 alpha 命中平面共同进入 24 帧 LRU 缓存；人物命中阈值为 alpha `16`。

## 构建

```powershell
dotnet build DesktopPet.sln
```

需要 Windows 和 .NET 8 SDK。Release 构建不会显示调试动作选择器菜单。
