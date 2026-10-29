# 源生成 JSON 的 init-only 默认值

状态：confirmed。来源：F-20261027-04-tagged-release。

- 现象：精简作者 JSON 只声明音轨 id，生成元数据反序列化后 MaxVoices、DefaultGain 均为 0，导致合法音轨配置被拒绝；Bus gain 也变为 0。
- 根因：init-only 成员通过生成器的构造绑定初始化，省略字段的默认绑定值覆盖 CLR 属性初始化器。反射路径的行为不能作为源生成路径证据。
- 验证：GeneratedMetadataPreservesDefaultsForOmittedAudioFields 同时断言音轨容量/增益与 Bus 增益，先失败，显式带可选默认值的 JsonConstructor 后通过。
- 修正：保留 public 无参构造与 record/init 不可变语义，增加 JsonConstructor 及明确的字段默认值。
- 禁止：将 DTO 全部改成可写属性破坏已有所有权保证，或放宽音轨容量校验掩盖默认值丢失。
