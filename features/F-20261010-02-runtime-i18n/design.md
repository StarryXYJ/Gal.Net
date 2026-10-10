# F-20261010-02 设计：游戏文本国际化与 DynamicLocalization

**状态：Proposed**

## 结论

首期不按 galgroup 动态装卸翻译，也不为每个 group 增加 i18n 文件 ID 列表或独立 i18n 包元数据。游戏启动时一次性、递归读取包内 `I18n/` 的 JSON，并按确定性顺序逐个交给 DynamicLocalization 动态加载；外部目录的 JSON 在其后加载，以库的覆盖语义覆盖同 culture、同 key 的内置值。

这让 `ITextResolver` 始终能按照当前 culture 解出任意已加载 group 的 key，避免进入 group 时异步加载、离开 group 后丢失已显示文本、以及保存/读档时 catalog 状态不一致的问题。文本 JSON 很小，按 group 拆文件只服务于作者组织和 diff，不影响运行时载入策略。

DynamicLocalization 仅在 Sample 的宿主层使用：它拥有 culture 切换和 provider 注册，Sample 自有 Avalonia 12 `ReflectionBinding` 适配层负责 UI 绑定。Core/Runtime 继续只依赖 `ITextResolver`。`DynamicLocalization.Avalonia` 2.1.2 虽声明兼容 Avalonia 11–12，却在运行时调用了 Avalonia 12 已移除的 Binding 构造函数，因此不使用该 XAML extension。宿主把同一个文化服务同时用于 UI 与 Runtime adapter，因此外部 JSON 可以覆盖 UI 与游戏文本；键命名空间避免意外碰撞。

## 当前事实

- Runtime 已有 `ITextResolver.Resolve(string)`；未提供实现时会回显输入 key。
- `GameContent` 目前只有 Graph、Assets、Gallery 与 ProjectSettings；两个内容 provider 尚未读取 `I18n/`。
- 导出器与内容构建器已递归复制 `I18n/`，并把它作为非资源的特殊内容随 `.galpak` 分发。
- `ProjectSettings.Speakers` 是 Editor 自动补全用的显示名字符串列表，不是人物表；当前 `dialogue.text` 的 `speaker` 参数也是字符串。
- Sample 的玩家变量独立于存档槽，保存到 profile 目录的 `player-variables.json`；有游戏目录时默认 profile 为 `<game>/.galnet`。

## 目标与边界

本设计覆盖：

- Avalonia Sample UI 的嵌入 JSON、本机语言选择与运行时切换。
- 游戏包中人物表、人物名称、galgroup 对话和分支选项的键化文本。
- 包内内置 JSON 与安装目录外部 JSON 的递归合并、回退和覆盖。
- 后续宿主注册自定义文本源的扩展点。

本设计不覆盖：Editor i18n 迁移、翻译模板导出、Editor 翻译编辑器、图片/语音/图层等二进制本地化、网络下载与热更新协议。

## 统一键空间与文件布局

所有本地化文件保留 DynamicLocalization 的 JSON 格式和 `.json` 扩展名；不采用 `x_zh-CN.png` 一类资源后缀约定。建议键空间为：

```text
ui.<area>.<name>                         Sample UI
character.<character-id>.name            人物显示名
group.<group-id>.entry.<entry-id>.<field> 对话/NVL 等条目字段
branch.<branch-id>.option.<index>.text   选项文本
```

项目内置文件按 culture 集中而按内容分片：

```text
I18n/
  catalog/
    characters.json
  zh-CN/
    characters.json
    groups/
      intro.json
      chapter-01.json
  en-US/
    characters.json
    groups/
      intro.json
```

`I18n/catalog/characters.json` 是不随语言重复的 character catalog；它只保存角色 ID 与名称 key。`I18n/<culture>/characters.json` 才保存各 culture 的名称文本。每个文本 JSON 是普通平铺或嵌套 key/value JSON；其 culture 由目录名提供，文件名不承载 locale。`groups/<group-id>.json` 是作者组织约定，不是 Runtime 必需的引用关系。导出器已经递归复制 `I18n/`，不需要将这些文件写入资源 PAK。

Sample UI 使用程序集嵌入的标准 DynamicLocalization JSON，例如 `Localization/zh-CN.json`；它和游戏的 `I18n/` 不共享物理位置，但在运行时合并到同一键空间。约定 `ui.` 只由 Sample UI 使用，游戏文本绝不占用该前缀。

外部覆盖目录建议由安装器在解压后创建为：

```text
<installed-game>/I18n/External/<culture>/**/*.json
```

它与包内文件同样按 culture、递归读取。安装/更新必须保留该目录；若未来安装器以重建安装目录的方式更新，则应在重建前保留并恢复 `I18n/External`，而不是把第三方翻译与包内文件混放。

## 人物表

新增游戏内容的 character catalog，而不是把 `ProjectSettings.Speakers` 扩展成隐式人物表。最小人物记录仅包含：

```text
CharacterId       稳定、项目内唯一，供剧情引用
DisplayNameKey    必填，例如 character.alice.name
```

`dialogue.text`、`dialogue.textVoice` 等文本条目的 `speaker` 参数存储 `CharacterId`。Runtime 或 built-in dialogue 模块在调用 presenter 前解析人物 ID 到 DisplayNameKey，再经 `ITextResolver` 解析成显示名。缺失人物 ID 应为内容验证错误，而不是把 ID 当名称静默显示。

旧内容的 speaker 显示名需要由作者显式迁移成人物 ID，并建立姓名 key；不自动把显示字符串当人物记录。内置对话实现拒绝未知非空人物 ID。人物 catalog 在内容层加载并由宿主传入模块，文件读取留在 Phase 3；Editor 自动补全的接线在内容加载完成后处理。

## 条目 key 的稳定性

每个 group 的 entry 在编译前后都有表示顺序位置的 `Entry.Id`；组合条目展开会增加 entry 数量并重新编号。`content` 在 raw 中已经是完整 i18n key，编译器与 Runtime 原样传递，禁止根据执行位置重新生成。

未来 Editor 自动生成 key 时，使用 raw 阶段的 ID 作为 `group.<group-id>.entry.<raw-id>.<field>` 的一段；`sourceEntryId` 仅指生成时的输入，不新增持久化属性。key 一旦保存就独立于顺序位置，后续重排和复合展开不改变它。现有序列化的 raw string `id` 与 `<raw-id>#<展开序号>` 编译 envelope ID 继续保留原协议，不承担运行时文本解析职责。

## 哪些字段是 key

不能把任意 `string` 参数自动视为 i18n key：许多 entry 的字符串是资源 ID、变量名、表达式、轨道 ID 或扩展自己的协议值。内置 gameplay schema 必须显式声明可本地化字段：人物显示名经 character catalog 解析；`dialogue.text` / `dialogue.textVoice` 的 `content`、NVL 内容和 branch option 文本均为 key。`speaker` 仅是 CharacterId，不是文本 key。

自定义 entry 不受这套内置字段约定约束：其参数 schema 只负责描述参数形态，是否把某个参数视为 i18n key、如何解析和何时解析，完全由该 entry 的 implement 决定。框架不对自定义字符串做推断或转换。这样“游戏文本一律是 key”的规则只约束内置 gameplay 文本字段，不会破坏资源和插件协议。

## 加载、覆盖与回退

启动某游戏时，宿主的游戏 i18n 加载协调器（名称为设计建议）按如下顺序把每个 JSON 单独交给 DynamicLocalization：

```text
embedded Sample UI JSON
        -> package I18n/<culture>/**/*.json
        -> external I18n/External/<culture>/**/*.json
        -> DynamicLocalization culture service
        -> UI binding / ITextResolver adapter
```

合并顺序固定：

1. embedded Sample UI；
2. 包内游戏 JSON；
3. 外部 JSON。

同一层内部按相对路径的 ordinal 顺序加载，后加载者覆盖先加载者；后一层覆盖前一层。这使外部 JSON 自然可以覆盖 `ui.*` 和游戏 key。加载错误的 JSON 不加入文化服务，宿主可以记录诊断但继续以其余来源启动。

文本解析顺序固定：请求 culture → 父 culture → `ProjectSettings.TargetLocale` → 原 key。默认语言必须包含所有运行时必需的游戏 key；内容构建/验证应检查该不变量。Runtime 不承担 culture 查找或文件 I/O，adapter 负责这一逻辑。

DynamicLocalization 的逐文件动态加载与重复 key 的覆盖语义必须在引入选定 NuGet 版本时以 characterization test 确认。首期不在 GalNet 侧预合并 JSON，也不依赖未定义的 provider 注册或文件系统枚举顺序；加载协调器显式排序并逐个调用该库的加载 API。若该库版本不能表达后加载覆盖语义，才退回一个内部聚合 adapter。

## 为什么不在载入 galgroup 时动态载入

候选方案是每个 group meta 保存 JSON 文件 ID，进入 group 时注册相应 provider，并在离开后卸载。首期不选它，因为：

- `ITextResolver` 只接收 key，不带 group 上下文；选择、历史 NVL、回放和异步展示可能在 group 切换后仍需旧文本。
- provider 注册/卸载造成 culture 切换与读档重放的时序状态，需要额外可存档或可重建的加载集。
- group 文本通常远小于图像、音频和视频；一次性载入更可预测，且与当前递归复制整个 `I18n/` 的发布模型一致。

因此 group 的 i18n 文件不需要在 `.galgroup` meta 中保存 ID。未来若有测得的文本规模问题，可新增按 group 的 lazy catalog cache；它应由完整的文档/运行状态管理，而不是把文件路径泄漏给 Runtime。

## 包、更新与元数据

现有 `.galpak` 导出已对 `I18n/**` 逐文件打包，安装过程已有包 manifest 的文件完整性职责。因此首期不创建“这是 i18n 包”的额外元数据文件，也不创建独立 i18n PAK。

未来热更新或补丁若确实需要独立下载、版本协商或卸载翻译层，可在那个 feature 中引入版本化 localization manifest。其触发条件应是实际的补丁/网络分发需求，而不是为静态本地 JSON 提前增加协议。

## 首次语言选择

Sample 读取本机 profile 的玩家变量：首次启动计数为零时，以嵌入 UI 的 bootstrap culture 显示语言选择页。用户确认后在同一操作中持久化所选 locale，再增加启动计数。取消或异常不增加计数，因此下次仍显示选择页。后续启动读取所选 locale 并在创建 GameEngine 前设置 catalog/current culture；重置玩家变量会重新触发该流程。

## 验收影响

- Character catalog 验证唯一 ID、非空 display-name key，并由内置对话拒绝未知的 speaker ID。
- 同一 culture 下的多个内置与外部 JSON 的覆盖结果可复现，且不依赖枚举顺序。
- UI markup 与 Runtime dialogue/choice 使用同一选择 culture；外部文件可以覆盖两者。
- 默认语言缺失必需游戏 key 时，内容验证失败；非默认语言缺失时按回退链解析。
- 首次选择、确认、取消、重启与重置玩家变量均可自动化验证。

## 风险与后续决策

- 需在实现前 pin DynamicLocalization 版本并验证其 Avalonia markup、动态 provider 和 culture-change 行为。
- Character catalog 的具体持久化文件（例如 `I18n/characters.json` 的 metadata 与 display key 分离，或独立 `characters.json`）需要以 Editor/项目设置序列化的现有约束选择；它不应随着 culture 重复。
- 外部目录在包更新时的保留需要由安装器实现测试保证。
