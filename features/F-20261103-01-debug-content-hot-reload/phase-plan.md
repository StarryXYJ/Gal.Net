# 开发调试内容热更新阶段计划

状态：planned。设计依据见 [design.md](design.md)。

依赖顺序：P1 → P2 → P3 → P4 → P5 → P6。每个 Phase 达到退出条件后单独提交，不混入工作区其他任务改动。实现只增加 Editor `DebugContentHotReloadService` 与客户端 `DebugContentRevisionService` 两个有状态协调服务；Runtime、协议和 UI 保持薄层。

## P1 — 不可变候选与纯差异分类（verified）

实现结果（2026-11-03）：调试准备改为发布 `Output/Debug/Revisions/<revision>` 不可变目录；清单记录内容/契约/作者身份、Graph 摘要、最终编译作者 span 与会话文件摘要。调试内容指纹纳入 Assets。纯分类器按稳定作者前缀区分 Direct、ReturnGroupStart 和 RestartGame，当前无法归属的 Graph/资源保持保守重开。

验证：`GalNet.Debugging.Tests` 16/16 通过；`ProjectContentBuilderTests` 5/5 通过；Debugging 与 Editor.Shared 定向构建成功（Editor.Shared 保留 29 个既有 analyzer warning，0 error）。

提交：`d8fb567 feat(debug): classify immutable content revisions`。

## P2 — 运行事实与 Runtime 原子换代（verified）

实现结果（2026-11-03）：Snapshot v6 保存自然进入的有序 Group ID；inspect 报告经过序列、入口缓存可用性和重复访问。Runtime 新增单一 `ReplaceContentAsync` 原语，以 Editor 提供的执行边界校验并整体替换不可变导航；活动 primitive 保持，候选后缀从映射位置继续。换代推进 generation 并清除旧断点/步进状态，不保存候选、影响等级或提示原因。

验证：`GalNet.Runtime.Tests` 129/129 通过；`DebugSnapshotFactoryTests` 与 `GameEnginePrimitiveDispatchTests` 相关集成 23/23 通过。另有定向 DebugSession/Lifecycle 47/47 通过。构建仅保留仓库既有 analyzer warning。

### 目标

Runtime 只提供 Editor 分类所需事实和一个原子导航换代能力，不保存候选、不分类、不管理 UI 状态。

### 任务

- `GameSnapshot` v6 增加按自然进入顺序保存的 Group ID；复制、存档、读档保持一致。
- 自然进入 Group 时追加 ID；Seek、回放和断点定位不追加；检测重复自然进入并报告首期不支持。
- inspect 报告当前 Group/index、活动 primitive ID、经过 Group ID、入口缓存可用性和重复访问。
- `GameGraphNavigator` 支持离线构造完整不可变候选并整体替换。
- `DebugSession` 增加单一换代原语：校验预期执行边界，替换导航并采用 Editor 给出的候选编译 index。
- 换代成功推进 generation，取消旧定位/追帧并失效旧 span 操作；失败保持旧状态。
- 删除当前草稿中 Runtime 的影响枚举、候选暂存和 pending reason 状态。

### 验证与退出条件

- Snapshot v6 的 Group 序列保存/恢复往返；Seek 不污染序列。
- 普通、活动 primitive、Composite 和 Group 尾部边界换代正确，不重复已执行 primitive。
- 预期位置变化、坏 Graph、缺 primitive 实现和旧 generation 均原子失败。
- 普通非调试运行回归通过。
- 测试、阶段证据完整后单独提交。

## P3 — 客户端版本服务与两段式协议（planned）

### 目标

由唯一的客户端 `DebugContentRevisionService` 管理 Latest/Run 两个版本，完成“始终安装最新版、按命令激活”。

### 任务

- 协议增加 `content.install`、`content.activate`、`content.activateAndSeekGroupStart` capability。
- inspect 返回 `LatestRevision` 与 `RunRevision`。
- 版本服务验证候选目录、项目/内容/契约身份、Graph 和 primitive 实现后更新 Latest；安装不改变 Run 或 generation。
- 新未激活候选覆盖旧候选时释放旧候选；失败保持两个版本指针不变。
- Direct 激活调用 P2 原语，同时切换当前运行的新资源获取来源。
- 返回开头操作串行完成“激活最新版 → 现有 SeekGroupStart”；不自行降级。
- 新 Run 默认绑定 LatestRevision。
- 超时后通过 inspect 对账；位置竞态只拒绝，不重新分类。

### 验证与退出条件

- 安装、覆盖未激活候选、失败回滚和身份拒绝。
- Latest/Run 分离及 Direct 激活后的统一切换。
- 返回开头使用旧入口状态执行最新版 Group。
- 新游戏采用最新版，旧运行在此之前保持完整旧版。
- loopback 协议、断连、响应丢失和执行位置变化测试通过并单独提交。

## P4 — Editor 协调服务与 UI（planned）

### 目标

由唯一的 Editor `DebugContentHotReloadService` 完成构建、安装、分类、激活、提示和源码映射提交。

### 任务

- 服务串联现有构建器、调试连接、P1 分类器和 P3 协议。
- 每个成功构建先安装最新版，再根据实际 RunRevision 与 inspect 状态分类。
- Direct 自动激活；Return/Restart 维护 Editor 本地待处理状态和命令。
- Editor Toast、顶部菜单及调试面板绑定同一状态；客户端 App 不增加相关 UI。
- 返回命令调用 `activateAndSeekGroupStart`；重新开始调用既有新游戏入口。
- 只有 RunRevision 确认切换后提交源码高亮、定位和断点映射。
- 连续构建使用作者签名/请求序号淘汰迟到结果；后续保存可升级、降低或解除要求。

### 验证与退出条件

- 未来 Group、当前后缀、当前前缀和历史 Group 四类真实闭环。
- Toast、菜单状态和命令启用均来自一个服务状态。
- 构建失败、安装失败、激活竞态、超时对账不造成 Editor/客户端版本分裂。
- 客户端工程不存在影响分类或本功能提示文案。
- Editor、协议和真实 Developer Sample 验证通过后单独提交。

## P5 — 资源与会话内容生命周期（planned）

### 目标

扩展同一个客户端版本服务，使新加载读取 RunRevision，旧持有对象按简化生命周期正常结束。

### 任务

- 每个已验证 revision 持有完整 `GameResourceScope`，不增加独立资源协调服务。
- 激活时切换新 Acquire、文本解析和文件物化来源；已有 handle/媒体对象继续使用旧对象。
- 曾成为 RunRevision 的旧 scope 保留到当前游戏结束统一释放；不做逐句柄退休。
- 设置小的退休版本上限；达到上限时拒绝 Direct 激活并返回明确错误，由 Editor 转为重开提示。
- 新游戏使用 LatestRevision 重建 Gallery、预载、文本和项目级会话对象。
- 只能在 App 启动时建立的内容明确要求重启调试 App。
- P1 清单补齐普通资源使用位置，使 Editor 而非客户端决定资源影响等级。

### 验证与退出条件

- 同资源 ID 的旧对象继续有效，新加载得到新版。
- 图片、音频、视频和文本覆盖 Direct、返回开头及新游戏路径。
- 未激活候选及时释放；已激活旧 scope 在游戏结束统一释放；版本上限有界。
- 会话级资源重建及 App 重启边界明确，测试通过后单独提交。

## P6 — 完整验收、正式文档与收尾（planned）

### 目标

覆盖竞态、生命周期和真实 Sample，确认简化职责没有隐式第二套状态机。

### 任务

- 覆盖停止、读档、返回标题页、断开、项目关闭、连续保存和进程退出时的取消/释放。
- 验证 Snapshot v6、协议版本和发行 Player 不获得热更新入口。
- 执行真实窗口流程：Direct、返回 Group 开头、重新开始、资源更新、失败重试和断点/高亮。
- 检查不可变版本目录、scope、日志和后台任务有界清理。
- 同步 Runtime/Editor spec 与 Editor 操作手册。
- 生成 `summary.md` 和独立 `review.md`，如实记录未验证平台或媒体边界。

### 退出条件

- feature 验收标准均有自动或人工证据。
- 完整相关回归通过，或环境限制有明确证据和用户决定。
- 正式文档、总结、review 和逐阶段提交齐全。
