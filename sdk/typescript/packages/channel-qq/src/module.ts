import {
  type ModuleInstance,
  type WorkspaceContext,
} from "@dotcraft/channel";

import { QQAdapter } from "./qq-adapter.js";

export function createModule(context: WorkspaceContext): ModuleInstance {
  const adapter = new QQAdapter();
  return {
    start: () => adapter.startWithContext(context),
    stop: () => adapter.stop(),
    onStatusChange: (handler) => adapter.onStatusChange(handler),
    getStatus: () => adapter.getStatus(),
    getError: () => adapter.getError(),
  };
}
