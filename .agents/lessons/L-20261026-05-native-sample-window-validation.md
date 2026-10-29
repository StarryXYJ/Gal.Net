# 原生 Sample 验证必须定位实际窗口并检查显式用例数

状态：confirmed
来源：F-20261026-01-engine-core-simplification / R8

## 现象与证据

ExternalSampleDebugTests 的三个用例可运行、编辑、Seek/Step、停止和重启，却在 CloseMainWindow 后超时，且没有宿主 shutdown 日志。r8-native-diagnostic 记录 Process.MainWindowTitle 为空；EnumWindows 显示同一 PID 有两个无标题可见窗口和一个 GalNet Avalonia 标题窗口。向后者发送 WM_CLOSE 后，三个用例均正常清理和零码退出，清理日志约 100 ms。

## 正确做法

- Windows 原生集成测试按自己启动的 PID、可见性与明确 Sample 标题定位唯一主窗口，再发送关闭消息；其他平台沿标准关闭接口。辅助代码只放测试层，不能广播关闭或操作其他进程。
- 关闭超时时共享读写读取活动日志，不能让 FileShare 异常覆盖原始失败。
- NUnit 显式测试单独以对应方法/fixture 过滤，并核对 TRX 的实际执行数量。本轮混合普通和显式方法的 OR 过滤只运行了两个普通用例；退出码不能证明显式用例已执行。
- Native 截图默认写 Pictures；沙箱拒绝该路径时，复验应申请执行这一个受控测试所需的权限，不能把截图缺失归因于渲染，也不为测试给产品增加全局路径后门。

证据：artifacts/engine-core-audit/r8-native-diagnostic.log、r8-results/ExternalSampleConnectsAtTitleStartsEditsSeeksStopsAndRejectsOldRuns.trx。测试辅助为 NativeSampleWindow。
