# Git 构建元数据会改变契约二进制指纹

状态：confirmed。来源：F-20261021-03，用户再次反馈启动契约不匹配。

- 现象：项目和导出目录契约相同，重新构建的 Sample 契约 SHA256 不同，未改契约逻辑也被拒绝。
- 证据：两个实际 DLL 长度均 14848，反编译 IL 文本完全相同；AssemblyInformationalVersion 分别包含不同 Git commit。SDK 自动追加的 SourceRevisionId 属于文件内容，改变整个 DLL 的 SHA256。
- 正确做法：区分文件身份与语义兼容；选择严格二进制身份时，应稳定被哈希产物的非功能构建元数据，并以不同 SourceRevisionId 的真实重建比较 SHA256。部署后重开 Editor 项目，因为其 schema context 保留当前会话指纹。
- 补充：只稳定最终契约的 InformationalVersion 不够，编译引用的元数据也影响输出；SourceLink 的提交 URL 进入 PDB，其 checksum 也在 DLL 中。保持本地 PDB 不等于必须保留会变的 SourceLink URL。
- 禁止：把相同反编译代码直接解释为任意版本兼容；关闭真正的契约校验；把当前工作区哈希相同当成跨提交稳定性证据。
- 构建修正与验证见 feature 的 phase-contract-identity.md；其他编译配置、源码、生成器及依赖变化仍可能需要重新部署。
