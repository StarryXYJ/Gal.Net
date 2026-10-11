# F-20261011-03 Theme-owned views

状态：完成。用户授权先提交 F02，再清理 GameView 默认页面。

目标：GameView 保留状态、导航和共享行为；布局主题提供全部普通页面、工具栏与截图对话框布局。Sample、Editor 显式选择主题；开发者仍可覆盖单个 View。

验收：没有默认普通页面回退；注册时检查所有必需映射；GamePage 仍是运行时同一 Scoped 实例；三主题截图行为与页面可用；宿主覆盖优先；Presentation、Integration、Architecture 测试通过，Sample 与 Editor 构建通过。

不包含运行时主题切换、配色设计变更或核心运行时改动。

设计见 design.md，阶段见 phase-plan.md。
