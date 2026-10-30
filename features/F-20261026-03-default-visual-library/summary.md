# 阶段交付记录

整体状态：约定首批已完成，P1–P4 verified。完整候选清单中的后续内容不属于本批交付。

## P1

- 新建默认契约项目获得 8 个可编辑 SkSL 资产及自身 metadata；运行不依赖编辑器模板。
- `defaultVisualPrograms` 保存项目用途绑定；准备阶段保留作者已有值，不改变传入配置。
- dry-run 报告包含将要部署的 shader/metadata；自定义契约和无契约 scaffold 维持显式能力边界。
- 9 项初始化/CLI 测试及 8 项真实 shader 编译测试通过。
- P1 提交时 DepthBlur / MaskReveal 仅有可编译资源，后续 P2 完成实际 Z 注入与外部纹理绑定，P3 完成预设与创作入口，P4 完成展示和文档。

## P2

- CPU/GPU 纹理链传入当前图层 Z，DepthBlur 读取每次绘制的实际值。
- 补齐通用 texture 参数加载、缩放与绑定，租约跟随效果实例，覆盖替换/停止/释放/准备失败；异步准备失效后不重新发布。
- 27 项实际 shader、像素及既有效果测试通过。默认宿主构建通过；真实 GPU 视觉验收尚未执行。

## P3

- 六个 Composite 编译为既有时间线：shader 遮罩转场、模糊淡化、人物动作、多层震动、动态集中线、冲击演出。
- 编辑器在插入/类型切换时填入项目绑定；保持显式可替换资源引用，编译器不查找默认 shader。
- 项目验证检查 shader 元数据契约、动画范围和遮罩资源；现有导出包含全部项目资产，不依赖扫描参数名裁剪资源。
- 场景分析及复制支持复合演出和 targets[].handleId。
- 306 项 Builtins、447 项 Shared、9 项桌面作者交互通过。普通 Editor 输出锁定，使用隔离 artifacts 验证。

## P4

- 新增 `project visuals install <project> [--dry-run]`，通过现有作者文件事务显式补入缺失资源和绑定。保留已有 shader 修改及绑定，拒绝覆盖占用路径，重复执行无变化。
- GameTestCase 两条路线的 ending 组演示首批效果、景深切焦、人物动作、同步震动、动态径向/定向集中线、冲击、擦除/模糊/图片遮罩转场，提供中英文文本。
- CPU contact sheet 视觉复核发现并修正景深采样网格重影和块状集中线；低分辨率线条抗锯齿问题由像素测试复现并修复。预览由显式 `WriteDefaultVisualPreview` 生成到 `artifacts/visual-library-preview.png`，不提交生成物。
- 最终 Shared 完整 448 项、Presentation 完整 257 项通过；GameTestCase 7 项集成通过，实际导出 PAK 中八个 shader 与遮罩图片可读取。显式预览生成 1 项通过。P3 的 Builtins 306 项和桌面作者交互 9 项保持有效；之后编译器/测试助手仅进行空白格式调整。
- feature C# 文件的格式检查和 git diff --check 通过。自审结论 pass-with-follow-up。

## 实现决定与边界

- 不新增 shader 专用效果 Primitive；六个复合入口展开为 `timeline.play`，所有 shader 引用来自作者 Entry 的显式参数。
- 稳定项目资源 GUID 与临时演出实例 ID 分离；项目配置的用途绑定只用于创作填充。
- 按图层 Z 景深需要显式选择参与图层；不提供图片内部深度图和散景。
- 正常完成/快进清理临时对象。取消/被替换沿用已有语义，不保证末帧事件执行；需要完整收尾时使用 `CompleteImmediately`。
- GPU 路径共享绑定代码构建通过，但本批没有真实 GPU 截图/性能验收；CPU 像素与视觉检查不能替代该验证。
- 开放 JSON 的本地化 opaque/incomplete 警告继续报告，样例完整 CLI 校验无 error、保留 32 条 warning。不把静态候选引用查询描述为完全分析。
- 旧项目补入通过 CLI 提供；未新增专属 GUI 安装按钮。契约 DLL ABI 变化仍需用户重建部署/重开项目，本地示例忽略的旧 DLL 已更新用于验证，二进制未入库。

## 文档与提交

- [使用手册](../../docs/manual/editor/default-visuals.md)、[Entry 规范](../../docs/spec/runtime/entries.md)、[资源规范](../../docs/spec/content/assets.md)。
- P1 `398f2cbb`：初始化 shader 与用途绑定。
- P2 `f8d33f4c`：图层 Z 与租约纹理绑定。
- P3 `882af07b`：转场、演出及作者集成。
- P4 为本收尾提交，包含样例、补入命令、视觉修正、测试和文档。
