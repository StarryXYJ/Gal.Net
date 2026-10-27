# 递归属性访问审核

审核方式：实现作者自审、代码/需求逐项核对及实际回归；未安排独立第二审阅者。当前范围结论为 **pass-with-follow-up**；整仓库验收仍有下述构建阻挡，P5 不标 complete。

## 已核对

- 共享结构描述复用现有字段 schema 和 codec，未增加第二份可变作者数据；Core 无 UI/项目 I/O 依赖。
- 旧公开描述构造函数及 Pointer Set API 保留；独立 consumer 对旧 API stub 编译后在当前 Core 上加载通过。
- 固定内置结构支持共享只读分析、专用编辑、局部 patch 和保存；未知字段、重复/歧义及无法理解的 codec 不被当作确定无引用。
- JsonString、MapKey、Index 与 Member 分开，已同时检查读取、定位、引用身份比较、复制和回写；数值 Map 键显示为字面键，避免与数组序号混淆。
- 只读遍历无实例 getter/DTO 构造；预算/取消、Partial/完整检查、missing/null/default 与编码边界有实际断言。
- 既有专用布局、注册覆盖、宿主 Reset/可见性、值草稿及项目历史链路保留。失败的键名结构操作仅在当前控件保留，已在 spec/manual 明确。
- 控件/分析缓存均有作用域，旧列表绑定由结构代次取消，DTO scope 转发属性路径。动态查询入口不伪装成已有领域迁移。
- 相关 source/build/default-contract 证据在 TestResults；正式规范与接入手册只写入当前实现，不宣称动态效果/Timeline 已覆盖。

## 审核中已修正

1. 字符串内部多个引用共用实际 Pointer，旧引用比较/回写会冲突或替换整段字符串。迁入 LocationKey / PropertyPath.Rewrite，编码数组及文本/角色旧回归验证独立叶值更新。
2. JsonArray 递归回写原节点会触发 NodeAlreadyHasParent。仅在节点实例实际改变时重新挂载；保留失败回归与修复证据。
3. 编码对象被按原始 string 误判结构改变，导致叶编辑重建；编码集合结构操作也会失去编码。改按解码形状判断并在提交时保留原表示。
4. Root Reset 能力与宿主 Reset 策略不能混同，子字段现在继承宿主能力；未知旧输入回退 JSON，避免空的专用面板。
5. 字面标签与本地化 token 分开；显示完整路径时 Map 键不按整数索引格式化。相同末级字段优先按完整 PropertySegments 聚焦。
6. 显式结构类型不应投影成无子结构的错误标量并静默通过；未知 DTO codec 的 string wire 也不证明内部没有引用。加固为 schema.invalid/opaque 诊断并补充断言。

## 剩余验证与跟进

**仓库构建阻挡**：最近保留的整解决方案 Release 构建在 `src/Samples/GalNet.Sample.Avalonia/SampleCustomization.cs:105` 报 CS1503：第 7 个视频参数 IVideoPresenter 无法转换为 AvaloniaVideoPresenter。此 feature 未修改 Sample/视频宿主，相关 Editor/Core/Controls 测试项目构建通过。该失败保留于 `TestResults/solution-build.log`，不能声称当前整解决方案构建通过；P5 保持待全局复核状态。

**图集用例稳定性**：ColdAtlasSelectorCanFinishPreparationButAnAuthorEditStillInvalidatesIt 在两个 Release 全量中失败，单独/前缀运行通过；旧 Debug 基线 336 项通过。配置和产物不同，不能用 Debug 通过直接证明 Release 的失败与本次完全无关。用例新增更严格的初次 Select 断言及实际失效版本信息，未修改 SceneTargetSelection/ProjectSceneTargets，也未放宽版本保护；随后 Release 全量 338 项通过。根因尚未确定，保留 suite-order/时间相关稳定性跟进，不将其描述为已修复。

**范围边界**：动态效果、Timeline、未描述 JSON、自定义复杂 converter 仍可报告未覆盖；这是本次明确的交付边界。原生 IME/DPI/辅助技术及独立第二审核未执行，不用 Headless 截图替代这些验收。

最终验证结果与实际次数以 [summary](summary.md) 和 TestResults 中最终 TRX 为准，复跑不重复累计。无证据的全局通过或未迁移能力不计入结论。
