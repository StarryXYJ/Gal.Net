# 分阶段计划

## Phase 1：组合契约与稳定 GamePage — complete

提供主题入口、默认→主题→宿主的注册顺序及自动 View 注册；模板化 GamePage 并保留行为。退出条件：Presentation 测试验证覆盖顺序、模板部件、核心控件 identity 与首条对白。

证据：GameThemeTests 验证主题/宿主优先级、Scoped View 复用、派生 GamePage 与 Runtime 别名 identity、损坏模板诊断，以及九种组合的真实 presenter/点击/选择/NVL 链路。

## Phase 2：内置布局、配色与全页面覆盖 — complete

新增 Themes 项目、三套布局与三套独立配色，接通 Sample。退出条件：全部组合能加载，所有共享页及启动/截图 UI 引用配色，Sample/Desktop 可构建，渲染检查布局。

证据：Classic/Minimal/Paper × Ocean/Rose/Mono 的标题、游戏、共享页面和截图界面 Skia headless 渲染通过；Sample 与 Editor 构建通过，Editor 使用独立 OutDir 避开正在运行的程序锁。

## Phase 3：回归与开发者文档 — complete

运行相关测试、架构与格式检查，修复本 feature 引入问题，同步 spec/README 和总结。退出条件：记录验证证据、已知限制、原有用户修改仍保留。

证据见 summary.md。已同步 control spec、开发者手册、Themes README 与 ADR，保留原有 Sample csproj DependentUpon 修改；运行期换主题、原生媒体及系统选择器未扩充范围。
