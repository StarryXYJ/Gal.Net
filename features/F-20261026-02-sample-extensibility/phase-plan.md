# 实施阶段

| Phase | 内容 | 状态 |
| --- | --- | --- |
| P1 | 设置/启动可替换、默认 DI 与页面扩展 | verified |
| P2 | 玩家/运行绑定和显式变量提交 | verified |
| P3 | 本地化/Headless 整理、集成验证与收尾 | in-progress |

## P1

依赖：底层重构完成。修改 Avalonia.GameHost、GameView、Sample 接线。验证自有设置实现、派生标题 VM/手写 View、已有设置与页面回归。退出：默认与替换路径均实际运行，接口不引用完整默认偏好 DTO。

## P2

依赖：P1。修改 Engine 的有限变量入口及宿主生命周期。验证预校验、检查点、玩家落盘、恢复/停止隔离、绑定失败/释放。退出：同一套默认生命周期支持项目服务，不增加第二个执行门。

## P3

依赖：P2。删除 Sample 中可移入默认宿主的重复适配，修复 Headless 输入/终态/保存观察；同步规范与手册。执行 Runtime、GameHost、Presentation、Integration、Architecture 相关回归与解决方案构建，review 后修复本次问题。退出：有测试证据、review/summary、正式文档及精确 Git 提交；任何环境限制明确保留。

P1 证据：手写 ProjectSettingsView.axaml、派生标题命令及默认服务接入已运行；首轮定向 Integration 38 项通过，日志 artifacts/sample-extensibility-integration.log。Runtime 新增变量入口 7 项已通过，生命周期扩充验证继续归 P2。

P2 证据：ExternalVariableTests 7 项通过；SampleExtensibilityTests 3 项通过，包含玩家设置真实落盘重开、Prepare 后接入运行、替换/停止/失败恢复及异步绑定释放。测试曾因 UI 回调中的 ThrowsAsync 同步阻塞而挂起，改用直接 await/try-catch 后通过，复用既有 L-20261015-02-ui-tests-await-failures，不新增重复 lesson。日志 artifacts/sample-extensibility-runtime.log、sample-extensibility-lifecycle.log。
