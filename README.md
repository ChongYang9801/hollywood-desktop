# Hollywood Desktop

[简体中文](README.md) · [English](README.en.md)

Hollywood Desktop 把电影风格的终端动画放到 Windows 桌面图标后方。桌面图标的点击、拖动和右键菜单仍由 Windows 处理。数字雨、滚动日志、十六进制、波形和进程表等面板会定时重排，面板数据均在本地模拟生成。

![Hollywood Desktop 动态终端壁纸预览](Preview.png)

## 下载与使用

1. 从 [GitHub Releases](https://github.com/ChongYang9801/hollywood-desktop/releases/latest) 下载 `HollywoodDesktop.exe`，双击启动。程序使用 Windows 自带的 .NET Framework 4.x，无需安装 WSL、Docker 或额外运行时。
2. 在任务栏通知区域右键单击 Hollywood 图标，可以**暂停动画**、**继续动画**或**重排终端面板**。如果没看到图标，展开任务栏上的 `^`。
3. 选择**退出并恢复壁纸**，即可撤下动画并显示原有壁纸。也可以在 PowerShell 里运行 `./HollywoodDesktop.exe --stop` 退出。

程序已在 Windows 11 双屏环境验证。Windows 关闭动画效果时，启动后会显示静止画面，可通过托盘菜单继续播放。如果系统的 Explorer 桌面层级发生变化，程序会先尝试重新挂载。启动时找不到可用的桌面背景层，程序会提示并退出。

## 从源码构建

在仓库目录打开 PowerShell，运行以下命令。脚本会调用 Windows 自带的 C# 编译器，在同一目录生成 `HollywoodDesktop.exe`。

```powershell
powershell -NoProfile -File .\Build.ps1
```

## 致谢

终端演示的灵感来自 Dustin Kirkland 的 [Hollywood](https://github.com/dustinkirkland/hollywood)。
