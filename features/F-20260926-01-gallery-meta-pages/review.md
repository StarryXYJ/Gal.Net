---
feature: F-20260926-01-gallery-meta-pages
reviewed: 2026-09-26
scope: feature-and-design
result: pass
---

# Gallery 元数据聚合与可扩展页面注册设计审核

## 结论

**pass**。

最新确认已经消除了此前的需求分叉：Gallery 使用全局唯一正整数 ID 并按 ID 排序；Editor authoring、Undo/Redo 和旧数据迁移后移；资源类型完整注册包含 type ID、Meta DTO 类型和扩展名，同时允许只向既有类型追加扩展名；当前阶段明确不承担格式兼容。

设计可以进入 Phase Plan。没有发现阻止实施的架构问题。

最终复核补充：需求中“运行时不扫描 `.meta`”已经收敛为“目录开发运行启动时聚合一次、会话建立后不再扫描；发行运行只读生成内容”，与设计的数据流一致。未发现新的 Blocker、P1 或 P2 问题。

## 审核结果

### 1. 资源类型注册边界清楚

- `Add<TMeta>(typeId, extensions...)` 是完整注册。
- `AddExtensions(typeId, extensions...)` 只扩展已有类型，冻结时统一校验，因此不依赖插件调用顺序。
- Meta DTO 通过文件中的稳定 type ID 选择，文件不保存 CLR 类型名。
- decoder 使用 `(resourceTypeId, target CLR type)` 注册，解决多个格式解码为同一运行时对象时的覆盖问题。
- `Compress` 属于通用存储策略，留在公共 Meta 基类；渲染专属字段进入具体 DTO。

### 2. 开发目录与发行包语义一致

Local provider 和 pak reader 都通过同一资源 registry 取得 DTO 类型。Pak 保存字符串 type ID 和 canonical metadata JSON，自定义字段不会只在开发目录有效。Gallery JSON、typed metadata 和代码 registry 之间都有一致性校验。

### 3. Gallery 数字 ID 方案自洽

正 `Int32` ID 在全项目唯一，因而可以同时作为：

- `gallery_<id>_unlocked` 的稳定中间值；
- `gallery.unlock(id)` 的参数；
- 类型页面内的升序键。

导出内容是静态的，因此不需要额外重排模型：ID 就是最终顺序和稳定解锁身份，修改 ID 等同于替换该 Gallery item。

### 4. Avalonia 扩展不再经过 renderer enum

Gallery type ID 到 ViewModel、ViewModel 到 View 是两张独立表。精确 type ID 注册、显式页面复用、显式覆盖和缺页诊断均有明确行为；零类型、单类型直达和多类型选择页共享同一个导航服务。

### 5. 首轮范围足够克制

Editor 只承担删除 `ResourceType` enum 后必要的编译适配。Gallery Inspector、数字分配 UI、metadata Undo/Redo、旧 `gallery.json` 迁移和兼容 reader 均不进入首轮，避免底层 registry 与 Editor 产品交互同时展开。

## 实施时必须保持的检查点

- 自定义 Meta DTO 字段必须通过 LocalFileProvider 与 pak round-trip，不能只验证 type ID。
- pak table 的 Id/Path/TypeId 必须与反序列化 Metadata 一致。
- 数字 Gallery ID 冲突必须报告两个来源 `.meta`，不能只报告第二个值。
- `AddExtensions` 的未知目标和后缀冲突必须在 registry 冻结时失败。
- 删除 `ResourceType` enum 后要全 solution 搜索残留 switch、picker filter 和 sample decoder 注册。
- 页面注册 Builder 必须同时验证 Gallery page mapping、VM-to-View mapping 和 DI registration。

## 验证记录

- 审核了 feature、修订后的 Proposed design，以及当前 Assets、Storage、Editor、Gallery 和 Avalonia navigation 实现。
- 核对了当前 `ResourceType` enum、pak int32 类型字段、单目标类型 decoder 字典、重复扩展名推断和 renderer enum 的实际传播路径。
- 本轮仅修改 feature 文档，没有业务代码可执行测试；未运行构建或测试。

