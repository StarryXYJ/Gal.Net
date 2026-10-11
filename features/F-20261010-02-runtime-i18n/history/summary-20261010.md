# Runtime i18n 阶段总结

日期：2026-10-10。状态：阶段性提交，Feature 仍在实现中。

## 已实现

- Sample 使用 DynamicLocalization.Core 2.1.2 的嵌入 JSON、CultureService 与 LocalizedString.Value。自有 Avalonia 12 ReflectionBinding 适配层替代不兼容的平台扩展；直接修改 CultureService 也更新已有绑定。
- 共享页面提供通用 IPlayerSettingsService 和选择/数值设置项，Sample 提供语言与文本速度。设置标签、常用静态页面文本及调试窗口使用动态本地化绑定。
- 设置复用会话的 Player 变量存储，不覆盖其他玩家变量；会话初始化后恢复，修改时排队写入，清空玩家状态及销毁会话前等待写入。
- Core 提供人物 ID/name-key catalog；内置 dialogue/NVL 通过角色 ID 和 ITextResolver 解析文本。raw content 中已有的 key 原样通过编译和运行期，不按执行位置重写。
- Sample 启动脚本将默认 profile 放在项目 .galnet 中，避免把玩家数据放入可替换的构建输出。

## 验证

本次提交前执行代码空白格式化、IDE0005 无用 using 清理及 git diff --check。
测试通过：Core 61、Builtins 52、Presentation 76、Integration 43、Architecture 24，共 256 项，0 失败。Presentation 输出另有真实媒体场景跳过提示，不能据此声称本机媒体验收完成。集成测试包含真实 Avalonia TextBlock 绑定的直接 culture 更新、设置持久化/恢复及其他 Player 变量保留、嵌入资源与 JSON 同 key 覆盖。

## 未完成与边界

- Phase 3 的游戏人物文件加载、游戏翻译加载和 Sample resolver 接线未完成。因此旧 Sample 剧情的 Guide 尚未提供角色记录，点击启动游戏的 Unknown character ID 异常仍是已知问题，不是本阶段完成的验收项。
- Phase 4 的外部翻译目录发现、验证及确定性覆盖未完成。provider 单元级 JSON 覆盖测试不等于完整外部包加载已实现。
- Phase 5 首次语言选择与启动计数未完成。设置存储仅在游戏会话成功初始化后接入，不能声称无游戏/初始化失败时的设置已持久化。
- 任意 ViewModel 业务字符串及已显示剧情不会因 culture 自动翻译；只有接入 LocalizedString 的动态绑定具备当前刷新行为。
- 未完成完整 GUI 游戏流程验收；已有分析器和 AVLN3001 构建警告仍保留。
- 原 Output 目录替换失败的访问拒绝未确认最终根因；独立 Output-new 可重复构建不代表原目录问题已修复。生成输出和官方参考仓库不进入提交。

## 文档与经验

实现事实同步至 docs/spec/i18n.md 与 docs/spec/control.md。经验记录见 L-20261010-05（平台扩展二进制兼容与官方动态绑定机制）及 L-20261010-06（已存储 key 的作者边界）。后续继续按 phase-plan.md 推进，不将阶段性提交标记为整个 Feature 完成。
