# 定制 Avalonia Sample

`GalNet.Sample.Avalonia` 是项目级组合根示例：保留启动参数、项目选择、本地化和调试策略；通用会话、页面与展示组合应优先复用 `Avalonia.GameHost`、`Avalonia.GameView` 和 Themes，而不是复制其生命周期代码。

## 推荐改造顺序

1. **建立项目契约。** 复制或引用 `GalNet.Sample.Contracts`，在 `ProjectSchemas.Modules` 中列出项目允许的 `IEntryModule`。构建后用 `scripts/build-project-contracts.ps1` 部署到内容项目的 `Contracts/`。CLI/Editor 与玩家必须消费同一契约集合。
2. **配置运行组合。** 在 Sample 的 `SampleCustomization` 中配置 catalog、执行模块、资源/玩家存储、音频 host 和项目启动策略。把项目专属 primitive 注册在这里；不要修改 `App`、Session 或关闭链来接入一条业务能力。
3. **定制外观。** 通过 `AddAvaloniaGameViewPages` 选择 `ClassicTheme`、`MinimalTheme` 或 `PaperTheme`，也可在 `configureViews` 覆盖单个页面。运行期主题切换不受支持。
4. **扩展项目功能。** schema/DTO 与 composite 放在契约项目；执行模块放在 Avalonia 或项目执行程序集。参数 DTO 用 `GenerateFieldSchema`/字段特性生成 descriptor，平台控件仅在页面/设置工厂中处理。

## 页面与主题边界

- `GamePage` 是稳定的游戏页面宿主；其模板必须保留 `PART_Scene`、`PART_Dialogue`、`PART_Nvl` 和 `PART_Choice` 四个 ContentPresenter。
- 对话与 NVL 的内部部件分别需要 `PART_Typewriter`、`PART_Lines` 和 `PART_ScrollViewer`。不要在剧情播放中重建这些部件。
- 主题页面复用共享 ViewModel 和命令；只替换布局/样式，不复制 presenter、媒体或游戏会话逻辑。
- Palette 使用完整的语义资源字典。宿主若覆写资源，以玩家根的资源为准；不要跨多个根复用同一 `Styles` 实例。

## 上线前检查

- Project `Contracts/` 是与当前 GalNet API 匹配的可信构建产物；重新构建游戏或 CLI 不会自动更新该目录。
- 自定义 entry 已在 CLI 校验/编译、Avalonia 运行和保存恢复路径中测试；Headless 缺少平台实现时应清晰报错。
- 主题的普通页面映射完整，`GamePage` 布局可在目标分辨率和输入方式下工作。
- 退出、切换项目和读档均由会话链路释放旧的 presentation、媒体与资源。

主题的可复制示例在[Themes README](../../src/Presentation/GalNet.Avalonia.Themes/README.md)；运行与页面的精确约束见[Avalonia 游戏页面规范](../spec/control.md)。
