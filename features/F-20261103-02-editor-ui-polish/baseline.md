# 基线盘点（进行中）

2026-11-03 静态代码检查；尚未完成真实桌面、主题、语言、窗口尺寸或全部扩展的运行时盘点，P0 保持 in-progress。

- 窗口：`App.axaml` 内有共享 Ursa 标题栏及 caption 样式；设置窗口分别 480×380、520×480。普通、浮动和原生装饰的实际覆盖及已保存窗口位置仍待检查。
- 设置：`ConfigurationSectionEditor` 对无显式 editor 的非标量字段强制 JSON；项目音频 Bus/Track、默认视觉 map 已手工赋予 structure。音频契约有唯一 ID、Bus 引用、增益、容量和策略验证；旧 audio feature 记录曾支持轨道增删改，历史视觉布局尚待比对。
- JSON 用户入口：共享 Registry 默认 JsonElement/未知结构 JSON、深度 8 回退；StructuredFieldEditor 显式 JSON 切换/回退；SchemaForm 异常回退；标量值验证失败回退；Editor 的 Transform、Flipbook、Timeline、ShaderVector、音效参数、Variant 调用 PropertyEditorSupport JSON 修复；图集异常修复和通用文件缓冲另有入口。具体实际可达性和扩展实例待逐项运行验证。
- 菜单：文件面板空白和项目菜单由 AXAML 定义；Graph、Entry 和 Timeline 另有代码构造菜单。文件项右键始终 Select，右击已选项会清除其他选中项；需核对其余菜单和真实作用对象。
- 快捷键：Global 与 EntryTypePicker 两种可见 context；执行时 Global 与当前 context 同时参与，但 FindConflict 仅同 context，单项重置不检查冲突。Sample 的 InputBindingCapture 使用 tunnel 捕获与取消机制，但绑定 Player 数据模型，不能直接复用。
- Inspector：DockInspectorCoordinator 当前特意忽略没有 Inspector contribution 的辅助面板；多个 Inspector 及锁定通过 Host 独立跟随管理。Problems 需要显式贡献/选择通知，避免空列表改变目标。

后续阶段需补 GUI 观察与每项入口的保全/处理结果，尤其扩展 schema、损坏文件和文本入口。暂不能将本静态记录视为完整覆盖或 P0 验证通过。
