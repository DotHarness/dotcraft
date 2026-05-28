import { randomUUID } from "node:crypto";
import { join } from "node:path";
import { tmpdir } from "node:os";

import { type WorkspaceContext } from "@dotcraft/sdk/channel";
import { runModuleConformanceSuite } from "@dotcraft/sdk/testing";

function createWorkspaceContextFixture(): WorkspaceContext {
  const workspaceRoot = join(tmpdir(), `dotcraft-weixin-${randomUUID()}`);
  return {
    workspaceRoot,
    craftPath: join(workspaceRoot, ".craft"),
    channelName: "weixin",
    moduleId: "weixin-standard",
  };
}

runModuleConformanceSuite(
  "@dotcraft/channel-weixin",
  async () => await import("./index.js"),
  {
    expectedModuleId: "weixin-standard",
    expectedChannelName: "weixin",
    expectedConfigFileName: "weixin.json",
    expectedRequiresInteractiveSetup: true,
    expectedVariant: "standard",
    workspaceContextFixture: createWorkspaceContextFixture(),
    validConfigFixture: {
      dotcraft: {
        wsUrl: "ws://127.0.0.1:9100/ws",
      },
      weixin: {
        apiBaseUrl: "https://ilinkai.weixin.qq.com",
      },
    },
  },
);
