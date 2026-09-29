# 实施计划

## Phase 1 - Characterization tests

**状态：verified**

- 扩充 FileSaveService 保存、metadata、preview、删除、quick-save 和取消测试。
- 增加 `ISaveService` API shape 测试。

退出条件：现有语义有迁移前证据，目标接口可自动验证。

**验证（2026-09-29）：** 新增 6 个 FileSaveService 测试，覆盖 snapshot、description、preview、quick-save、删除、清空、取消传播和 ISaveService API shape。

## Phase 2 - 端口迁移与死接口删除

**状态：verified**

- 移动 Runtime/Editor 契约，更新消费者。
- 删除无消费者接口和 DefaultGameSession。
- 收敛 ISaveService 与实现、调用方。

退出条件：编译通过，搜索无旧接口或旧保存重载。

**验证（2026-09-29）：** Core Services 仅保留 `IAudioService` 与 `ITextResolver`；进度端口迁入 Runtime，Editor 设置/退出端口迁入 Editor.Abstraction。旧接口、DefaultGameSession 和双套保存重载已删除，搜索无残留。

## Phase 3 - 全量验证与提交

**状态：verified**

- 运行测试、Headless、完整 solution 和 diff 检查。
- 更新路线图、spec、feature 总结。
- 创建独立 Git 提交。

退出条件：Phase 2a 全部退出条件满足。

**验证（2026-09-29）：** GeneralTest 275/275、Assets Tests 34/34 通过；Editor Headless、Sample Headless 与完整 Android/Browser/Desktop/iOS solution 构建成功。
