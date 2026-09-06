# idle.alt · BD 主轴眨眼修订
2026-09-06。用户已确认本版效果，指定提交到 blink 分支。独立素材预览版本，应用与正式素材未改动。

## 查看
本机交互预览：http://127.0.0.1:8769/previews/preview.html
也可直接打开 previews/preview.html。支持正常 / 四倍慢速、暂停、逐帧、四状态按钮和开合度滑杆。
显示浅色、深色背景及 8 倍眼部放大；可展开原始 512px 画布和曲线对照。

## 主要变化
- 采用 BD 主轴（−16.445584°），保留 A/B/C/D 原坐标；每只眼的端点高差独立保留。
- 在 BD 局部坐标中重新描摹源上眼线与下眼缘，分别设计两眼闭合弧度。
- 全闭中点分别约 (230.158,227.148)、(280.364,212.990)；相对各眼弦线的法向下弯约 2.5、2.7 px。
- 上眼睑为主要运动部分；固定虹膜、眼白和高光原图采样位置，通过眼缝遮罩显示。
- 睫毛颜色从母图采样，沿上眼睑曲线绑定；软边和厚度随闭合调整。
- 清除原眼线软边残留后重新检查四状态及连续过渡。
- 肤色来自局部边界的调和补全。它是隐藏眼皮的视觉估计，后续可独立精修。
- 近睁眼使用局部重建残差修正，避免贴回原图时跳变；详见 provenance.json。没有整图淡化或身体形变。

## 文件
frames/open.png、light.png、near.png、closed.png：四张 512×512 RGBA 状态帧。
frames/slider/：41 个开合采样；frames/sequence/：实际循环采样。
layers/：固定底图补全、双眼原纹理、受保护原图、编辑遮罩、全闭眼线参考。
rig.json：BD 坐标、单眼曲线、弧度参数及母图哈希。source/calibration.json 保留原标定。
build.cjs：可重复的离线绑定渲染；不是 Cubism 原生工程。
previews/blink-normal.gif、blink-slow.gif：正常 / 慢速兼容预览，采用浅色背景。
previews/blink-normal.webp、blink-slow.webp：无损透明动画。
previews/blink-normal.svg、blink-slow.svg：内嵌 PNG 的自包含动画 SVG。
previews/blink-discrete.webp / gif：按原四状态停留表播放的节奏对照。
previews/full-light.png、full-dark.png：原始尺寸全身接触表。
previews/desktop-light.png、desktop-dark.png：默认 276px 画布接触表。
previews/eyes-light.png、eyes-dark.png：眼部四状态接触表。
qa/curves-original.svg、curves-rectified.svg：原姿态和摆正诊断图。
manifest.json：导出文件路径、大小与哈希。

## 节奏
连续版 80 ms 闭合 + 60 ms 全闭 + 160 ms 睁开，共 300 ms，每 4000 ms 循环。
开头睁眼停留 1000 ms，结尾停留 2700 ms；闭合动作按 20 ms 采样。
四倍慢速为 16000 ms 一轮。WebP/GIF 编码会合并连续相同闭眼帧，解码为 15 帧。
四状态版：轻闭40、近闭40、全闭60、近闭80、轻闭80、睁眼3700 ms。
分别记录在 timing.json、timing-discrete.json，不混用帧表。

## 检查结果
qa/verification.json：62 张 PNG，全部 512×512，眼区外差异和 alpha 差异均为0。
睁眼与母图字节一致；分层重组与原图一致；循环首尾一致。
三个 WebP 解码后的各显示帧与对应 PNG 的所有通道完全一致，帧序及时长正确。
qa/initial-report.json：101 个开合阶段采样中上下曲线不交叉，可见眼缝面积随闭合不增加。
qa/browser-report.json：状态按钮、滑杆、逐帧、真实循环均通过；SVG 每个检查时刻只有一个可见帧，回环正确。
浏览器截图采样确认 GIF 和 WebP 实际运动。GIF 的调色板与透明度不作为 RGBA 母图无损验收依据。
qa/acceptance.json：视觉检查记录、微闭差异量及交付范围。视觉检查代表本轮自查，可继续按你的反馈微调。

SHA-256：
C723F38EE331CA2DEB461BFF7256BD43F8F42491397E205399FC959DC48BB47B

## 重建
node build.cjs --sequence
node package.cjs
node serve.cjs
node verify.cjs
node browser-qa.cjs
node finalize.cjs
首次使用可在本目录执行 npm install 安装 package.json 中固定版本的 sharp / Playwright。
脚本优先使用本地依赖，也支持 CODEX_NODE_MODULES 或当前用户的 Codex 捆绑依赖目录。
浏览器检查默认使用已安装的 Google Chrome；其他安装位置可设置 CHROME_PATH。
先在另一个终端运行 node serve.cjs，再执行 browser-qa.cjs。
原生 Cubism/PSD 导出不在此版本内；可编辑的分层 PNG、参数和生成脚本均已交付。

本轮唯一图像输入是 idle.alt.png；旧眨眼图、遮罩及关键形态均未作为制作输入。
未使用 imagegen、光流补帧或生成式重绘。此前版本保留；提交范围只包含本目录已确认素材和必要工具。
qa/acceptance.json 中 githubPushed:false 记录的是生成验收时的状态，不是当前远端同步状态。


## Git 提交范围
包含源图与 BD 标定、状态帧、连续采样、分层、预览、重建脚本及验证记录。
未采用的首轮脚本、一次性修补脚本、服务日志和源图定位草稿留在本地。
无需改动应用即可直接查看 previews/preview.html；本次不将素材绑定到运行时。
