import {
  type ModuleInstance,
  type WorkspaceContext,
} from "@dotcraft/sdk/channel";

import { WeComAdapter } from "./wecom-adapter.js";

export function createModule(context: WorkspaceContext): ModuleInstance {
  const adapter = new WeComAdapter();
  return {
    start: () => adapter.startWithContext(context),
    stop: () => adapter.stop(),
    onStatusChange: (handler) => adapter.onStatusChange(handler),
    getStatus: () => adapter.getStatus(),
    getError: () => adapter.getError(),
  };
}

