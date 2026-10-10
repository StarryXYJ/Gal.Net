# Runtime i18n 实现总结

日期：2026-10-11

原 [2026-10-10 阶段性总结](history/summary-20261010.md) 保留为历史；其中未完成事项描述的是当时状态，并非当前结论。

Phase 1–5 已完成；[阶段证据](phase-plan.md)、[交付自查](review.md) 和 [当前国际化契约](../../docs/spec/i18n.md) 是具体依据。

## 实现结果

- Sample UI 嵌入中英文资源；同一个 culture service 驱动 UI 与游戏文本 resolver。
- 人物 catalog 保存 ID/姓名 key；dialogue、NVL 和选项从 raw 起保存 key，编译展开不生成或改写它。
- FileSystem 递归加载包内文本，校验默认语言、人物引用和 JSON 结构；外部目录递归覆盖 UI/game，损坏文件独立诊断跳过。
- 宿主可传入额外 JSON source；第三方新增语言进入设置和首次选择列表。
- 首次选择确认后原子保存语言/count，后续启动恢复语言并增加 count；取消/失败不计数，清空玩家变量后再次选择。
- 语言切换保持 UI 即时刷新、剧情从下一条起生效；不强制重启，不重译已显示对话/选项/NVL 历史。

## 模块与关键取舍

Core/Builtins 定义人物/key 和展示前解析契约。Runtime.Content 只持有来源描述与诊断。FileSystem 加载/验证，安装器保留 External；Editor 仅在导出时排除第三方文件。Sample composition 和共享源码 adapter 使用 DynamicLocalization，未新增生产项目或让 Headless 依赖 Avalonia。

DynamicLocalization.Avalonia 2.1.2 与 Avalonia 12 的运行时 Binding API 不兼容，改用 Core + Sample ReflectionBinding。JSON provider 同 culture 是整字典替换且无父语言回退，采用每文件独立 provider、culture-first 有序查询；不合并文件或重写 JSON 展平算法。人物自动补全和模板工具留给 Editor 后续功能；默认 key 检查发生在内容加载，而非构建器。

External 不随包发布。安装器仍拒绝覆盖不同/损坏安装，复用已有安装不删除第三方文件；没有新增包更新协议。文件修改后重新启动加载。

## 验证

Integration 60/60；Core 61/61；Runtime 49/49；Builtins 52/52；Storage 12/12；Presentation 77/77；Architecture 24/24。Headless 构建 0 warning/0 error。此前已验证中英文 × 两条逻辑路线到结尾及真实 Headless 运行；本期补充真实 Avalonia 首次选择期间正常关闭、无计数写入。

未执行完整 GUI 确认后剧情的人工全程验收；原生影音 opt-in 测试未启用。现有 analyzer/XAML warning 不在本期清理范围。

## 提交与后续

按用户要求按 Phase 提交：Phase 4 为 `2ac4d03`，Phase 5 随本总结独立提交。粒子结尾崩溃已由用户在本轮前修复，没有混入这两次提交。

Editor 模板/自动生成 key/人物自动补全、非文本本地化、热更新协议与当前文本实时刷新另行规划。相关包兼容/逐文件行为/渲染回调 lesson 保留在 agent 知识索引，不复制到正式 spec。
