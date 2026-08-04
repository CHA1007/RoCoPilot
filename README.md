<p align="center">简体中文 | <a href="README_EN.md">English</a></p>

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

## 功能特性

- **自动丢球** — 在大世界中识别画面中的野生精灵，自动转镜头居中、瞄准投球持续捕捉
- **自动战斗** — 识别战斗场景，自动释放技能完成回合
- **地图快传** — 识别世界地图界面，点击魔力之源，炼金釜等地图元素后自动点击传送按钮实现快速传送
- **中央调度** — 自动识别当前游戏场景，在工具之间自动切换，无需手动干预
- **灵动岛 OSD** — 游戏画面上方展示灵动岛样式悬浮状态窗，实时展示运行状态与关键读数
- **切出自动暂停** — 游戏窗口失焦时自动挂起，重新聚焦游戏窗口后自动续跑
- **自动更新** — 基于 Velopack 增量更新，支持稳定版 / 测试版两个渠道

全程仅计算机视觉 + 模拟鼠标键盘操作，不读取内存、不注入、不 Hook

## 效果展示

工具箱主界面 —— 工具展示：

![功能页面展示](assets/showcase/功能页面展示.png)

灵动岛 OSD —— 工具箱信息悬浮窗：

![灵动岛效果展示](assets/showcase/灵动岛效果展示.gif)

## 获取

目前仅提供**测试版**，暂时只能通过自行构建使用：克隆仓库后执行

两种构建方式任选其一，产物均在 `publish/` 目录，直接运行其中的 `RocoPilot.exe` 即可：

**自包含版** —— 内置 .NET Runtime，装完即用：

```
dotnet publish src/RocoPilot.Shell -c Release -r win-x64 --self-contained true -o publish
```

**轻量版** —— 体积小，但需自行安装 .NET 9 Desktop Runtime：

```
dotnet publish src/RocoPilot.Shell -c Release -r win-x64 --self-contained false -o publish
```

后续会发布**稳定版**安装包，届时可从 Release 页下载安装，并通过应用内增量更新升级

## 运行要求

- Windows 10 / 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)（仅构建**轻量版**时需要；自包含版无需安装）
- **Interception 驱动**（内核级输入模拟，首次使用须安装）：

  1. 从 [Interception Releases](https://github.com/oblitum/Interception/releases) 下载最新 zip 并解压
  2. 以**管理员身份**打开命令提示符，进入解压目录中的 `command line installer` 文件夹：
     ```
     cd Interception\command line installer
     ```
  3. 执行安装：
     ```
     install-interception.exe /install
     ```
  4. 看到成功提示后**重启电脑**（驱动须重启才生效）
  5. 重启后验证服务状态，确认输出包含 `STATE : 4 RUNNING`：
     ```
     sc query interception
     ```
  6. 如需卸载：
     ```
     install-interception.exe /uninstall
     ```
     卸载后同样需要重启若提示文件删除失败，重启后再执行一次卸载即可

  > 安装失败时请检查杀毒软件是否拦截了驱动写入 `C:\Windows\System32\drivers`

## 快速上手

1. 启动**洛克王国：世界**游戏客户端
2. 首次使用安装 Interception 驱动并重启（见「运行要求」）
3. 运行 RocoPilot，在「启动」页点击**启动截图器**
4. 在「实时」页打开所需工具的开关，中央调度会按游戏场景自动切换工具
5. 切出游戏自动暂停一切操作，切回自动续跑

## 免责声明

本项目采用 [GPL-3.0](LICENSE) 许可证

本项目仅供学习与研究使用，使用本工具可能违反游戏用户协议，由此产生的任何风险与后果由使用者自行承担

问题与建议请提 [Issue](https://github.com/CHA1007/RoCoPilot/issues)
