import { resolveClient, type ClientProvider } from "./channelRuntime.js";

export interface DeliveryDispatcherOptions {
  client: ClientProvider;
  onSend: (
    target: string,
    message: Record<string, unknown>,
    metadata: Record<string, unknown>,
  ) => Promise<Record<string, unknown>>;
  onError?: (message: string, error: unknown) => void;
}

export class DeliveryDispatcher {
  private readonly client: ClientProvider;
  private readonly onSend: (
    target: string,
    message: Record<string, unknown>,
    metadata: Record<string, unknown>,
  ) => Promise<Record<string, unknown>>;
  private readonly onError: (message: string, error: unknown) => void;
  private readonly deliveries = new Map<string, Promise<Record<string, unknown>>>();
  private static readonly rememberedDeliveries = 1024;

  constructor(options: DeliveryDispatcherOptions) {
    this.client = options.client;
    this.onSend = options.onSend;
    this.onError = options.onError ?? ((message, error) => console.error(message, error));
  }

  register(): void {
    const client = resolveClient(this.client);
    client.registerServerRequestHandler("ext/channel/send", async (_id, params) => {
      const deliveryId = String(params.deliveryId);
      const remembered = this.deliveries.get(deliveryId);
      if (remembered) return await remembered;

      const delivery = this.send(params);
      this.remember(deliveryId, delivery);
      const result = await delivery;
      if (result.delivered !== true) this.deliveries.delete(deliveryId);
      return result;
    });
  }

  private async send(params: Record<string, unknown>): Promise<Record<string, unknown>> {
    const target = String(params.target ?? "");
    const message = (params.message as Record<string, unknown>) ?? {};
    const metadata = (params.metadata as Record<string, unknown>) ?? {};
    try {
      return await this.onSend(target, message, metadata);
    } catch (error) {
      this.onError("onSend raised:", error);
      return {
        delivered: false,
        errorCode: "AdapterDeliveryFailed",
        errorMessage: String(error),
      };
    }
  }

  private remember(deliveryId: string, delivery: Promise<Record<string, unknown>>): void {
    this.deliveries.set(deliveryId, delivery);
    while (this.deliveries.size > DeliveryDispatcher.rememberedDeliveries) {
      const oldest = this.deliveries.keys().next().value;
      if (oldest === undefined) break;
      this.deliveries.delete(oldest);
    }
  }
}
