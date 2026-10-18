# 查询上下文须随项目 scope 失效

状态：confirmed
日期：2026-10-18
来源：F-20261015-02-editor-authoring-workspace / P6e2b

现象：SceneQueryIsPositionBasedAndSelectionsRejectChangedOrClosedProjects 在 CloseAsync 完成后，旧选择仍能解析 handle，首轮新增回归稳定复现。早期 locator-focused.log 已被后续重跑覆盖；保留用例作为关闭/过期行为的回归，最终结果见 timeline-validation/locator-GalNet.Editor.Tests.trx。

根因：ProjectService 关闭时释放项目 scope，EditorDocumentService 对象仍可能被旧控件或局部变量引用，IsLoaded 不等于项目仍处于活动状态。原查询服务仅检查 IsLoaded 和内容版本，没有自己的释放状态。

正确做法：项目 scope 的查询服务实现幂等 IDisposable，释放时清除分析缓存并推进查询代次；旧选择按捕获代次拒绝解析。查询服务不拥有控件，也不应通过主动卸载其他服务来弥补自己的生命周期。

适用：持有不可变分析快照、选择候选或异步返回结果的项目服务。禁止仅以源对象尚可读取或 IsLoaded=true 作为会话仍有效的证据。新增测试同时验证保存相同内容不换代、作者修改/撤销换代、关闭项目使旧选择失效。
