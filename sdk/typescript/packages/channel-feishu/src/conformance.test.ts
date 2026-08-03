import { randomUUID } from "node:crypto";
import { join } from "node:path";
import { tmpdir } from "node:os";

import { type WorkspaceContext } from "@dotcraft/channel";
import { runModuleConformanceSuite } from "@dotcraft/channel/testing";

function createWorkspaceContextFixture(): WorkspaceContext {
  const workspaceRoot = join(tmpdir(), `dotcraft-feishu-${randomUUID()}`);
  return {
    workspaceRoot,
    craftPath: join(workspaceRoot, ".craft"),
    channelName: "feishu",
    moduleId: "feishu-standard",
  };
}

runModuleConformanceSuite(
  "@dotcraft/channel-feishu",
  async () => await import("./index.js"),
  {
    expectedModuleId: "feishu-standard",
    expectedChannelName: "feishu",
    expectedConfigFileName: "feishu.json",
    expectedRequiresInteractiveSetup: false,
    expectedVariant: "standard",
    workspaceContextFixture: createWorkspaceContextFixture(),
    validConfigFixture: {
      dotcraft: {
        wsUrl: "ws://127.0.0.1:9100/ws",
      },
      feishu: {
        appId: "cli_test",
        appSecret: "test-secret",
      },
    },
  },
);
