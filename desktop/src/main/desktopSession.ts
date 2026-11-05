import type { Session } from 'electron'
import { MCP_APP_SANDBOX_SCHEME } from '../shared/mcpAppSandbox'
import { handleMcpAppSandboxRequest } from './mcpAppSandboxProtocol'
import { configureVoiceMediaPermissions } from './voice/VoiceMicrophonePermissions'

const configured = new WeakSet<Session>()

export function configureDesktopSession(session: Session): void {
  if (configured.has(session)) return
  configured.add(session)
  configureVoiceMediaPermissions(session)
  session.protocol.handle(MCP_APP_SANDBOX_SCHEME, handleMcpAppSandboxRequest)
  if (import.meta.env.DEV) return
  session.webRequest.onHeadersReceived((details, callback) => {
    if (details.url.startsWith(`${MCP_APP_SANDBOX_SCHEME}:`)) {
      callback({ responseHeaders: details.responseHeaders })
      return
    }
    callback({
      responseHeaders: {
        ...details.responseHeaders,
        'Content-Security-Policy': [
          `default-src 'self' dotcraft-viewer: dotcraft-plugin:; script-src 'self' dotcraft-plugin:; style-src 'self' 'unsafe-inline' dotcraft-plugin:; img-src 'self' data: blob: file: dotcraft-viewer: dotcraft-plugin: https://t0.gstatic.com; font-src 'self' data: dotcraft-plugin:; connect-src 'self' dotcraft-viewer:; frame-src 'self' ${MCP_APP_SANDBOX_SCHEME}:`
        ]
      }
    })
  })
}
