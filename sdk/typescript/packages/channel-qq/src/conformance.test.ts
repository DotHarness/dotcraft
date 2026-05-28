import { randomUUID } from "node:crypto";
import { join } from "node:path";
import { tmpdir } from "node:os";

import { type WorkspaceContext } from "@dotcraft/sdk/channel";
import { runModuleConformanceSuite } from "@dotcraft/sdk/testing";

function createWorkspaceContextFixture(): WorkspaceContext {
  const workspaceRoot = join(tmpdir(), `dotcraft-qq-${randomUUID()}`);
  return {
    workspaceRoot,
    craftPath: join(workspaceRoot, ".craft"),
    channelName: "qq",
    moduleId: "qq-standard",
  };
}

runModuleConformanceSuite(
  "@dotcraft/channel-qq",
  async () => await import("./index.js"),
  {
    expectedModuleId: "qq-standard",
    expectedChannelName: "qq",
    expectedConfigFileName: "qq.json",
    expectedRequiresInteractiveSetup: false,
    expectedVariant: "standard",
    workspaceContextFixture: createWorkspaceContextFixture(),
    validConfigFixture: {
      dotcraft: {
        wsUrl: "ws://127.0.0.1:9100/ws",
      },
      qq: {
        host: "127.0.0.1",
        port: 6700,
      },
    },
  },
);
