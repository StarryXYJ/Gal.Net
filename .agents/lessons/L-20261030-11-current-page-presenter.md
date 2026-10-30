# 重复呈现当前页不能清理它自己的 presenter

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications / Android 后台重进
- 范围：GameShell 的 scoped 页面复用与 TransitioningContentControl

现象：Android 返回桌面后，进程和 Activity 都正常，再次从 launcher 打开却只显示页面背景。没有未处理异常。隐藏退出按钮并保留会话后仍可复现，不能只看 PID/启动退出码就记为重进通过。

根因：后台流程重复 ResetTo 标题页，页面工厂返回当前的 scoped control。旧清理逻辑看到目标仍有 ContentPresenter 父级，就把父级 Content 清空；此父级实际上就是当前 presenter。随后 PageHost.Content 设置回相同对象不会触发属性更新，视觉树丢失页面。

正确做法：目标和 PageHost.Content 引用相同时直接完成呈现，不拆父级。只有切换到不同页面时才执行旧 presenter 清理。回归必须检查页面仍在窗口视觉树中，不能只断言导航 VM 或 Content 字段正确。

验证：三种主题的重复 ResetToAsync（None/None/CrossFade）回归在修复前 3/3 失败；修复后结果见 feature execution.md。手机截图 `out/android-device-validation/immersive-reopen-{1,2,3}.png` 是失败证据，不能误记为成功。该记录与 Android Activity 退出导致永久关闭共享 App 的另一条生命周期问题分别处理。
