# 外部工具创作与热重载

Codex、Claude 等外部工具可以通过 GalNet CLI 读取项目契约和修改项目。CLI 不包含模型服务；创作 Skill 随 CLI 分发，Editor 观察普通文件变化，不识别或依赖 AI 来源。

## 准备 CLI 与 Skill

源码中构建 `src/Editor/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj`，使用生成的可执行文件，或 `dotnet <GalNet.Editor.Headless.dll>`。需要对应的 .NET 10 运行环境。构建/发布目录包含 `README.md` 和 `Skills/galnet-project-authoring/`；将后者提供给所用工具的 Skill 机制。源码入口在 [Skill](../../skills/galnet-project-authoring/SKILL.md)。

任务中明确目标游戏项目目录和 CLI 路径。可用 `GALNET_CLI` 环境变量向 Agent 提供路径；它是 Skill 的发现约定，不是 CLI 自身必需的配置。

```text
<cli> help --json
<cli> project info <project>
<cli> schema list <project>
<cli> schema show <project> <type-id>
<cli> help group.entry.patchParameters --json
<cli> help project.apply --json
```

参数契约由机器帮助给出。小改动使用 `execute` 的文档批次；一次生成 Graph、Group、本地化、立绘和 sidecar 时使用 `project apply`。

## 批量应用原生文件

在 `Temp/` 或项目外的暂存目录准备新文件和版本 1 的 manifest。source 相对 manifest 所在目录，path 相对目标项目；两者均拒绝路径逃逸和链接。原生剧情源是 `.rawgalgroup`，可运行 `.galgroup` 由编译器产生。

```json
{
  "version": 1,
  "writes": [
    {
      "path": "Graph/groups/intro.rawgalgroup",
      "source": "intro.rawgalgroup",
      "contentHash": "<暂存文件的64位十六进制SHA-256>",
      "expectedHash": "<开始生成前原文件的SHA-256>"
    }
  ],
  "deletes": []
}
```

新增目标使用显式 `expectedHash: null`，表示目标必须不存在。删除项包含 `path` 和非空 `expectedHash`。哈希大小写均接受；例如 Python 的 `hashlib.sha256(path.read_bytes()).hexdigest()` 或 PowerShell 的 `Get-FileHash -Algorithm SHA256` 可计算。expectedHash 应在生成前捕获；发生冲突后需要重新核对内容，不能只替换为最新哈希来跳过冲突。

```text
<cli> project apply <project> <manifest-file> --dry-run
<cli> project apply <project> <manifest-file>
<cli> validate <project>
<cli> group compile <project> --id <group-id> --dry-run
```

dry-run 在临时目录验证最终候选，不写项目文件。正式应用重新检查原文件哈希和文件集合，再通过同一作者事务发布。资源大文件以文件复制参与事务，不嵌入 manifest JSON；候选校验只建立媒体存在性，不复制或解码既有媒体载荷。报告沿用 validate 的完整性和 skippedChecks，不把参数正确当作图片/音频可用的证明。

资源载荷和 `.meta` 必须同时存在或同时删除；替换已有 metadata 保留资源 ID。删除资源、文件或 Graph 节点后，剩余静态 JSON 引用需在同批更新。自定义 C# 和运行表达式仍不保证完整发现。固定 Graph/settings 入口允许显式替换，不允许删除；Contracts、Output、Temp、`.galnet`、Git/IDE 内部路径不允许作为修改目标。

失败由作者事务补偿；异常退出留下的事务材料位于 `.galnet/editor/transactions/`，恢复规则见[历史与保存](../spec/editor-history-and-save.md)。它不承诺跨进程多文件隔离或断电整体原子性。

## Editor 保持打开

保存当前编辑后运行外部工具。Editor 合并文件通知并定期复核，在候选结构有效、事务完成且当前项目没有未保存作者内容/待提交输入时，直接更新当前项目会话。Group 页面、稳定身份的选中条目和布局保留；删除的 Group 页面关闭。资源通过既有索引代次刷新，已打开的普通文本也会更新。

外部版本采纳后，保存基线推进，旧 Undo/Redo 清除。Editor 的下一次编辑和 Save All 使用新基线；外部结果的长期回退通过 Git 或用户备份。

有本地未保存内容或输入时，外部变化提示会保留双方版本。可在外部核对/还原磁盘内容后点“重新检查”，也可点“从磁盘重新加载”，确认放弃当前作者修改及待提交输入。已应用内容会保留恢复快照；该快照不保存控件中尚未提交的原始输入。进行中的输入法组字或异步输入不能安全取消时，仍等待输入结束。

坏 JSON、稳定身份/结构不合法或来源仍在变化时保留当前会话，显示原因并重试。项目契约 DLL 改动要求重新打开项目，不能把新 DLL 混进旧契约 scope。已经启动的游戏预览不自动重演剧情，需要重新启动预览以运行新内容。

## 常用创作任务

- 文本转剧情：指定剧本、目标项目和期望分场，读取契约后生成 Group/Graph 与开发语言文本。`LocalizedText` 字段存 key，原文写入相应 I18n 文件；新 speaker 使用角色 catalog。
- 补演出：保持对白 Entry ID、文本 key 和原文，添加出入场、表情、位置、过渡、等待及音频等契约支持的行为。
- 生成/导入立绘：图像工具产出文件，再把图片、metadata、可选 variants 描述及剧情引用放入同一批次。图片质量与角色一致性由预览检查。

在 Git 项目里先检查已有修改，再检查 AI 完成后的 diff。提交、建分支或回退沿用用户工作方式，避免覆盖无关未提交内容。Git restore/revert 造成的文件变化也会热重载。无 Git 时 CLI 仍可使用，但成功修改之后的回退需要用户已有备份。
