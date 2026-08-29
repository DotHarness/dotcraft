# Spec-Driven Development（规范驱动开发）

DotCraft 采用 **spec-first（规范先行）** 的方式构建：每一项协议与跨模块行为都先写成规范（spec），再去实现，之后由代码对齐这份规范。本页说明这一思路，以及为 DotCraft 贡献代码时如何遵循它。

## 什么是 Spec-Driven Development？

Spec-Driven Development（SDD，规范驱动开发）是一种方法论：**作为唯一事实来源（source of truth）的是版本化、人类可读的规范，而不是代码**。你先描述预期的行为、契约与约束。实现与测试随后从规范推导，并对规范进行校验。当需求变化时，先改规范，再让代码回到一致。

SDD 在 2025–2026 年间逐渐流行，作为对 AI 辅助下随性"vibe coding"的一种结构化替代——后者往往偏离意图，并随项目增长而劣化。把意图固化在持久的规范里，能给人和 Agent 都提供一份稳定的契约。

> SDD 通常分为三个严谨度层级：**spec-first**（先写规范再写代码）、**spec-anchored**（把规范作为代码持续对齐的活契约）、**spec-as-source**（从规范重新生成代码）。DotCraft 处于 spec-first / spec-anchored 层级——规范在实现之前编写，并始终作为契约存在。

## DotCraft 如何应用它

`specs/` 目录存放 DotCraft 的活规范——`session-core`、`appserver-protocol`、`app-binding` 等。它们定义的是 **what（是什么）**：行为、线协议契约、生命周期与约束，而非实现细节。

工作准则：

- **先规范，后代码。** 当你修改 `specs/` 中定义的协议设计或跨模块流程时，先更新规范，再实现。
- **在规范层面解决冲突。** 如果改动与现有规范冲突，先在规范层面解决，再动代码。
- **一致性测试对齐规范。** 依赖协议的代码带有与规范对齐的测试，偏离会以测试失败的形式暴露出来。
- **文档跟随行为。** 行为变化时，更新面向用户的文档（中英双语）。
- **规范描述契约，而非代码。** 规范说明"必须为真的事项"，并保持足够高层，以经受实现迭代。

## 工作流

对于较大的功能，节奏是：先讨论、先规范后代码、一次只做一个里程碑：

| 阶段 | 内容 |
|---|---|
| 1. 范围与调研 | 厘清目标、受益者、成功标准与非目标。仅在有帮助时才调研先例。 |
| 2. 里程碑大纲 | 把工作拆成若干里程碑，给出目标、产出与范围边界。 |
| 3. 风险与可行性 | 评估对现有模块的架构压力、兼容性、UX 与测试难度。 |
| 4. 里程碑规范 | 在 `specs/` 下编写编号规范（如 `feature-name-m1.md`）——写行为与验收，而非实现。 |
| 5. 实现计划 | 仅针对当前里程碑，规划涉及的模块、契约与验证方式。 |
| 6. 实现 | 每次只实现一个里程碑。遇到与规范不符的情况时，通过提问确认，不要悄悄偏离。 |
| 7. 基于规范的验证 | 对照里程碑规范的验收清单检查结果。 |

## 工具：产品插件与共享工作流插件

两个官方插件分别承载产品专属知识与可复用的工程工作流。可从 Plugins catalog 启用它们（参见 [插件与工具](../../features/agent-system/plugins-tools)）：

同时启用两个插件后，DotCraft 专属规则来自 `dotcraft`。"规划一个新功能"这样的请求会激活 `harness-workflow` 中的 `$feature-workflow`：Agent 会讨论范围、提出里程碑、按照当前仓库约定编写规范，然后才实现并对照规范验证。

## 参见

- [架构总览](../architecture/overview)——规范所描述的运行时。
- [插件与工具](../../features/agent-system/plugins-tools)——安装与启用 `dotcraft` 和 `harness-workflow`。
- [外部 Agent 协作](../../features/agent-system/workspace-handoff)——为外部 coding agent 导出上下文。

延伸阅读：[What is Spec-Driven Development?（IBM）](https://www.ibm.com/think/topics/spec-driven-development) · [Understanding Spec-Driven Development（Martin Fowler）](https://martinfowler.com/articles/exploring-gen-ai/sdd-3-tools.html)
