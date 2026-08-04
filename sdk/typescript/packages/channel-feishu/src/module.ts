import {
  type ModuleInstance,
  type WorkspaceContext,
} from "@dotcraft/channel";

import { FeishuAdapter } from "./feishu-adapter.js";

export function createModule(context: WorkspaceContext): ModuleInstance {
  const adapter = new FeishuAdapter();

  return {
    start: () => adapter.startWithContext(context),
    stop: () => adapter.stop(),
    onStatusChange: (handler) => adapter.onStatusChange(handler),
    getStatus: () => adapter.getStatus(),
    getError: () => adapter.getError(),
  };
}
