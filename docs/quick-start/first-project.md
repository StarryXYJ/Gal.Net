# 创建第一个项目

目标是完成一条短剧情的创作、检查、导出和运行。先[运行自带示例](run-sample.md)，确认环境能够工作，再使用同一套源码或发行包继续。

## 选择契约

Editor 默认新建项目使用随程序部署的默认契约；如果计划用 Sample 运行，创作项目应使用该玩家对应的契约闭包。默认 Editor 契约和 Sample 契约并不相同，不能等到合包时才忽略差异。

仓库自带的 `GalNet.Sample.Contracts` 输出固定入口 `GalNet.Project.Contracts.dll`。部署脚本见[契约与自定义指令](../manual/extensions/actions-and-contracts.md)；发行 Sample-Contracts 包则应与玩家版本完全匹配。

## 创建并编写

1. 打开 Editor，在新建项目面板选择空目录和项目名称；如需 Sample，选择已构建的对应契约目录。
2. 创建结果包含 Entry 与空 Opening Group。新建骨架不包含完整可运行剧情。
3. 在 Graph 中确认入口连接 Opening；打开 Group，添加文本条目并填写正文。也可先按 [GameTestCase 说明](../../GameTestCase/README.md#open-and-maintain-in-editor)部署 Sample 契约，再直接打开该目录，对照现有剧情、资源和双语文本。
4. 需要资源时从资源面板导入，使用资源 ID 选择，不把开发机绝对路径写入剧情。
5. 保存已提交的作者内容并检查当前内容。先处理错误，再进入预览或构建。

界面细节见[工作区](../manual/editor/projects-and-workspace.md)、[剧情编辑](../manual/editor/story-editing.md)和[资源管理](../manual/editor/resources.md)。

## 预览、导出和运行

Editor 的创作检查、预览与外部玩家调试用途不同；需要验证实际玩家时按[预览与调试](../manual/editor/preview-and-debugging.md)配置对应开发版宿主。

导出时选择完整 `.galpak` 或内容目录；剧情分包仍需包含完整集合。把内容与使用同一契约构建的玩家通过[游戏合包](../manual/publishing/game-distribution.md)组合，解压后从标题页开始游戏，并测试保存、读档与正常退出。

成功标准：玩家能进入新写的剧情，资源与文本正确加载，保存后重新打开可恢复。首次流程建议只用一段文本和一张图，确认闭环后再加入音频、翻译和复杂演出。
