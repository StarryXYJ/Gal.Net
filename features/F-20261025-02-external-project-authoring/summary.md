# 实现总结

## 已实现

- 新增 `project apply`：版本化 manifest、原/新文件 SHA-256、dry-run、完整候选校验、资源/sidecar 配对、删除后的静态 JSON 引用保护，以及同一作者事务中的多文件发布/补偿。结果明确 `referencesComplete=false`，不宣称覆盖自定义代码或媒体解码。
- 新增通用外部内容同步：后台读取稳定候选，等待外部事务完成，在同一项目 scope 中更新 Graph、Group、settings、本地化和受管理文本；文件监听配合周期复核，不依赖修改源是 AI。
- 无本地未保存内容/输入时自动采纳。有本地冲突时保留内容，提供重新检查和明确放弃后的重新加载；坏 JSON/不安全结构保留旧会话。资源刷新与文件面板也等待外部事务。
- 保留已有 Group、节点和相同 Entry 身份，结构插入后更新行号并保持选择；采纳推进全部保存基线并清除旧 Undo/Redo。自己的保存不会成为外部历史边界。
- 待提交输入可取消；组字/异步输入未能取消时继续阻止加载。已打开文本支持外部编码变化，变为二进制时只读且保留真实字节，恢复文本后可继续编辑。
- CLI 携带 `Skills/galnet-project-authoring`，包含查询/生成/应用/Git 工作方式及文本转 Group、补演出、导入立绘与差分的说明；不绑定模型提供商。

## 设计取舍与限制

- 以整个项目判断本地冲突；当前没有自动三方合并或“只合并无冲突字段”的第二套状态机制。
- 外部采纳是历史边界，成功外部修改的长期回退依赖 Git 或用户备份。CLI 事务只负责失败补偿，不提供持久 Undo。
- 契约 DLL 变化要求重新打开项目。已启动的游戏预览不自动重演；普通作者内容及资源刷新不关闭项目。
- 任意外部工具未提供提交协议时，多个文件保存无法被推断为原子批次；CLI 使用事务 manifest 提供明确的发布边界。中断事务保留恢复材料，不自动覆盖未知外部内容。
- 大项目扫描/长媒体导入性能和原生 IME 人工验收未执行。候选验证不复制既有媒体负载，也不解码新媒体。

## 验证

- Shared 全回归 417 项通过；最终原生场景/批次与热重载定向 19 项通过。
- Editor 相关回归初轮 30 项通过，包括实际挂载 Group 的文件监听、结构插入后的选择/行号保留、设置/文本草稿保护、明确放弃后的继续编辑，以及历史和四语资源。
- 最终 Editor 相关 36 项通过，新增外部资源事务通知与项目 schema 生命周期覆盖；二进制/文本来回切换保持真实字节并恢复可编辑性。最后一次仅调整文件面板异常捕获位置，重新构建后文件面板/资源通知 4 项通过。最后修正 CLI 文字帮助的 manifest 标签，Headless 构建与机器/文字帮助实际调用通过。
- 实际 CLI 使用项目 DefaultContracts 预览/应用带角色、开发语言对白、等待演出及图片/metadata 的场景，并成功定向编译。没有调用真实外部模型或图像生成服务，不把该证据描述为生成质量验收。
- Shared/Headless 构建通过；Editor 使用独立 `artifacts/external-authoring/` 输出验证，未关闭用户进程。首轮普通输出构建遇到运行中进程的 DLL 锁定，之后采用隔离输出。
- Skill 的 `quick_validate.py` 通过。沙箱中的 testhost 连接超时后，测试在允许本机测试进程通信的环境中通过。

TRX 位于 `artifacts/external-authoring/{shared-results,editor-results,final-results}/`。仓库原有其他本地化改动保留，未提交或重置用户工作区。

## 正式文档

- [使用手册](../../docs/manual/editor/external-tools.md)
- [历史与保存](../../docs/spec/editor/history-and-save.md#外部内容热重载)
- [文件操作](../../docs/spec/editor/project-and-files.md)
- [CLI](../../src/Editor/GalNet.Editor.Headless/README.md)
- [产品创作 Skill](../../skills/galnet-project-authoring/SKILL.md)
