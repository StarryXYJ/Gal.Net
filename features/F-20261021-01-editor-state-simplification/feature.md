# Editor 写入口与状态精简

日期：2026-10-21

用户授权实施 Editor 优化清单的前三项：删除旧生产路径、统一变量声明修改入口、收口 GUI 未保存状态。快速迭代期间不保留旧 API 或转发兼容层。

范围：Workspace、GraphEditingService、Preview 变量视图、声明查询接口、Document/GalProject dirty 状态及对应测试和规范。

验收：生产 Entry 操作只使用项目作者服务；Preview 仅查看声明与编辑运行时当前值；声明改名与引用仍为一次历史；保存、Undo/Redo、草稿和关闭检查均以 ContentSession 为准；相关测试与构建通过。

非目标：全量刷新重构、缓存版本化、文件监听合并、历史内存预算优化、CLI 变量引用策略统一、预览宿主新功能。
