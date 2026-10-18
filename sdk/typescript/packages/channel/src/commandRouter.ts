import { JsonRpcError } from "@dotcraft/sdk/wire";
import type {
  CommandChoice,
  CommandExecuteResult,
  CommandInfo,
  SenderContext,
  SessionThread,
} from "@dotcraft/sdk/contracts";
import {
  replaceLeadingSlashTextWithCommandRef,
  resolveClient,
  type ChannelAdapterMessageOptions,
  type ClientProvider,
} from "./channelRuntime.js";

const DEFAULT_CATALOG_TTL_MS = 60_000;

export interface CommandRunRequest {
  opts: ChannelAdapterMessageOptions;
  threadId: string;
  sender: SenderContext;
}

export interface CommandChoiceContext {
  opts: ChannelAdapterMessageOptions;
  sender: SenderContext;
  threadId: string;
}

export interface CommandRouterOptions {
  client: ClientProvider;
  deliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
  enqueueMessage: (opts: ChannelAdapterMessageOptions) => void;
  switchThread: (thread: SessionThread, request: CommandRunRequest) => Promise<void>;
  renderChoices: (target: string, choices: CommandChoice[], context: CommandChoiceContext) => Promise<void>;
  catalogTtlMs?: number;
  now?: () => number;
}

export function commandWord(text: string): string {
  const first = text.trim().split(/\s+/, 1)[0] ?? "";
  return first.length > 1 && first.startsWith("/") ? first.slice(1).toLowerCase() : "";
}

function normalizeCommandName(name: unknown): string {
  return String(name ?? "").trim().replace(/^\//, "").toLowerCase();
}

export class CommandRouter {
  private readonly options: CommandRouterOptions;
  private readonly catalogTtlMs: number;
  private readonly now: () => number;
  private commands: CommandInfo[] = [];
  private names = new Set<string>();
  private loadedAt: number | undefined;
  private loading: Promise<void> | undefined;

  constructor(options: CommandRouterOptions) {
    this.options = options;
    this.catalogTtlMs = options.catalogTtlMs ?? DEFAULT_CATALOG_TTL_MS;
    this.now = options.now ?? Date.now;
  }

  invalidate(): void {
    this.loadedAt = undefined;
  }

  async refresh(): Promise<void> {
    this.loading ??= this.load().finally(() => {
      this.loading = undefined;
    });
    await this.loading;
  }

  async listCommands(): Promise<CommandInfo[]> {
    await this.ensureFresh();
    return this.commands;
  }

  async isCommand(text: string): Promise<boolean> {
    const name = commandWord(text);
    if (!name) return false;
    await this.ensureFresh();
    return this.names.has(name);
  }

  async run(request: CommandRunRequest): Promise<void> {
    const text = request.opts.text.trim();
    const [command = "", ...args] = text.split(/\s+/);
    const target = request.opts.channelContext ?? "";
    let result: CommandExecuteResult;
    try {
      result = await resolveClient(this.options.client).commandExecute({
        threadId: request.threadId,
        command: command.toLowerCase(),
        arguments: args.length > 0 ? args : undefined,
        sender: request.sender,
      });
    } catch (error) {
      if (!(error instanceof JsonRpcError)) throw error;
      await this.options.deliver(target, error.message || String(error), {});
      return;
    }

    if (result.expandedPrompt) {
      this.options.enqueueMessage({
        ...request.opts,
        inputParts: replaceLeadingSlashTextWithCommandRef(request.opts.inputParts, text),
        skipCommand: true,
      });
      return;
    }

    let threadId = request.threadId;
    if (result.thread?.id) {
      await this.options.switchThread(result.thread, request);
      threadId = result.thread.id;
    }
    if (result.message) await this.options.deliver(target, result.message, {});
    const choices = (result.choices ?? []).filter((choice) => Boolean(choice.command));
    if (choices.length > 0) {
      await this.options.renderChoices(target, choices, { opts: request.opts, sender: request.sender, threadId });
    }
  }

  private async ensureFresh(): Promise<void> {
    if (this.loadedAt !== undefined && this.now() - this.loadedAt <= this.catalogTtlMs) return;
    await this.refresh();
  }

  private async load(): Promise<void> {
    try {
      const commands = await resolveClient(this.options.client).commandList();
      const names = new Set<string>();
      for (const command of commands) {
        for (const name of [command.name, ...(command.aliases ?? [])]) {
          const normalized = normalizeCommandName(name);
          if (normalized) names.add(normalized);
        }
      }
      this.commands = commands;
      this.names = names;
      this.loadedAt = this.now();
    } catch (error) {
      console.warn("[channel] command catalog refresh failed:", error instanceof Error ? error.message : String(error));
    }
  }
}
