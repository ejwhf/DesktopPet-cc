# DesktopPet 1.0 Refined · NaturalMotion Preview

这是以 `DesktopPet-1.0-Refined` 静态底座为基础的 NaturalMotion 开发工程。当前版本为 `0.0.1 Preview`，只完成总体重制计划的阶段 A–C，不包含任何重新制作的具体人物动作。

当前启动行为固定为：

```text
启动 → 校验 pose-profiles.json → 显示 06_idle → 长期静止
```

不存在 `06 → 07 → 05 → 06` 轮播、呼吸缩放、上下浮动、自动表情或自动大动作。发布输出只复制 `idle.neutral.png`；开发目录中的 05/07 仅作为历史实验参考。

## 已建立的底座

- Pose：经过 Profile 校验的完整姿势母版。
- Layer Motion：局部图层的 `TranslateX`、`TranslateY`、`Rotate`、`Opacity`。
- Part Sprite：局部替换帧及独立时间采样。
- Window Motion：只修改窗口位置，不改变完整人物图片。
- 真实时间轴、关键帧、Easing、Pivot、Anchor、中断与退出策略。
- 仅播放时挂接的渲染循环；静态和暂停状态不持续采样。
- 独立 Debug Preview：Play、Pause、Restart、0.25×、0.5×、1×、正常/浅色/深色背景、Pivot/Anchor/Bounds。

## 运行与验证

```powershell
dotnet run --project src/DesktopPet1Refined.App/DesktopPet1Refined.App.csproj
dotnet run --project src/DesktopPet1Refined.NaturalMotion.Smoke/DesktopPet1Refined.NaturalMotion.Smoke.csproj -c Release
```

右击托盘图标，选择 `NaturalMotion 调试 → 打开独立 Preview...`。当前正式 Motion 目录为空，因此 Preview 只显示静态 06，并明确提示尚无正式动作。

设置继续保存在 `%LOCALAPPDATA%\DesktopPet1Refined\settings.json`，单实例与产品标识暂不改名。旧版 `%LOCALAPPDATA%\DesktopPet` 和 `dist/DesktopPet-0.1.0-win-x64` 不会被读取或修改。

具体动作必须按“动作设计 → 素材方案 → 静态重组检查 → Preview → 用户确认 → 运行时手动接入”的顺序逐个推进。本阶段没有制作眨眼、招手或其他动作。
