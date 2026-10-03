import { NativeModule, requireNativeModule } from 'expo'

interface PinnedRequestOptions {
  url: string
  method: string
  headers: Record<string, string>
  body?: string
  fingerprint: string
  tunnel?: string
}

interface PinnedSocketOptions {
  url: string
  headers: Record<string, string>
  fingerprint: string
  tunnel?: string
}

type PinnedTransportEvents = {
  open(event: { id: string }): void
  message(event: { id: string; text: string }): void
  close(event: { id: string; code: number; reason: string }): void
  error(event: { id: string; code: string; message: string; status: number | null }): void
}

declare class PinnedTransportModule extends NativeModule<PinnedTransportEvents> {
  request(options: PinnedRequestOptions): Promise<{ status: number; body: string }>
  openSocket(options: PinnedSocketOptions): string
  send(id: string, text: string): void
  close(id: string, code: number, reason: string): void
}

export default requireNativeModule<PinnedTransportModule>('PinnedTransport')
