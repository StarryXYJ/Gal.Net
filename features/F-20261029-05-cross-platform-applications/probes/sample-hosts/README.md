# 重构后宿主原生回归

`verify-windows.py` 验证发布矩阵中的 Sample/CrossPlatform × Player/Developer 四份 Windows 原始 SDK 输出。先用各产物字节匹配的契约在独立作者副本中导出内容，将同一 galpak 安装到四份输出的 `pak`。

```powershell
python features/F-20261029-05-cross-platform-applications/probes/sample-hosts/verify-windows.py --publish out/cross-platform-p6e/windows-sdk/publish --content-report out/cross-platform-p6e/windows-content.json --output out/cross-platform-p6e/windows-native
```

内容报告提供独立测试项目的 `projectId`。探针拒绝复用已存在的默认 profile；Player 不传参数，使用新测试身份对应的 `LocalApplicationData/GalNet/<hash>`，Developer 使用证据目录中的独立 profile。测试从非应用工作目录启动，隐藏控制台，在窗口所属进程内检查 CoreCLR、SDL 和 VLC 的实际加载路径，通过该窗口的退出确认正常关闭，再核对启动次数落盘。不使用强杀作为成功退出证据；异常时仅清理自有测试进程。

原始发布产物与内容、日志和运行报告分别保留。探针不会修改已有玩家 profile，也不删除新测试身份的 profile。需要可交互 Windows 桌面及启动 GUI 的执行权限；原生结果独立于无界面测试和载荷 hash 检查。

P6e 已完成 Windows 六应用发布/载荷检查、完整内容准备，以及本探针的四宿主原生运行。首次执行曾被自动审批服务的额度错误阻止；用户恢复权限后实际四份均会话就绪、加载包内 CoreCLR/SDL/VLC、进度落盘并正常退出。外部调试使用 `ExternalSampleDebugTests` 的显式用例，两个 Developer 各实际执行 3/3；`SampleAppHost` 参数可指向原始 SDK 输出或带布局元数据的发行入口。

Linux 使用既有 `probes/source-compose/verify-linux-window.py` 的 `--raw-publish --entry <入口> --media-directory <本机媒体目录>`，对四份原始输出分别验证。Player 另传 `--player-project-id <独立测试内容身份>`，通过独立 XDG_DATA_HOME 测默认无参数启动。默认不传新选项时继续验证既有发行布局。Ubuntu 26.04.1 / WSLg 四份结果通过，不代表 Ubuntu 24.04 最低基线、原生桌面或新的公开媒体闭包。
