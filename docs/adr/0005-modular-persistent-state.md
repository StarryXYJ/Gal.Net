# ADR-0005：模块注册持久状态与恢复处理器

状态：Accepted（2026-10-15，用户确认设计并授权实施）

## 上下文

固定 SceneState、逐字段 SnapshotCopy、场景实例镜像和 Builtins 中央重放清单使新增持久领域需要修改多个中央位置。音频逻辑与后端身份混合，不能直接保存。

## 决定

- GameState 按注册类型及稳定 item ID 持有必要数据；存档以稳定 StateId、section version 和 JSON payload 编码。GameSnapshot v4 直接替换旧格式，不保留兼容读取或迁移路径。
- 执行模块通过 PrimitiveRegistry 贡献状态、恢复处理器及业务校验。Core/Runtime 不列举具体领域类型；schema 编译不创建执行模块。恢复依赖在 view 组合时检查并排序。
- 复用中立 Layer、EffectInstance、ParticleEmitterInstance 数据对象，删除 Active* 镜像。记录修改与快照捕获共享 State.Update 的同步边界；索引只引用记录。Task、取消源、动画停止控制和后端播放身份留在运行期。
- 读档验证候选状态后替换运行代；旧回调保留旧状态根。宿主 StateRestoreSession 统一恢复 continuation 与逆序清理；成功前不继续剧情，失败明确诊断。
- AudioTrackState 与 AudioSEffectState 保存所有仍存在的轨道逻辑、暂停、队列与效果；恢复从头播放，使用当前玩家设置与项目轨道配置，不恢复媒体游标或淡化时钟。

## 候选与取舍

继续固定列表虽改动小，但保留中央扩展点与双写。仅把列表放入 Type 字典仍不能处理 codec、身份及资源所有权。采用注册表与统一恢复会话增加一份通用协议，换取各领域独立贡献及删除中央类型分支。

首期不增加反射发现、自动对象图持久化、迁移器、事件溯源或深度不可变模型。可变记录需要模块遵守同步写入契约；通用 codec 无法推导业务规则，模块仍承担领域验证与恢复语义。

## 关联

- [设计](../design/modular-game-state-and-restore.md)
- [运行时契约](../spec/runtime.md)
- [实现与验证](../../features/F-20261015-03-modular-game-state/summary.md)
