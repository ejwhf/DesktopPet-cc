# DesktopPet-NaturalMotion Blink 0.1 Report

## 1. 结论与范围

本轮只完成第一个正式 Idle 行为：`06_idle` 静态底图上的单次同步眨眼。未制作或接入眼球跟随、视线、头部、呼吸、头发、拖拽、招手或其它人物动作；`assets/natural-motion/motions/` 中仍没有正式 Motion JSON。

实现遵循固定结构：

```text
512x512 CharacterRoot
├─ PetImage            = 06_idle，始终不换图
└─ BlinkOverlayImage   = 512x512 RGBA，X/Y=0，仅眼区非透明
```

`BlinkOverlayImage` 与底图同容器、同尺寸、同 `Stretch=Fill`，Margin 为 0，RenderTransform 为 Identity。运行时不对 Base 或 Overlay 做 Translate、Scale、Rotate、Canvas.Left/Top 或增量变换；每次结束强制 `Source = null`。

## 2. 修改文件

### 素材分析、正式素材与审核产物

- `tools/blink_asset_pipeline.py`：确定性坐标分析、局部眼皮合成、接触表、像素差异和 DPI 门禁。
- `assets/natural-motion/rigs/idle/blink_40.png`
- `assets/natural-motion/rigs/idle/blink_75.png`
- `assets/natural-motion/rigs/idle/blink_closed.png`
- `review/blink/`：ROI、坐标网格、普通/浅色/深色接触表、8×眼部表、时序预览、QA JSON 和测试记录。

### 状态机、WPF 接入和调试

- `src/DesktopPet1Refined.NaturalMotion/Runtime/BlinkTimeline.cs`
- `src/DesktopPet1Refined.App/NaturalMotion/BlinkController.cs`
- `src/DesktopPet1Refined.App/NaturalMotion/NaturalMotionCoordinator.cs`
- `src/DesktopPet1Refined.App/NaturalMotion/LiveMotionPlayer.cs`
- `src/DesktopPet1Refined.App/MainWindow.xaml`
- `src/DesktopPet1Refined.App/MainWindow.xaml.cs`
- `src/DesktopPet1Refined.App/App.xaml.cs`
- `src/DesktopPet1Refined.App/Models/AppSettings.cs`
- `src/DesktopPet1Refined.App/Services/TrayIconService.cs`
- `src/DesktopPet1Refined.App/BlinkDebugWindow.xaml`
- `src/DesktopPet1Refined.App/BlinkDebugWindow.xaml.cs`

### 测试

- `src/DesktopPet1Refined.NaturalMotion.Smoke/Program.cs`
- `src/DesktopPet1Refined.Blink.Wpf.Smoke/`
- `src/DesktopPet1Refined.App/Properties/AssemblyInfo.cs`：仅向 WPF smoke 程序开放 internal 测试入口。

## 3. 原始素材与保护哈希

`06` 未被修改，仍是唯一运行时底图：

```text
06 idle.neutral  79EDF1E5F30DC16E594AFB0C345266F4BF70F2D68C7020B82D716E4A3B651931
05 blink_smile   EA6F9979B28AD5AD06BE4B317BAB4AC2804EE36F5947F9270C9E1C18A218A00B
07 idle.alt       C723F38EE331CA2DEB461BFF7256BD43F8F42491397E205399FC959DC48BB47B
```

受保护 0.1.0 发行物工作后复核：

```text
DesktopPet-0.1.0-win-x64.zip
91E07A282CC8AA5C4E53AC751D17EA3BA0793D814519F95F5DD285B691917D3A

DesktopPet-Setup-0.1.0-win-x64.exe
97BCE95D29FC8A1BF4F65329F0778C08FE20713B216D7BC47A5113BBF7103FC2
```

两个 Hash 均保持原值；未覆盖旧版、未制作 Release。

## 4. Eye ROI 与固定闭眼几何

坐标由脚本在真实 06 上做暗度候选检测，再在原生像素坐标网格上测量；左右眼分别定义，没有镜像复制。

| 眼睛 | 中心 | Eye ROI（左、上、右、下） | 半径 | 固定 closure line |
|---|---:|---:|---:|---:|
| screen-left | `(238, 221)` | `(213, 197, 264, 246)` | `(21, 19)` | `y=222, arc=5, tilt=2` |
| screen-right | `(281, 210)` | `(257, 187, 306, 234)` | `(20, 18)` | `y=211, arc=4, tilt=1` |

完整眼镜关键点和鼻梁点见 `review/blink/blink_analysis.json`，可视化见 `review/blink/blink_eye_roi_debug.png`。

## 5. 05 的使用结论

05 与 06 的双眼参考点无法通过共享刚性平移达到约 1 px 稳定：最佳共享位移约 `(-3.88, 3.37)`，左右眼残差均约 `5.99 px`。因此立即放弃从 05 裁像素或贴眼睛。

05 只用于人工确认闭眼弧度、眼皮方向和角色闭眼风格；正式 Overlay 的肤色拟合、眼皮线和透明区域全部由 06 周边像素与固定几何确定，不复制 05 的任何像素，也不 Warp/缩放/旋转脸部。

## 6. Overlay 尺寸、Hash 与像素范围

三张正式素材均为 `512×512 RGBA`，正式文件与审核候选逐字节一致。

| Overlay | SHA-256 | 非透明像素 | 非透明范围 | 合成后实际变化像素 | 实际变化范围 |
|---|---|---:|---:|---:|---:|
| blink_40 | `53B25DFEBA2356194DF40B02265427980E0FFAE20274951FC5A74B620CBEA3C7` | 730 | `(218,193)-(301,223)` | 672 | `(218,193)-(301,223)` |
| blink_75 | `F51D5F33C31F22C956AA3B029ED049289719545F7EDBBF2A13A0A8AFA4B833FC` | 1584 | `(218,193)-(301,240)` | 1473 | `(218,193)-(301,240)` |
| blink_closed | `84D747443139DFEDC8E8428315D928C4B971664C862AF5D3A8CD0716437840B9` | 2254 | `(218,193)-(301,240)` | 2254 | `(218,193)-(301,240)` |

## 7. Pixel Diff 与视觉 QA

每个阶段均通过以下硬门禁：

| Overlay | Eye ROI 外差异 | 实测眼球内部外差异 | 受保护眼镜像素差异 |
|---|---:|---:|---:|
| blink_40 | 0 | 0 | 0 |
| blink_75 | 0 | 0 | 0 |
| blink_closed | 0 | 0 | 0 |

因此头发、眉毛、鼻子、嘴、脸轮廓、脖子和身体均保持原始 06；眼镜帧与鼻梁区域由保护 Mask 强制留空，最终像素继续来自 06。

目视审核已覆盖普通透明棋盘、浅色、深色背景、完整人物横向接触表、8×双眼局部表和 300 ms 时序 GIF。第一版选择三张稳定阶段，不继续增加中间帧。

## 8. Blink 状态机

状态流：

```text
Interval → Closing → Closed → Opening → Interval
```

- `Closing = 100 ms`
- `Closed = 50 ms`
- `Opening = 150 ms`
- 总时长 `300 ms`
- 等待间隔：每次完成后重新随机 `3000..7000 ms`，只影响下一次开始时间。
- 时间线使用 `Stopwatch.Elapsed` 的真实 elapsed time，不假设 Timer tick 精确。
- 一个 `progress` 同时选择包含左右眼的同一张 full-canvas Overlay；左右眼绝对同步，但几何不镜像。
- 闭眼：`OPEN → 40 → 75 → CLOSED`；睁眼反向复用。
- 等待期只有一个可复用的 one-shot `System.Threading.Timer`；`CompositionTarget.Rendering` 只在 300 ms 眨眼期间挂接，结束立即解绑。

自动眨眼只在可见、非拖拽、非动作播放、非 Reduce Motion 的 Idle/06 状态可用。隐藏、拖拽开始、动作开始、Reduce Motion 或退出都会停止计时、终止当前时间线、解绑 Rendering 并清空 Overlay；条件恢复后重新生成一次 3–7 秒等待。

## 9. Debug Preview

托盘菜单 `NaturalMotion 调试` 新增：

- `打开 Idle 眨眼调试...`
- `手动触发一次眨眼`

独立 Blink Debug 支持：

- Trigger、Pause、Restart
- `0.25× / 0.5× / 1.0×`
- 固定 Open、40%、75%、Closed
- 显示左右 Eye ROI
- 正常、浅色、深色背景

运行方式：

```powershell
dotnet run --project DesktopPet-1.0-Refined/src/DesktopPet1Refined.App/DesktopPet1Refined.App.csproj
```

然后从托盘进入 `NaturalMotion 调试 → 打开 Idle 眨眼调试...`。正式应用长期保持 06 静态，只在随机间隔触发单次眨眼。

## 10. DPI 结果

确定性缩放检查将 Base 与每张 full-canvas Overlay 从相同 `(0,0)` 原点、相同画布通过同一缩放路径渲染：

| DPI | 像素画布 | 三阶段 Overlay 尺寸 | 缩放后 Eye ROI 外差异 |
|---:|---:|---:|---:|
| 100% | 512×512 | 512×512 | 全部 0 |
| 125% | 640×640 | 640×640 | 全部 0 |
| 150% | 768×768 | 768×768 | 全部 0 |
| 200% | 1024×1024 | 1024×1024 | 全部 0 |

WPF 应用使用 `PerMonitorV2`；WPF smoke 同时验证运行时 Overlay 为 512×512、Margin 0、Identity Transform、`Stretch.Fill`。Base 与 Overlay 位于同一 512×512 Grid/Viewbox，不存在独立 DPI 坐标计算或 1 px 偏移来源。

## 11. 测试结果

| 检查 | 结果 |
|---|---|
| Debug build | PASS，0 warning / 0 error |
| Release build | PASS，0 warning / 0 error |
| 素材脚本门禁 | PASS，三阶段 ROI 外、眼内部外、眼镜差异均为 0 |
| 1000 次纯状态机循环 | PASS；每次 300 ms 后回到 Idle/Open；无增量坐标或 Transform |
| Pause/Resume | PASS；暂停期间 elapsed 不推进，恢复保留偏移 |
| 0.25× / 0.5× / 1× | PASS；总时长按速度缩放 |
| 100 次真实 WPF Rendering 触发 | PASS，100/100；40/75/Closed 均实际进入 Image.Source；结束均为 null/Collapsed |
| 拖拽/动作/隐藏/Reduce Motion 门禁 | PASS；禁用状态不能触发且 Overlay 为 null |
| 100 次资源检查 | PASS；Private Bytes `49,000,448 → 56,221,696`，线程 `30 → 34`，阈值内且未按次数累积 |
| 30 分钟真实 WPF 自动运行 | PASS；共完成 340 次（含启动手动 1 次），正常退出 |
| 30 分钟资源检查 | PASS；Private Bytes `48,480,256 → 59,310,080`，线程 `30 → 28`；中段内存有回落，非单调泄漏 |
| 0.1.0 保护 Hash | PASS，zip 与 installer 均保持原值 |

持久化测试证据：

- `review/blink/blink_wpf_100_report.json`
- `review/blink/blink_wpf_soak_30_report.json`
- `review/blink/blink_dpi_report.json`
- `review/blink/blink_qa.json`

## 12. Review 素材路径

- `review/blink/blink_open.png`
- `review/blink/blink_40.png`
- `review/blink/blink_75.png`
- `review/blink/blink_closed.png`
- `review/blink/blink_contact_sheet.png`
- `review/blink/blink_contact_sheet_light.png`
- `review/blink/blink_contact_sheet_dark.png`
- `review/blink/blink_eye_contact_sheet_8x.png`
- `review/blink/blink_eye_roi_debug.png`
- `review/blink/blink_face_coordinate_grid_05.png`
- `review/blink/blink_face_coordinate_grid_06.png`
- `review/blink/blink_face_coordinate_grid_07.png`
- `review/blink/blink_timing_preview.gif`
- `review/blink/blink_analysis.json`
- `review/blink/blink_diff_report.txt`
- `review/blink/blink_qa.json`
- `review/blink/blink_dpi_report.json`

## 13. 已知限制

- 第一版只有 40%、75%、Closed 三张 Overlay；在 0.25× 调试速度下能看到离散换帧。这是为避免几何漂移而有意选择的稳定性优先方案。
- 只支持单次同步眨眼；没有双眨眼、困倦、半眯眼或左右时间差。
- 05 因约 5.99 px 刚性对齐残差只能作为形态参考，不能复用其像素。
- Windows UI 控制插件在本轮验证时无法初始化，因此没有通过该插件点击调试窗口；已由真实 WPF 窗口、CompositionTarget.Rendering、100 次触发和 30 分钟独立测试进程替代，并保留机器可读结果。

本轮未 push、未创建 Release、未提交 Git commit、未修改 0.1.0 发行物。
