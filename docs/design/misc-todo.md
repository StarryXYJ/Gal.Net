# 杂项待办

本文件记录与游戏页面和游戏本体功能无直接耦合的工程性工作项。完成后移除对应条目，并在相关规范中留下最终行为。

## 条目编译层

- 将 `GalgroupCompiler` 返回的 source map 输出为可随构建产物分发的映射文件，供编辑器诊断、断点和跳转定位使用。
- 设计用户自定义复用非原语的定义与调用格式：类型化参数、局部句柄作用域、调用栈与递归/循环调用诊断。
- 支持 `animation.play.events` 中的非原语在同一帧展开为多个原语事件；在明确异步与阻塞语义前，继续拒绝此类输入。
- 将 `.rawgalgroup → .galgroup` 编译接入正式 `.galpak` 导出，而不仅是编辑器预览。
- 为旧项目从 `*.galgroup` 编辑源迁移到 `*.rawgalgroup` 提供显式迁移与旧文件清理，避免保存后遗留两份来源不明的内容。

## 资源定位统一

- 已完成基础运行时资源模型：`IAssetManager.LoadAsync<T>(guid)` 以 `GUID + 目标类型` 为缓存键，并维护引用计数与 `IDisposable` 资源释放；宿主通过 `RegisterDecoder<T>` 注册平台/领域类型的解码器。`SceneTexture` 的 Skia 解码器属于 Avalonia Rendering，Core 与 Assets 不依赖 Avalonia。
- 已完成 Sample 的场景预热：启动时异步获取所有 Sprite 的 `SceneTexture`，Layer 绘制只查询已加载的强类型资源；Session 结束统一释放 AssetManager 所有缓存。shader 文本同样走 AssetManager，已编译的 GPU shader program 由 renderer 在 effect runtime 生命周期内管理。
- `LoadByPathAsync` 与无类型 `Release(id)` 仅保留给旧工具和迁移逻辑；正式游戏内容应始终用 GUID，且新调用点应配对使用 `LoadAsync<T>` / `Release<T>`。
- 为 Headless / 已发布宿主接入与 Sample 相同的 AssetManager GUID 解析，并在加载阶段拒绝旧路径引用；Sample 已完成此迁移。
- `.sksl` 已纳入正式 `EffectProgram` 资源类型，AssetCatalog/文件命令会生成对应 `.meta`，资源选择和打包保留该类型。仍需让编译器在写入 `EffectProgramResource` 与 Layer 引用时强制验证 GUID；相对路径仅保留为开发诊断显示或明确的迁移输入，不能成为正式存档与已发布 galgroup 的依赖。
- 新项目模板预置效果资源属于后续独立工作，不是渲染器的内嵌资源。
