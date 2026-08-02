# RocoPilot

RocoPilot 是一个 Windows 工具箱，外壳托管多个可插拔工具。当前内置「自动丢球」：纯视觉识别《洛克王国：世界》画面中的野生精灵，自动瞄准投球持续捕捉，触发奇遇刷新。仅 Windows + 官方 PC 客户端 + 纯视觉 + 模拟操作，不做内存读取、注入或 Hook。

## 功能特性

自动识别画面中的精灵并丢球捕捉，全程无需手动操作；切出游戏自动暂停。

## 技术栈

| 层 | 技术 |
|---|---|
| 运行时 | .NET 9 / C# |
| 界面 | WPF + [WPF-UI](https://github.com/lepoco/wpfui) |
| 目标检测 | [YOLO11](https://github.com/ultralytics/ultralytics) + ONNX Runtime（支持 DirectML GPU 加速） |
| 屏幕捕获 | Windows Graphics Capture（WGC）/ GDI 回退 |
| 输入模拟 | [Interception](https://github.com/oblitum/Interception) 内核级输入驱动 |

## 运行要求

- Windows 10 / 11
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Interception 驱动**（内核级输入模拟，首次使用须安装）：

  1. 从 [Interception Releases](https://github.com/oblitum/Interception/releases) 下载最新 zip 并解压。
  2. 以**管理员身份**打开命令提示符，进入解压目录中的 `command line installer` 文件夹：
     ```
     cd Interception\command line installer
     ```
  3. 执行安装：
     ```
     install-interception.exe /install
     ```
  4. 看到成功提示后**重启电脑**（驱动须重启才生效）。
  5. 重启后验证服务状态，确认输出包含 `STATE : 4 RUNNING`：
     ```
     sc query interception
     ```
  6. 如需卸载：
     ```
     install-interception.exe /uninstall
     ```
     卸载后同样需要重启。若提示文件删除失败，重启后再执行一次卸载即可。

  > 安装失败时请检查杀毒软件是否拦截了驱动写入 `C:\Windows\System32\drivers`。
  
## 免责声明

本项目仅供学习与研究使用。使用本工具可能违反游戏用户协议，由此产生的任何风险与后果由使用者自行承担。
