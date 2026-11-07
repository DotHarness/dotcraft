import { userInfo } from "node:os";
import { DotCraftThread } from "./thread.js";
import { stripRuntimeDynamicToolHandlers } from "./threadInput.js";
import type { DotCraft, ThreadManager, ThreadIdentityOptions, GetOrCreateThreadOptions, StartThreadOptions, ResumeThreadOptions, ForkThreadOptions, ListThreadOptions, ThreadHistoryPageOptions, ThreadItemPageOptions } from "./dotcraft.js";
import type { SessionThread, ThreadSummary, ThreadListResult, ThreadTurnsListResult, ThreadItemsListResult } from "./generated/appserver/index.js";

function defaultUserId(): string {
  try {
    return userInfo().username || "local-user";
  } catch {
    return "local-user";
  }
}

type NormalizedIdentity = {
  channelName: string;
  userId: string;
  workspacePath?: string;
  channelContext?: string;
};

function normalizeIdentity(options: ThreadIdentityOptions = {}): NormalizedIdentity {
  return {
    channelName: options.channelName ?? "sdk",
    userId: options.userId ?? defaultUserId(),
    ...(options.workspacePath ? { workspacePath: options.workspacePath } : {}),
    ...(options.channelContext ? { channelContext: options.channelContext } : {}),
  };
}

export class ThreadManagerImpl implements ThreadManager {
  constructor(private readonly sdk: DotCraft) {}

  async getOrCreate(options: GetOrCreateThreadOptions = {}): Promise<DotCraftThread> {
    const identity = normalizeIdentity(options);
    const threads = await this.sdk.wire.threadList({
      ...identity,
      includeArchived: options.includeArchived ?? false,
    });
    const reusable = threads.find((thread) => thread.status === "active");
    if (reusable) {
      return await this.resume(reusable.id, {
        dynamicTools: options.dynamicTools,
        additionalContext: options.additionalContext,
      });
    }
    return await this.start(options);
  }

  async start(options: StartThreadOptions = {}): Promise<DotCraftThread> {
    const identity = normalizeIdentity(options);
    let thread!: DotCraftThread;
    await this.sdk.wire.threadStart({
      ...identity,
      displayName: options.displayName,
      historyMode: options.historyMode,
      config: options.config,
      dynamicTools: stripRuntimeDynamicToolHandlers(options.dynamicTools),
      additionalContext: options.additionalContext,
    }, (snapshot) => {
      thread = new DotCraftThread(this.sdk, snapshot, identity);
      thread.bindDynamicTools(options.dynamicTools);
    });
    return thread;
  }

  async resume(threadId: string, options: ResumeThreadOptions = {}): Promise<DotCraftThread> {
    let thread!: DotCraftThread;
    await this.sdk.wire.threadResume(threadId, {
      dynamicTools: stripRuntimeDynamicToolHandlers(options.dynamicTools),
      additionalContext: options.additionalContext,
      initialTurnsPage: options.initialTurnsPage,
    }, (restoration) => {
      const snapshot = restoration.thread;
      thread = new DotCraftThread(this.sdk, snapshot, {
        channelName: snapshot.originChannel || "sdk",
        userId: snapshot.userId || defaultUserId(),
        workspacePath: snapshot.workspacePath || undefined,
      }, restoration);
      thread.bindDynamicTools(options.dynamicTools);
    });
    return thread;
  }

  async fork(threadId: string, options: ForkThreadOptions = {}): Promise<DotCraftThread> {
    let thread!: DotCraftThread;
    await this.sdk.wire.threadFork(threadId, {
      ...options,
      dynamicTools: stripRuntimeDynamicToolHandlers(options.dynamicTools),
    }, (snapshot) => {
      thread = new DotCraftThread(this.sdk, snapshot, {
        channelName: snapshot.originChannel || "sdk",
        userId: snapshot.userId || defaultUserId(),
        workspacePath: snapshot.workspacePath || undefined,
      });
      thread.bindDynamicTools(options.dynamicTools);
    });
    return thread;
  }

  async list(options: ListThreadOptions = {}): Promise<ThreadSummary[]> {
    const page = await this.listPage(options);
    return page.data;
  }

  async listPage(options: ListThreadOptions = {}): Promise<ThreadListResult> {
    const identity = normalizeIdentity(options);
    return await this.sdk.wire.threadListPage({
      ...identity,
      scope: options.scope,
      includeArchived: options.includeArchived ?? false,
      query: options.query,
      limit: options.limit,
      cursor: options.cursor,
    });
  }

  async read(threadId: string): Promise<SessionThread> {
    return await this.sdk.wire.threadRead(threadId);
  }

  async listTurns(threadId: string, options: ThreadHistoryPageOptions = {}): Promise<ThreadTurnsListResult> {
    return await this.sdk.wire.threadTurnsList({ threadId, ...options });
  }

  async listItems(threadId: string, options: ThreadItemPageOptions = {}): Promise<ThreadItemsListResult> {
    return await this.sdk.wire.threadItemsList({ threadId, ...options });
  }
}
