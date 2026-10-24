# P3a：移除旧角色兼容入口

状态：verified；2026-10-20，按用户“不考虑向后兼容，删除所有向后兼容部分”的最新要求调整本 feature。

删除 ProjectSettings.Speakers 及设置快照复制项；删除旧角色候选/迁移服务、人物表迁移区、四语迁移文案、废弃 speaker 失焦回调和对应测试。角色身份仅以 catalog 为准，未知 ID 只产生 character.unknown，不将存储值解释为姓名。现有设置/保存/深拷贝测试改用仍有效的设置字段，保留原事务与快照断言。

验证：Editor 定向 18 项、Shared 文档/事务 17 项，共 35 项通过；Editor.Tests 及 Shared.Tests 构建通过。全解决方案首次构建因同时变动的 SceneAuthoringIdentities.IsOutput 暂缺失败，对应场景实现补齐后受影响项目重建成功；未修改或提交其他任务的场景变更。日志与 TRX 在 Temp/localization-p3a-*。

生产与测试 C#/XAML 不再引用 Speakers、CommitSpeaker、LegacyCandidates 或 ImportLegacy。默认语言文本、嵌套 DTO、未知扩展字段保护仍是当前格式的作者文档规则，不作为旧版本迁移入口处理。

需求/设计/spec 已同步；P3 原记录仅作为历史。完成后继续 P4。
