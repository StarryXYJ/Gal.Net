# 玩家字号档位与富文本相对字号：实现总结

日期：2026-11-02。实现与相关自动化验证完成，移动端实际阅读体验仍待真机确认。

## 实现结果

- 设置新增“小、较小、较大、大”四档，英文 Small/Smaller/Larger/Large。按项目玩家偏好保存稳定档位名，恢复不重写偏好；无效旧值回退默认。
- Sample 的 PlayerTypography.cs 提供四套独立角色字号及 DesktopDefault/MobileDefault。桌面默认较小，Android/iOS 默认较大。配置方式是项目源码，不增加 Editor 项目配置字段。Browser 使用桌面默认，若需区分手机浏览器，由项目选择平台默认。
- 语义角色包含页面标题、分组标题、正文、强调、操作、辅助、对白正文。桌面较小档保留原有标题 64、页头 34、正文 18、Speaker 26、对白 22 等字号；历史和弹窗保留原有细分层级。当前 surface/shell 局部资源动态更新，截图弹窗读取当前偏好；开场演出独立排版。
- 富文本新增 `<size=1.25em>`、`<size=125%>`、`<size=+4>`、`<size=-2>`、`<size=24>` 和 `</size>`。比例与无单位正负偏移相对于父级，支持嵌套恢复；绝对值保持固定。最终字号限制 1–512，非法标签保留字面文本。
- Core token 通过默认 init 属性扩展 scale/offset，保留原位置构造与解构参数；Avalonia 更新当前 runs 的字号，不清空文本、不重启打字、不改变完成任务。ADV/NVL/历史共用此路径。

固定场景画布、剧情坐标、页面布局、按钮命中范围、截图/存档数据格式未改变。没有加入响应式重排、底栏收纳或黑边扩展。

## 验证证据

使用本机既有 SDK/NuGet 缓存与默认 bin/obj，`dotnet test --no-restore`：

| 范围 | 结果 |
| --- | --- |
| GalNet.Core.Tests 全部 | 156 passed |
| GalNet.Presentation.Tests 全部 | 230 passed / 236 total；6 项 Explicit 未执行 |
| Integration 相关筛选 | 93 passed / 93 total |

合计 479 passed；不是全解决方案 CI。集成筛选覆盖 PlayerTypography、SampleTemplatePages、SampleWindowTheme、SampleLocalization、SampleDisplaySettings、SampleReadTextColor、SampleCustomization、SampleOpening、SampleInputSettings、ScreenshotDialogLayout、GameTheme。新增回归共 24 例。

测试包括真实挂载的标题/ADV/姓名/NVL/历史更新、富文本 Run 字号、实际文字测量变化、打字过程中的任务和 Run 身份保持、平台默认、重建变量服务后的保存恢复及无重写、标签本地化、未知档位回退、截图资源与角色独立配置。

TRX 保存在仓库 output/player-typography/{core,presentation-final,integration-final}/，不提交 Git。最终 git diff --check 通过；文档链接验证结果见 execution.md。

## 过程偏差与限制

用户以 fast feature 启动；因涉及 Core 公开 token，按技能规则转为普通分阶段流程，未压缩掉兼容性与回归记录。没有请求额外实现授权。

初轮测试发现测试夹具问题：skip 必须遵守已有单词边界；纯 Presentation 测试没有字体后端，不宜直接做字体测量。修正文本夹具，并把测量验证放到已有 Headless 集成会话。初轮集成测试另有方法组/命名空间/必填参数编译错误，均已修正；最终结果如上。没有通过创建新环境绕过问题。

桌面一致性验证范围是原设计字号保留与现有窗口/模板/布局回归，没有采集修改前后的像素级对照。Android/iOS 没有运行或安装；不能将平台分类单测当作真机验收。大字号仍受固定画布缩放影响，超长对白、超宽/竖屏和系统安全区域的可用性属于后续移动布局验收。

当前规范见 [GameView](../../docs/spec/presentation/game-view.md#玩家字号与富文本字号)、[GameHost](../../docs/spec/presentation/game-host.md)，配置见[页面与主题扩展](../../docs/manual/extensions/pages-and-themes.md#定制字号档位)，玩家说明见[操作与设置](../../docs/manual/sample/gameplay-and-settings.md#设置)。
