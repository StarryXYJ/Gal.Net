# 游戏布局主题与配色

状态：complete。用户于 2026-10-11 明确授权开始实现；实现、回归和开发者文档已完成，验证边界见 summary.md。

## 已确认需求

- 官方内置布局主题集中在独立 GalNet.Avalonia.Themes 项目，提供普通页面 View 和控件主题。
- 配色与布局独立组合，配色引用基础色表并暴露统一语义资源。
- 组合顺序：默认页面 → 所选主题页面 → 开发者页面覆盖；最终注册表不可变。
- GamePage 为稳定可模板化宿主；主题负责布局，共享层保留游戏行为。
- 开发者在启动代码/XAML 配置，第一版无动态主题切换、玩家变量绑定或运行期选择器。
- 覆盖共享玩家页面和 Sample 启动、首次语言选择、截图弹窗；调试 UI、编辑器与系统文件选择器不在范围内。

## 验收

三套布局不同的内置示例及独立配色可构建和展示；页面覆盖可单独注册；GamePage 的首条对白、NVL、选择、输入和截图能力不被模板化破坏；资源与页面覆盖有确定优先级；开发者文档包含配置与局部覆盖示例。

## 约束

Core/Runtime 不依赖 Themes。保护原有 Sample csproj 的 DependentUpon 修改。采用普通分阶段流程，因为影响公共模板契约与多个页面；不再压缩为快速通道。

## 文档

- [design.md](design.md)
- [phase-plan.md](phase-plan.md)
- [review.md](review.md)
- [summary.md](summary.md)
- [长期设计](https://github.com/StarryXYJ/Gal.Net/blob/79df06fd7a55084c552860b167bca0f38a650f69/docs/design/game-ui-themes.md)

默认示例选择 Classic + Ocean，另提供 Minimal、Paper 布局与 Rose、Mono 配色；名称属于实施默认值，不增加玩家选择流程。
