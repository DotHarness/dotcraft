# TypeScript SDK 参考

`@dotcraft/sdk` 的包标识与语言特定细节。如何使用请从[快速开始](./quickstart)入手。

## 包

| | |
|---|---|
| 包名 | `@dotcraft/sdk`（npm） |
| 模块格式 | ESM（`"type": "module"`） |
| 运行时基线 | Node.js 20+ |
| 版本 | 从包导出的 `version`、`sdkContractVersion` |

```bash
npm install @dotcraft/sdk
```

## 入口点

包按 subpath export 拆分，应用只引入所需部分：

| 入口点 | 用途 |
|--------|------|
| `@dotcraft/sdk` | 高层应用 API（`DotCraft`、`DotCraftThread`、run、events）。 |
| `@dotcraft/sdk/wire` | 低层 JSON-RPC 客户端、传输、raw DTO。 |
| `@dotcraft/sdk/hub` | Hub 发现、启动与 SSE 辅助。 |
| `@dotcraft/sdk/channel` | 渠道适配器与托管模块 runtime。 |
| `@dotcraft/sdk/testing` | 一致性测试辅助。 |

`@dotcraft/sdk/channel` 也导出 media source 辅助函数，供渠道模块把已审批的路径、base64 载荷和允许的 URL 统一转换为 bytes、临时文件或上传 URI 字符串，同时保持工具 schema 稳定。

## 顶层导出

`DotCraft`、`DotCraftThread`、`DotCraftRunResult`、`DotCraftRunEvent`、`DotCraftError`、typed error 类（`TurnInProgressError`、`TurnFailedError` 等）、输入 part 构造器（`textPart`、`imageUrlPart`、`localImagePart`、`skillRefPart`、`commandRefPart`、`fileRefPart`）、App Binding 辅助（`parseAppBindingHandoff`、`appBindingToolError`、`APP_BINDING_ERROR_CODES`），以及审批决策常量。

## 渠道模块

TypeScript 拥有一方托管渠道模块，均依赖 `@dotcraft/sdk`：

`@dotcraft/channel-feishu`、`@dotcraft/channel-weixin`、`@dotcraft/channel-telegram`、`@dotcraft/channel-qq`、`@dotcraft/channel-wecom`。参见[渠道适配器](./channels)。

## 验证

```bash
cd sdk/typescript
npm run typecheck:all
npm run test:all
```

## 参见

- [快速开始](./quickstart) · [线程与运行](./runs) · [工具与审批](./tools) · [渠道适配器](./channels)
- TypeScript 绑定规范：`specs/sdk/typescript.md`
