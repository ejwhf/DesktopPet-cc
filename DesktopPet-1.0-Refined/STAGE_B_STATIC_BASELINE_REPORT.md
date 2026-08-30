# 阶段 B：静态等价副本验收报告

## 已完成

- 新建独立工程 `DesktopPet-1.0-Refined`。
- 程序名：`DesktopPet1Refined.exe`。
- 设置目录：`%LOCALAPPDATA%\DesktopPet1Refined`。
- 单实例锁：`Local\DesktopPet1Refined.Application`。
- 激活事件：`Local\DesktopPet1Refined.Activate`。
- 恢复热键：`Ctrl+Alt+Shift+R`，不占用旧版的 `Ctrl+Alt+Shift+P`。
- 只复制并加载原版 `idle.neutral`（06）完整 PNG。
- 窗口先保持全透明；06完成固定路径、尺寸和Alpha校验后才显示。
- 实现无边框透明窗口、置顶、拖拽、逐像素点击、点击穿透、托盘恢复和托盘退出。
- 实现独立的位置、大小、透明度、置顶、锁定和点击区域设置。

## 明确未加入

- 自动动作调度
- 眨眼和闭眼层
- 完整人物缩放
- 人物旋转、浮动和弹跳
- 动作切换和转场
- 除06以外的任何人物素材
- 计时驱动的动画渲染循环

## 素材核验

- 发布目录 PNG 数量：`1`
- 发布 PNG：`assets/sprites/idle.neutral.png`
- 原版06 SHA-256：`79EDF1E5F30DC16E594AFB0C345266F4BF70F2D68C7020B82D716E4A3B651931`
- 新版06 SHA-256：`79EDF1E5F30DC16E594AFB0C345266F4BF70F2D68C7020B82D716E4A3B651931`
- 结果：逐字节一致。

## 构建与原生窗口测试

- Release 构建：通过，`0` 警告、`0` 错误。
- 便携预览发布：通过。
- 当前 125% DPI 客户区：`375×450` 物理像素，对应 `300×360` 逻辑尺寸。
- 无边框、分层透明、置顶、工具窗口样式：通过。
- 透明点 `(1,1)`：`HTTRANSPARENT`。
- 人物像素点：`HTCLIENT`。
- 逐像素命中：通过。

## 旧版保护复核

- 旧版 ZIP SHA-256：`91E07A282CC8AA5C4E53AC751D17EA3BA0793D814519F95F5DD285B691917D3A`
- 旧版安装器 SHA-256：`97BCE95D29FC8A1BF4F65329F0778C08FE20713B216D7BC47A5113BBF7103FC2`
- 与阶段 A 记录一致。

## 预览位置

`dist/DesktopPet-1.0-Refined-0.1.0-win-x64/DesktopPet1Refined.exe`

本阶段只验收静态底座。确认人物大小、清晰度、启动表现、拖拽和托盘交互后，才提交第一个独立动作的小计划。
