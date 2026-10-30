# 文档整理与 Wiki 本地同步交付

## 结果

正式文档从 36 篇平铺说明整理为 72 页正文及分类入口。docs 是唯一正文维护源，Wiki 生成完整正文和树形侧栏；两处按相同读者分类与技术职责导航。保留术语表、理念和历史过程，补齐 Quick Start 与 Sample 使用入口。

已修正当前源码中可核实的过时说明，包括共享 Sample.Player、CrossPlatform 实际接入、Dock 公开泛型贡献、资源待领取引用与有界解压、当前文档包配置，以及与已实现 Editor 能力矛盾的旧阶段说法。逐页归属、核查入口与未重跑的验收范围见 [audit.md](audit.md)。

## 导航与维护

- `docs/navigation.json` 定义唯一分类树、中文标题、顺序、源文件和稳定 Wiki ID。
- `scripts/docs/sync_wiki.py` 提供 indexes/check/sync，校验正文、仓库引用、生成链接和来源 commit，转换附件及外部源码链接。
- `.galnet-wiki.json` 管理生成输出，保护手工修改、未受管文件和删除边界；生成不提交、不推送。
- `scripts/docs/tests/` 覆盖 11 项关键行为；`.github/workflows/docs.yml` 提供 PR 校验、预览 artifact 和受独立凭据控制的 master 发布。
- 40 条原先已删除设计/ADR 的历史引用改指其 Git 历史原文；历史记录不改写成当前规范。

日常操作见[文档维护](../../docs/manual/development/documentation.md)，读者入口见[文档首页](../../docs/README.md)。

## 验证

- 文档与导航检查：72 页通过，仓库指向 docs 的引用和生成后的 Wiki 页面/fragment 检查通过。
- Python 标准库测试：11/11 通过。
- .NET SDK：10.0.401；已有 Debug CLI 的帮助、带契约项目创建及结构校验均成功，无 diagnostics。
- 首次 Wiki 来源正文提交为 `291313bdfdec3c6bca6c1b99774bcb19c92adf52`；72 页正文加侧栏/页脚及管理清单共 75 个文件，重复生成所有输出字节一致。
- 首次 Wiki 本地提交：`53b0d08a71a31d25108637dd0d7d3017647e3c3e`。用户随后要求在当前 dev 本地提交，Wiki 据此重新生成并单独本地提交，两边仍不推送。
- 未重跑完整产品测试、GUI 用户流程或设备矩阵；未执行远程 Actions、线上 Wiki 或发布凭据验收。

## 交付边界

用户选择“主仓库与 Wiki 一起待发布”，本轮不 push。发布前应确保其他任务的对应源码/验收已经提交，主仓库来源 commit 可在 GitHub 访问，再推送 Wiki。DOCS_WIKI_TOKEN 的实际可用性仍需首次远程启用时确认。

本次只提交本 feature 的文档、链接修复、同步脚本和验证；已有 Sample/跨平台/资源源码、测试与其他 feature 状态继续由对应任务维护。

## 本地分支与后续整合

首次文档实施提交保存在 `codex/documentation-wiki` 分支，基线为 `695c6631e4f7bbc64c5105787b1b7f2019e1aa80`。为保护另一任务当时的 124 文件暂存，使用独立临时索引封存。该分支保留作首次交付记录。

2026-10-30 用户随后要求先本地提交 Git：将本次文档与引用改动提交到当前 dev，其他任务的 benchmark 和计划改动不混入；Wiki 使用新的 dev 文档提交生成来源记录。因此正常重新生成使用：

```powershell
python scripts/docs/sync_wiki.py sync
```

Actions 文件已进行本地内容审阅；尚未执行托管 runner，不能将本地 Python 检查等同于远程 workflow 通过。
