# Solution 构建必须覆盖测试读取的 fixture 配置

状态：confirmed。来源：F-20261027-04-tagged-release。

- 现象：Release 全量构建成功，契约配置流水线有三项失败；测试读取的 SchemaA Release DLL 时间仍是旧版本，缺少 developer.options。
- 证据：out/ci/tagged-release-candidate/build.log 显示 solution 外的 SchemaA/SchemaB 引用实际构建到 bin/Debug；测试按自身 Release 编译常量读取 bin/Release。
- 修正：将全部 fixture 项目纳入 GalNet.slnx，让 solution 为引用工程传递相同配置；fixture-build.log 确认输出 Release，ProjectContractPipelineTests 8 项通过。
- 适用：测试或 CLI 从普通 bin 路径加载程序集、包含 ReferenceOutputAssembly=false 的契约 fixture。
- 禁止：依赖开发机残留 DLL，或通过复制旧产物/跳过失败用例让 CI 通过。
