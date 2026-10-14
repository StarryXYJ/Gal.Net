# Phase 5b：简单 Primitive 参数 DTO 迁移

状态：verified，2026-10-14。无阻断项；下一批为 Phase 5c。全量 DTO/Composite 迁移与异步实例简化仍在后续阶段。

## 实现与收益

迁移 animation.stop、audio.seffect.stop、dialogue.text、effect.stop、gallery.unlock、layer.hide、layer.replace、nvl.append、particle.stop、variable.set、video.stop，共 11 条指令、16 个参数字段。新增 10 个 DTO，effect.stop/particle.stop 共用 InstanceIdOptions；DTO 属性/特性是唯一参数描述，旧 ParameterTypes/DefaultValues/ParameterOptions 已删除。执行读取使用 For<T>/Immediate<T>，删除 16 处按字符串名读取。

animation.stop 使用可空 AnimationStopMode 与严格字符串 converter，保留 AfterIteration 默认及原 options 顺序；显式 null 保留原 fallback。audio.seffect.stop/video.stop 保留 bgm/video 默认；Gallery 的正整数与 catalog 检查仍由原执行边界承担。对白/NVL 保留 reading 标记、文本解析、批次、scope cancellation 与原实例；effect/particle/audio 的异步/取消/错误处理不改动。四种同步操作同时删除手工 ImmediatePrimitiveInstance/batch 包装，使用既有 helper。

DTO 特性采用多行可读声明，总生产行数并未下降；本批收益是 16 个字段的类型/schema/读取事实收敛，以及编译器能够检查业务属性引用。旧 primitive 类暂时只保留 TypeId/作者壳，统一清理由 5d 处理，不把生成代码或后续删除计作本批收益。

## Editor 接入修正

初次完整 Editor 回归 39/40：variable.set 原目标补全控件因 DTO 整组表单提前返回而消失。检查实际 registry 确认尚无 entry.* 专属工厂，含补全、多行文本、资源或表达式提示的 DTO 因此继续使用原逐字段控件投影；通用字段仍使用共享 binding，同一 DTO.Schema 保持唯一事实。提取 UsesDedicatedEditor 复用原选择规则，不降级为普通文本框，也不新增 Editor DTO 副本。

修复后完整 Editor 40/40，再增加混合 layer.replace 的资源选择/普通字段提交/过期绑定测试，最终 41/41。现有变量目标测试继续验证 suggestions 更新和写回；专属控件模板本批没有重写。原因和规则记录在 L-20261014-05-dto-keeps-dedicated-entry-editors。

## 验证

- Builtins 完整 94 项通过：含 12 项新增省略/null/未知字段/错误类型/枚举选择/Gallery 执行诊断测试，以及既有文本、媒体、场景、模式和 Composite 回归。
- Editor 完整 41 项通过，含新增混合 DTO 字段绑定回归。
- Integration 完整 100 项通过，Sample Avalonia 随依赖构建通过。
- 合计 235 个不重复测试通过，TRX 位于 Temp/F-20261013-02/phase5b-{builtins,editor,integration}.trx。
- Contracts 独立部署和当前 CLI 构建通过；11 条指令迁移前后 CLI result.items 完全相同，包含参数顺序、默认、null、constraints、editor 与 reading 标记。
- GameTestCase 实际 CLI 编译成功：4 个 group，diagnostics 为空，记录 phase5b-build.json。

顺序运行测试并禁用 shared compilation/telemetry；仅检查本批文件的 IDE0005 与 whitespace，git diff --check。存在既有 analyzer/Avalonia warning 和 NU1900 审计端点不可用提示，无编译错误。

## 当前范围

43 条 entry 中 18 条参数 entry 已采用 DTO，5 条无参数；剩余 20 条参数 entry（12 条复杂 Primitive、8 条 Composite）由 5c/5d 继续。开放 payload 保留 JsonElement 的既定设计不变。
