# 实施阶段

| 阶段 | 内容与验证 | 状态 |
| --- | --- | --- |
| P1 | galpak v2 导出/校验/安装、旧包兼容；集成覆盖普通解压、安装复用、损坏/混合路径拒绝与剧情包 | verified |
| P2 | Sample 应用根、参数开关、用户 profile；开发/玩家/独立参数模式构建与启动策略测试 | verified |
| P3 | lib/docs/pak 发行布局、SDK apphost、开发包；两种部署的真实解压 CLI/桌面启动和清单校验 | verified |
| P4 | 正式文档与总结、diff/格式检查、记录已验证事实与限制 | verified |

每阶段退出需要相应验证通过；构建完成后再运行同输出根的 no-build 测试。过程日志与产物写入 out/package-layout/，不上传、不创建 tag。

- P1/P2 首轮：archive-player.trx 25 passed；integration-player.trx 154 passed；developer.trx 7 passed（含实际子进程契约不匹配退出）。
- 初次构建因 Avalonia telemetry 在用户目录写日志被沙箱拒绝；设置 AVALONIA_TELEMETRY_OPTOUT=1 后通过，未修改第三方目标或产品行为。
- 最终玩家完整回归：final-player.trx 154 passed；独立参数开关：arguments-only.trx 6 passed；错误开关组合：invalid-capabilities.log 明确拒绝。
- 发行：release/0.1.0-dev.42/ 生成并验收 7 ZIP；desktop-smoke/app-validation.json 两种部署 passed=true，实际无参数从不同工作目录加载 pak。
- 开发包：developer/0.1.0-dev.43/；published-native-confirmed.trx 5 passed，其中 3 项使用该 ZIP 的真实 EXE 调试并正常退出，2 项加载两种实际整合包的 Graph/资源。
- galpak 集成：galpak-integration-final/verification.json 与 galpak-winps/verification.json 均通过；实际导出独立剧情包，调用发布脚本原函数安装并归并，验证不复制其他 buildId。分别运行于 PowerShell 7 和 Windows PowerShell 5.1。
- 发布保护 7 项、修改文件 whitespace/IDE0005 通过；dotnet format 初次因沙箱命名管道失败，允许本机进程通信后通过。
