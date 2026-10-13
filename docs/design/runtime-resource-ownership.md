# Runtime 与宿主资源的职责边界

## 执行与通用规则

Runtime 拥有变量桥接、语言查询顺序、已读索引和故事执行；宿主选择存储、文本 provider 与平台后端。PlayerVariableService 依赖 IPlayerVariableStore，GameTextResolver 接收文化对应的查询委托。文件内容加载和存档 I/O 位于外层，Runtime 的 loader 只解析已读取的内容。

Engine 是执行门禁和运行 scope 的唯一所有者。内部 ActivePrimitiveRegistry 管理实例订阅、顺序和 batch；GameGraphNavigator 索引本次运行的稳定编译图。音频领域状态由 AudioRuntime 拥有，AudioBackendDispatcher 与它共享同步锁，负责后端命令顺序及清理等待。

## 媒体文件依赖

Core.Assets 声明 IAssetFileMaterializer 与 AssetFileLease；GalNet.Assets 提供从 AssetManager 获取字节并创建本地临时文件的实现。GameView 增加对 Assets 的直接引用，用于音频、视频和 Gallery adapter 的默认 materializer 构造。宿主可注入其他实现；官方 Sample 在资源 scope 中创建一个实例并共享给三个消费者。

这个依赖只存在于平台展示 adapter，Core/Runtime 保持与实现解耦。资产解码与缓存仍由 AssetManager 管理；播放器持有文件租约，最后一个消费者释放后才删除文件。materializer 关闭时拒绝新请求并取消待加载操作，已有租约继续有效。Gallery 缩略图拥有独立抽取与缓存生命周期。

## 恢复与关闭

Builtins 的 PersistentSceneReplaySession 拥有恢复产生的展示 continuation；等待取消后仍保留未清理实例。宿主先停止 Engine，再释放重放会话、执行 view 和平台后端，最后释放资源 scope。

已读进度的内存更新与磁盘提交分开。文件实现默认每 2 秒提交 dirty revision；存档和停止边界显式等待 flush。持久化失败保留 dirty 状态并重试，关闭失败向调用者传播。强制终止可能丢失上次成功提交后的进度；持续 I/O 失败时没有最大丢失时间保证。

当前契约和行为分别见 [architecture](../spec/architecture.md)、[assets](../spec/assets.md)、[runtime](../spec/runtime.md) 和 [i18n](../spec/i18n.md)。
