import type { DesktopBindingClient } from '../desktopThreadBindings'

export interface AdditionalMcpServerConfig {
  name: string
  enabled: boolean
  transport: 'streamableHttp'
  url: string
  headers?: Record<string, string>
}

export interface ClientAdditionalMcpProvider {
  prepare(): Promise<void> | undefined
  getServers(): AdditionalMcpServerConfig[] | undefined
  getRevision(): string
}

const providers = new WeakMap<DesktopBindingClient, ClientAdditionalMcpProvider>()

export function registerClientAdditionalMcpProvider(
  client: DesktopBindingClient,
  provider: ClientAdditionalMcpProvider
): void {
  providers.set(client, provider)
}

export function unregisterClientAdditionalMcpProvider(client: DesktopBindingClient): void {
  providers.delete(client)
}

export function prepareClientAdditionalMcpServers(client: DesktopBindingClient): Promise<void> | undefined {
  return providers.get(client)?.prepare()
}

export function getClientAdditionalMcpServers(
  client: DesktopBindingClient
): AdditionalMcpServerConfig[] | undefined {
  return providers.get(client)?.getServers()
}

export function getClientAdditionalMcpRevision(client: DesktopBindingClient): string {
  return providers.get(client)?.getRevision() ?? ''
}
