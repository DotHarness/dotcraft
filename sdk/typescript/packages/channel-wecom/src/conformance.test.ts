import { randomUUID } from "node:crypto";
import { join } from "node:path";
import { tmpdir } from "node:os";

import { type WorkspaceContext } from "@dotcraft/channel";
import { runModuleConformanceSuite } from "@dotcraft/channel/testing";

function createWorkspaceContextFixture(): WorkspaceContext {
  const workspaceRoot = join(tmpdir(), `dotcraft-wecom-${randomUUID()}`);
  return {
    workspaceRoot,
    craftPath: join(workspaceRoot, ".craft"),
    channelName: "wecom",
    moduleId: "wecom-standard",
  };
}

runModuleConformanceSuite(
  "@dotcraft/channel-wecom",
  async () => await import("./index.js"),
  {
    expectedModuleId: "wecom-standard",
    expectedChannelName: "wecom",
    expectedConfigFileName: "wecom.json",
    expectedRequiresInteractiveSetup: false,
    expectedVariant: "standard",
    workspaceContextFixture: createWorkspaceContextFixture(),
    validConfigFixture: {
      dotcraft: {
        wsUrl: "ws://127.0.0.1:9100/ws",
      },
      wecom: {
        robots: [{ path: "/dotcraft", token: "token", aesKey: "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG" }],
      },
    },
  },
);

