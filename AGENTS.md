# AI ScreenShot

Windows 11 x64 托盘截图工具；.NET 10、WinUI 3、Windows App SDK、Win32、FlaUI.UIA3。

- 默认中文；保留原文 API 名称。
- 原生控件，禁止引入 Web UI 或照搬 ShareX GPL 源码。
- 截图与提示词不写入日志；不得访问 Codex 内部数据库。
- 发送后结果不明确时返回 Unknown，禁止自动重发。
- 不覆盖剪贴板期间的用户修改，不使用固定坐标定位目标控件。
- 修改后运行 scripts/verify.ps1，更新 PROJECT_STATUS.md；不得自动 commit/push。
- 构建工具位于 .tools/dotnet；发布使用 scripts/publish.ps1。
