# GalNet Features

这里保存一次独立实现的 feature 记录。推荐的目录格式是：

```text
F-YYYYMMDD-NN-short-slug\
├─ feature.md
├─ design.md
├─ phase-plan.md
├─ review.md
└─ summary.md
```

推荐流程是：

```text
create → design → phase-plan → implement → review → closeout
```

这只是推荐工作流，不是所有任务的强制前置条件。任何单个 skill 都可以在没有完整 feature 记录时独立运行，但已有 feature 时应优先使用它作为上下文和产物归属。

`summary.md` 面向开发者，记录实现结果和可读的历史；agent 专用的详细踩坑记录放在 `G:\program\GalDotNet\.agents\lessons\`。

