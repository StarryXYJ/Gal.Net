# 包装或嵌入 JSON schema 必须保持局部引用语义

状态：confirmed
来源 Feature：F-20261013-02-parameter-and-sample-simplification / Phase 1

## 现象与根因

System.Text.Json 的 schema exporter 会为递归或重复 DTO 类型生成局部 $ref。直接将导出 schema 放进 allOf/anyOf 或命令 properties 后，引用仍从最终文档根解析，原路径会失效或指向错误对象。直接修改被引用根的可空规则也可能把外层字段规则扩散到递归子字段。

## 发现与验证

通过递归 DTO 调查并增加 ValueDescriptorTests.RecursiveSchemaReferencesResolveWhenStandaloneAndEmbedded：实际遍历和解析 standalone/embedded schema 的全部局部引用，同时验证非 null 根和 nullable 子值的运行期读取。不能假定第一层 Child 一定就是引用节点；exporter 可先展开一层再引用更深位置。

## 正确做法与禁止做法

保留 exporter 的对象结构，根字段约束放在外层；嵌套前把局部引用按真实目标位置重定位。当前 ValueCodec.Describe 可接收嵌入 prefix，CLI 使用经过 JSON Pointer 转义的 properties 路径。不要直接包装后保留原引用，不把引用字符串存在视为验证通过，不用重写整个根类型来实现单个字段的 null 策略。

适用于 DTO/schema 生成器、工具输入描述、嵌套表单及任何将独立 schema 组合进更大 schema 的代码。只处理当前 exporter 的局部引用，不宣称支持任意外部 $id/$ref 的通用 JSON Schema 重写。
