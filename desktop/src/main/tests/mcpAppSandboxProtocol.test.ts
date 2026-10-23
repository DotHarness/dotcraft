import { beforeEach, describe, expect, it, vi } from 'vitest'

const { protocolHandleMock, protocolRegisterSchemesAsPrivilegedMock } = vi.hoisted(() => ({
  protocolHandleMock: vi.fn(),
  protocolRegisterSchemesAsPrivilegedMock: vi.fn()
}))

vi.mock('electron', () => ({
  protocol: {
    registerSchemesAsPrivileged: protocolRegisterSchemesAsPrivilegedMock,
    handle: protocolHandleMock
  }
}))

import {
  MCP_APP_SANDBOX_PROXY_HTML,
  handleMcpAppSandboxRequest,
  registerMcpAppSandboxScheme
} from '../mcpAppSandboxProtocol'
import {
  MCP_APP_SANDBOX_PROXY_URL,
  MCP_APP_SANDBOX_SCHEME
} from '../../shared/mcpAppSandbox'

beforeEach(() => {
  protocolHandleMock.mockReset()
  protocolRegisterSchemesAsPrivilegedMock.mockReset()
})

describe('MCP App sandbox protocol', () => {
  it('registers a secure internal scheme without file or fetch privileges', () => {
    registerMcpAppSandboxScheme()

    expect(protocolRegisterSchemesAsPrivilegedMock).toHaveBeenCalledWith([{
      scheme: MCP_APP_SANDBOX_SCHEME,
      privileges: {
        standard: true,
        secure: true,
        supportFetchAPI: false,
        bypassCSP: false,
        stream: false,
        corsEnabled: false
      }
    }])
  })

  it('serves only the fixed trusted proxy document', async () => {
    const response = await handleMcpAppSandboxRequest(new Request(MCP_APP_SANDBOX_PROXY_URL))

    expect(response.status).toBe(200)
    expect(response.headers.get('Content-Type')).toBe('text/html; charset=utf-8')
    expect(response.headers.get('Cache-Control')).toBe('no-store')
    expect(response.headers.get('Content-Security-Policy')).toBeNull()
    expect(await response.text()).toBe(MCP_APP_SANDBOX_PROXY_HTML)

    const rejected = await handleMcpAppSandboxRequest(
      new Request(`${MCP_APP_SANDBOX_SCHEME}://proxy/arbitrary.html`)
    )
    expect(rejected.status).toBe(404)
  })
})
