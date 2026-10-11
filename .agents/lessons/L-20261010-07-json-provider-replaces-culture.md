# DynamicLocalization JSON 逐文件加载会替换同 culture 的字典

状态：confirmed

来源 Feature：F-20261010-02-runtime-i18n

## 现象与根因

DynamicLocalization.Core 2.1.2 的 `JsonLocalizationProvider.LoadJsonString` 将解析结果赋给该 culture 的整个字典，而非增量合并。对同 provider 依次加载不同 group 的文件，之前文件的独有 key 消失。该版本 provider 的 `TryGetString` 也不递归查询父 culture。

## 证据与发现方式

完整 GameTestCase 的四条语言/路线运行暴露人物名、选项、后续 group 回显 key；单文件重复 key 测试无法发现字典丢失。递归加载测试中 a.json 的独有 fallback key 在 z.json 加载后消失。直接调用安装的 2.1.2 DLL，只有 zh 资源时，查询 zh 成功而 zh-CN 失败。

## 正确做法与适用范围

宿主为每个 JSON 分别建立 provider，保留确定性源顺序；查询时先遍历当前 culture、父 culture，再遍历游戏默认 culture 链，每个 culture 内从后向前查找。库继续解析 JSON，GalNet 不合并文件或重复实现 JSON 展平算法。Sample 两个宿主共享源码适配器，Core/Runtime 不引入库类型。

## 禁止做法

不要把“后文件重复 key 生效”当作“逐文件增量合并”的证据。版本变更必须同时测试重复 key、独有 key 保留和父 culture 回退。

关联文档：`features/F-20261010-02-runtime-i18n/design.md`、`docs/spec/i18n.md`。
