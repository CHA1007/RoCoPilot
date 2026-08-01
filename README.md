# RocoPilot

RocoPilot 是一个 Windows 工具箱，外壳托管多个可插拔工具。当前内置「自动丢球」：纯视觉识别《洛克王国：世界》画面中的野生精灵，自动瞄准投球持续捕捉，触发奇遇刷新。仅 Windows + 官方 PC 客户端 + 纯视觉 + 模拟操作，不做内存读取、注入或 Hook。

## 功能特性

自动识别画面中的精灵并丢球捕捉，全程无需手动操作；切出游戏自动暂停。

## 技术栈

| 层 | 技术 |
|---|---|
| 运行时 | .NET 9 / C# |
| 界面 | WPF + [WPF-UI](https://github.com/lepoco/wpfui)（Fluent 风格，整体布局借鉴 [BetterGI](https://github.com/babalae/better-genshin-impact)） |
| 目标检测 | [YOLO11](https://github.com/ultralytics/ultralytics) + ONNX Runtime（支持 DirectML GPU 加速） |
| 屏幕捕获 | Windows Graphics Capture（WGC）/ GDI 回退 |
| 输入模拟 | [Interception](https://github.com/oblitum/Interception) 内核级输入驱动 |

## 运行要求

- Windows 10 / 11
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Interception 驱动**：需以管理员身份安装并确认服务处于 `RUNNING` 状态
  ```
  sc query interception
  ```
  
## 致谢

- [BetterGI](https://github.com/babalae/better-genshin-impact)：界面布局与交互范式的参考来源。
- [Interception](https://github.com/oblitum/Interception)：设备栈级输入模拟驱动。
- [ultralytics](https://github.com/ultralytics/ultralytics)：YOLO11 检测模型与训练框架。
- [WPF-UI](https://github.com/lepoco/wpfui)：Fluent 风格控件库。

## 免责声明

本项目仅供学习与研究使用。使用本工具可能违反游戏用户协议，由此产生的任何风险与后果由使用者自行承担。
