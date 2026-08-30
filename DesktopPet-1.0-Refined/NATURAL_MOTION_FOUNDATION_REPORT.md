# NaturalMotion 阶段 A–C 实施报告

## 本次完成

- 复核并重新记录 DesktopPet 0.1.0 ZIP 与安装器 SHA-256，结果与既有基线报告一致。
- 在 `natural-motion` 分支上继续使用 `DesktopPet-1.0-Refined`，未重建窗口、托盘、拖拽、逐像素命中、单实例和设置系统。
- 删除 0.1.1 的 `06 → 07 → 05 → 06` 播放器、Timer、设置项和托盘开关。
- 启动时只允许 `pose-profiles.json` 中的 `06_idle` 通过尺寸、路径和 Alpha 校验后显示。
- 新增 Pose Profile、Action Rig 目录约定、Motion JSON 模型与安全校验。
- 新增 Layer Motion、Part Sprite、Window Motion 的 WPF 渲染入口；Pose 使用稳定完整母版。
- 新增真实经过时间采样、关键帧插值、五种 easing、一次/循环/往返、暂停、速度、中断与退出策略。
- 渲染回调只在 Motion 实际播放时挂接；静态和暂停状态没有持续动画 Timer 或 Rendering 回调。
- 新增独立 NaturalMotion Debug Preview，支持 Play、Pause、Restart、0.25×、0.5×、1×、三种背景、Pivot、Anchor 和 Bounds。
- 托盘只提供 Preview 和已注册 Motion 的手动调试入口；没有 Behavior Scheduler。

## 刻意未做

- 没有制作眨眼、招手、欢呼、爱心、阅读、写字、坐姿、睡眠、探头或下落。
- 没有拆人物图层、补绘遮挡区域或生成局部素材。
- `assets/natural-motion/motions/` 没有正式动作 JSON。
- 05 与 07 文件仍只保留在开发目录作历史参考，不参与构建和发布。
- 没有自动行为，也没有任何全身 Scale 属性；Motion 数据模型从类型层面不提供 Scale。

## 下一道门

下一步应进入阶段 D，但必须先单独提交“待机 + 真眨眼”的动作设计和素材方案，经确认后才制作局部素材。本次不越过该门。
