import { randomUUID } from "node:crypto";
import { join } from "node:path";
import { tmpdir } from "node:os";

import { type WorkspaceContext } from "@dotcraft/channel";
import { runModuleConformanceSuite } from "@dotcraft/channel/testing";

function createWorkspaceContextFixture(): WorkspaceContext {
  const workspaceRoot = join(tmpdir(), `dotcraft-telegram-${randomUUID()}`);
  return {
    workspaceRoot,
    craftPath: join(workspaceRoot, ".craft"),
    channelName: "telegram",
    moduleId: "telegram-standard",
  };
}

runModuleConformanceSuite(
  "@dotcraft/channel-telegram",
  async () => await import("./index.js"),
  {
    expectedModuleId: "telegram-standard",
    expectedChannelName: "telegram",
    expectedConfigFileName: "telegram.json",
    expectedRequiresInteractiveSetup: false,
    expectedVariant: "standard",
    workspaceContextFixture: createWorkspaceContextFixture(),
    validConfigFixture: {
      dotcraft: {
        wsUrl: "ws://127.0.0.1:9100/ws",
      },
      telegram: {
        botToken: "123:token",
      },
    },
  },
);
