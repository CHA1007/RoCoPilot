
<p align="center">
  <img src="assets/icon.png" alt="RocoPilot 图标" width="96">
</p>

<h1 align="center">RocoPilot</h1>

<p align="center">面向《洛克王国：世界》 PC 客户端的 Windows 自动化工具箱<br>基于计算机视觉技术实现多种自动化功能</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/download/dotnet/9.0"><img src="https://img.shields.io/badge/运行时-.NET_9_/_C%23-512BD4?logo=dotnet&logoColor=white" alt="运行时 .NET 9 / C#"></a>
  <a href="https://github.com/lepoco/wpfui"><img src="https://img.shields.io/badge/界面-WPF_+_WPF--UI-0078D4" alt="界面 WPF + WPF-UI"></a>
  <a href="https://github.com/ultralytics/ultralytics"><img src="https://img.shields.io/badge/检测-YOLO11_+_ONNX_Runtime-111928?logo=ultralytics&logoColor=white" alt="检测 YOLO11 + ONNX Runtime"></a>
  <img src="https://img.shields.io/badge/捕获-WGC_/_BitBlt-0F7B5F" alt="捕获 WGC / BitBlt">
  <a href="https://github.com/oblitum/Interception"><img src="https://img.shields.io/badge/输入-Interception-444444" alt="输入 Interception"></a>
  <a href="https://velopack.io"><img src="https://img.shields.io/badge/更新-Velopack-6C3FC5" alt="更新 Velopack"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/许可证-GPL--3.0-blue" alt="许可证 GPL-3.0"></a>
</p>

<p align="center">简体中文 | <a href="README_EN.md">English</a></p>

## 功能特性

- **自动丢球** — 在大世界中识别画面中的野生精灵，自动转镜头居中、瞄准投球持续捕捉
- **自动战斗** — 识别战斗场景，自动释放技能完成回合
- **地图快传** — 识别世界地图中的传送按钮，支持自动触发或自定义按键触发，点击魔力之源、炼金釜等地图元素后自动完成传送
- **路线回放** — 编排「传送、延时、脚本回放」步骤组成执行链并循环运行；传送点借助内置魔力之源目录按地图几何对齐自动定位，某片段执行失败时自动回退到最近的上游传送点重跑，可实现自动跑图等操作
- **孵蛋查询** — 内置全量精灵蛋组数据，按精灵查蛋组、按蛋组查可一起孵蛋的精灵
- **中央调度** — 自动识别当前游戏场景，在工具之间自动切换，无需手动干预
- **灵动岛 OSD** — 游戏画面上方展示灵动岛样式悬浮状态窗，实时展示运行状态与关键读数
- **切出自动暂停** — 游戏窗口失焦时自动挂起，重新聚焦游戏窗口后自动续跑

全程仅基于计算机视觉 + 模拟鼠标键盘操作

## 效果展示

启动界面：

![启动界面展示](assets/showcase/启动界面展示.png)

灵动岛 OSD —— 工具箱信息悬浮窗：

![灵动岛效果展示](assets/showcase/灵动岛效果展示.gif)

## 获取

目前提供**测试版**，可从 [Releases 页面](https://github.com/CHA1007/RoCoPilot/releases) 下载：

- `RocoPilot-<版本>-Setup.exe` —— 单文件安装器，下载后双击即可安装

后续会发布**稳定版**，届时可从 Release 页下载并通过应用内增量更新升级

## 运行要求

- Windows 10 / 11

## 快速上手

1. 启动**洛克王国：世界**游戏客户端
2. 首次使用需安装 Interception 内核驱动（安装向导中勾选，或 RocoPilot「启动」页一键安装）并重启电脑后生效
3. 运行 RocoPilot，在「实时」页打开所需工具的开关，截图器会自动调用，中央调度按游戏场景自动切换工具
4. 需要跑图时，在「路线」页编排由传送 / 延时 / 脚本回放组成的路线并运行
5. 切出游戏自动暂停一切操作，切回自动续跑

## 数据来源

- 孵蛋查询的精灵蛋组、立绘数据来自 [洛克王国:手游 WIKI](https://wiki.biligame.com/rocom/)（蛋组计算器 / 孵蛋组别查询页），
  遵循 **CC BY-NC-SA 4.0**（署名-非商业性使用-相同方式共享）协议，仅用于学习与研究。
  数据文件本身的授权细节见 [assets/data/README.md](assets/data/README.md)。
- 路线回放内置的魔力之源锚点目录（名称 + 世界坐标）整理自 WIKI 地图数据，仅用于学习与研究。

## 免责声明

本项目采用 [GPL-3.0](LICENSE) 许可证

本项目仅供学习与研究使用，使用本工具可能违反游戏用户协议，由此产生的任何风险与后果由使用者自行承担

问题与建议请提 [Issue](https://github.com/CHA1007/RoCoPilot/issues)
