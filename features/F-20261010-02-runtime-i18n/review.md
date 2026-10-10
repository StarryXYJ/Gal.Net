# Runtime i18n 交付检查

日期：2026-10-11

结论：pass-with-follow-up

本检查由实现 agent 对需求、设计、diff、测试和生命周期做交付自查，不等同于独立 reviewer 的审核。没有发现阻止本期文本国际化交付的问题。

## 检查结果

- Core/Runtime 无 DynamicLocalization/Avalonia 新依赖；JSON I/O 在 FileSystem，provider/resolver 和 UI 在 Sample。
- raw key 原样传递；默认语言完整性在包内内容加载时检查，外部翻译不能掩盖缺失默认文本。
- culture-first、同 culture 内层级/文件顺序覆盖及独有 key 保留有测试；UI 通过同一 adapter 获得覆盖。
- External 不进入导出 manifest 或资源源；相同安装复用保留翻译，不同安装仍拒绝覆盖。
- 首次选择发生在 Engine 创建前。语言/count 同一次写盘，失败不发布内存；settings attach 在其后，避免分开的后台偏好保存提前标记首次流程完成。
- 窗口关闭取消 startup token，再等待 session 生命周期锁；真实进程关闭验证没有死锁或计数写入。
- 重置使用已有玩家变量清空/重启路径，不为此重构进程内资源释放。

## 验证与后续

Integration 60、Core 61、Runtime 49、Builtins 52、Storage 12、Presentation 77、Architecture 24 通过。Headless 构建通过。真实 Avalonia 首次选择等待期间关闭验证通过。

后续人工验收应覆盖真实 GUI 确认 → 中英文剧情两条路线 → 设置切换 → 退出/再次启动 → 调试重置。当前自动化覆盖各组成部分及逻辑剧情；未冒充完整 GUI 全程验证。原生影音 opt-in 测试未启用。安装目录覆盖更新协议、编辑器作者工具、文件监听热更新、非文本资源与当前剧情即时重译不属于本期实现。
