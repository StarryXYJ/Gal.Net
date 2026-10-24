# 架构扫描必须排除嵌套构建输出

状态：confirmed
日期：2026-10-21
来源：F-20261019-01-editor-game-localization / P6；提交 c7c97b7

架构测试递归枚举 test 下的 csproj，曾把 test/GalNet.Editor.Tests/bin/te4c3b 中的临时仓库副本也当作源码；15 项依赖断言因此报告同一测试项目出现两次。逐条检查失败项目路径，确认重复项均位于 bin 内。

扫描发现源码项目时须排除路径任一段为 bin/obj 的文件，复用现有 IsBuildOutput 规则；不能只排除项目根目录直接子级，也不能靠删除用户或其他任务的临时产物维持测试通过。修正规则后保留原副本，45 项架构测试全部通过。

适用于递归工程发现；不改变构建工具对显式指定项目的处理。过程见 features/F-20261019-01-editor-game-localization/phase-6.md。
