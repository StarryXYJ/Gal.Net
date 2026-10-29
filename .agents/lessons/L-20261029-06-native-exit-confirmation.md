# 原生 Sample 关闭测试须完成退出确认

- 状态：confirmed
- 来源：F-20261029-01-desktop-package-layout。
- 现象：开发发行包已通过调试连接、开始、变量修改、定位和停止，原生测试仍在退出处超时；finally 的临时目录清理 IOException 可掩盖原始超时。
- 根因：MainWindow 的退出遮罩默认聚焦 Cancel；仅向自有窗口发送 WM_CLOSE 不会完成关闭，不能据此推断发行包的释放链有问题。
- 做法：NativeSampleWindow 先按 PID 与明确标题选取唯一自有窗口，再排队 WM_CLOSE、Tab、Enter，完成真实确认；继续断言零退出码和玩家变量已落盘。输入只发给该 HWND，不切换用户前台窗口。
- 验证：未确认时三种原生场景均失败；补充确认后，同一开发 ZIP 三种场景全部通过，另两种真实整合内容加载也通过。见 out/package-layout/tests/published-native-confirmed.trx。
- 边界：这是测试适配，不在玩家代码中跳过确认，也不通过强杀进程宣称正常退出验收成功。失败清理异常需要保留原始诊断。
