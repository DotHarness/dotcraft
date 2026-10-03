export class TransportError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TransportError";
  }
}

export class TransportClosed extends TransportError {
  constructor(message = "Transport closed") {
    super(message);
    this.name = "TransportClosed";
  }
}

export interface Transport {
  readMessage(): Promise<Record<string, unknown>>;
  writeMessage(msg: Record<string, unknown>): Promise<void>;
  close(): Promise<void>;
  connect?(): Promise<void>;
}
