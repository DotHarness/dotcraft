/**
 * Module-aware ChannelAdapter runtime helpers and base class.
 */

import { ChannelAdapter, type ChannelAdapterOptions } from "./adapter.js";
import { DotCraftAppServerClient } from "./appServerClient.js";
import type { ModuleError, ModuleErrorCode } from "./lifecycle.js";
import type { WorkspaceContext } from "./module.js";
import { StdioTransport, Transport, TransportClosed } from "./transport.js";
import {
  ModuleConfigLoader,
  ModuleLifecycleState,
} from "./channelRuntime.js";

export {
  ConfigValidationError,
  loadJsonConfig,
  resolveConfigPath,
  resolveModuleStatePath,
  resolveModuleTempPath,
  type LoadJsonConfigResult,
} from "./channelRuntime.js";

export abstract class ModuleChannelAdapter<TConfig = unknown> extends ChannelAdapter {
  protected context: WorkspaceContext | undefined;
  protected loadedConfig: TConfig | undefined;
  private readonly moduleLifecycle = new ModuleLifecycleState();

  constructor(
    channelName: string,
    clientName: string,
    clientVersion: string,
    optOutNotifications: string[] = [],
    options?: ChannelAdapterOptions,
  ) {
    super(new PlaceholderTransport(), channelName, clientName, clientVersion, optOutNotifications, options);
  }

  protected getConfigFileName(context: WorkspaceContext): string {
    return `${context.channelName}.json`;
  }

  protected abstract validateConfig(rawConfig: unknown): asserts rawConfig is TConfig;

  protected abstract buildTransportFromConfig(config: TConfig): Transport;

  async startWithContext(context: WorkspaceContext): Promise<void> {
    this.context = context;
    this.setStatus("starting");

    const configLoader = new ModuleConfigLoader<TConfig>({
      getConfigFileName: (ctx) => this.getConfigFileName(ctx),
      validateConfig: (rawConfig): asserts rawConfig is TConfig => this.validateConfig(rawConfig),
    });
    const loaded = await configLoader.load(context);
    if (loaded.status !== "loaded") {
      this.setStatus(loaded.status, loaded.error);
      return;
    }
    this.loadedConfig = loaded.config;

    try {
      const transport = loaded.stdioRuntime
        ? new StdioTransport()
        : this.buildTransportFromConfig(loaded.config);
      this.client = new DotCraftAppServerClient(transport);
      await super.start();
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.setStatus("stopped", this.buildModuleError("startupFailed", message));
    }
  }

  protected signalAuthRequired(error?: Partial<ModuleError>): void {
    this.setStatus("authRequired", this.buildStatusError("authRequired", error));
  }

  protected signalAuthExpired(error?: Partial<ModuleError>): void {
    this.setStatus("authExpired", this.buildStatusError("authExpired", error));
  }

  private buildStatusError(code: "authRequired" | "authExpired", error?: Partial<ModuleError>): ModuleError {
    return this.moduleLifecycle.buildStatusError(code, error);
  }

  private buildModuleError(code: ModuleErrorCode, message: string): ModuleError {
    return this.moduleLifecycle.buildModuleError(code, message);
  }
}

class PlaceholderTransport implements Transport {
  async readMessage(): Promise<Record<string, unknown>> {
    throw new TransportClosed("ModuleChannelAdapter transport not configured");
  }

  async writeMessage(_msg: Record<string, unknown>): Promise<void> {
    throw new TransportClosed("ModuleChannelAdapter transport not configured");
  }

  async close(): Promise<void> {}
}
