# Closed 回调可能晚于 DI scope 释放

状态：confirmed。

来源：F-20261021-03 外部调试的真实 Sample 子进程验收。连接/开始/变量/跳转/停止/重开均通过，正常关闭进程却以 0xC000041D 退出；stderr 指向 MainWindow.OnClosed 对已释放 IServiceProvider 的 GetRequiredService。

根因：App 的异步 Closing 先释放项目 scope，再二次调用 Close；原生窗口 Closed 随后才发生。Headless 不完整模拟此原生关闭顺序，构造/挂载成功不能证明最终退出正常。

正确做法：窗口所需实例在构造时捕获；窗口绑定提供幂等显式解绑，App 在 scope 释放前调用，Closed 再调用可安全返回。不要在 Closed/迟到的 UI 回调中重新解析可能已释放的 scope，也不要用忽略异常掩盖解绑遗漏。

验证方式：真实启动独立 Sample，完成一次开始/停止/重开，使用正常窗口关闭并断言退出码为零，而不是仅 Kill 测试进程。适用于异步 Closing、项目 scope 和窗口生命周期分离的宿主。
