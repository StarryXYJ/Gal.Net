# 文档维护与 Wiki 同步

## 维护位置与职责

正式正文只在 `docs/` 维护。Quick Start 完成最短成功流程；manual 描述实际操作与扩展；spec 描述当前实现的契约；philosophy 解释高层原则；glossary 统一术语。

`features/` 保留需求、设计候选、过程与验收记录，`.agents/` 保留 agent 上下文和经验。模块 README 保留局部入口，通用长教程链接回正式手册。Wiki 是正式文档的发布副本，包含完整正文、分类页面、侧栏和返回上级链接。

## 修改与检查

新增页面先填写正文与 H1，再在 `docs/navigation.json` 中加入 source、中文 title 和唯一稳定 id。分类父子关系与顺序以这份配置为准；移动源文件时尽量保留 Wiki ID，以维持现有 URL。分类 README 的自动导航区块由脚本更新，区块外说明可以手工维护。

从仓库根目录执行，要求 Python 3.11 或更高版本，无第三方包依赖：

```powershell
python scripts/docs/sync_wiki.py indexes
python scripts/docs/sync_wiki.py check
python -m unittest discover -s scripts/docs/tests -v
```

检查包括页面覆盖、ID/标题、导航与本地链接及标题锚点。代码块和行内代码中的链接示例不作为真实导航。外部网站是否在线、示例命令能否在特定设备运行仍需相应验证。

文档更新应核查实际源码、适用测试、CLI 帮助和运行记录。目录移动不算完成内容核查，不能把 feature 中计划或单个构建成功写成平台支持承诺。

## 本地生成与发布 Wiki

先克隆独立 Wiki 仓库到根目录 `wiki/`，该目录被主仓库忽略。首次初始化需在 GitHub 创建 Wiki 首页。

```powershell
git -C wiki pull --ff-only
python scripts/docs/sync_wiki.py sync
git -C wiki diff --stat
git -C wiki status --short
```

脚本只生成，不提交或推送。正文链接转换为 Wiki 页面 ID；源码/feature 链接指向主仓库来源 commit；`docs/_assets/` 下被引用的附件复制进 Wiki。生成前逐页核对正文和导航是否与来源 commit 相同，拒绝把未提交内容标记为已提交快照。先提交主仓库文档，并在公开发布前保证来源 commit 可从 GitHub 访问；不要把未推送的本地源码来源链接当作已公开可读。

检查生成结果后在 Wiki 仓库单独提交并推送：

```powershell
git -C wiki add --all
git -C wiki commit -m "docs: sync documentation from source repository"
git -C wiki push
```

`.galnet-wiki.json` 记录受管文件与生成 hash。同步会拒绝被手工修改/删除的生成文件、同名非受管文件或已知的远程分叉，只删除清单拥有的过期输出。默认初始 Home 欢迎语可自动替换。发生冲突时先保存并核对修改，再回到 docs 修正文档；不要删除管理清单来强行覆盖。

侧栏表达与本地一致的分类树。Wiki 文件采用稳定 ID 平铺存储，避免多个 README 重名，GitHub 默认 Pages 列表不承担目录树导航。

`.github/workflows/docs.yml` 在文档相关 PR 中执行检查并生成 Wiki 预览 artifact；master 的相关 push 也运行。配置仓库 secret `DOCS_WIKI_TOKEN`（具备 Wiki 仓库写入权限）后，master 更新可自动发布；手动运行默认只检查，勾选 publish_wiki 才请求发布。PR 不接触发布凭据。

没有正确凭据时仍执行检查并保留预览，明确显示未启用发布。Wiki 克隆/推送由 Git 完成，不通过把 Wiki 当成普通 GitHub API 仓库查询；不能假定默认主仓库 token 能写 Wiki。本次整理按用户选择只在本地提交和生成，远程发布另行进行。
