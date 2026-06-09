import { describe, it, expect, beforeEach, vi } from 'vitest'

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
  DOTCRAFT_APP_SCHEME,
  registerDotCraftAppScheme,
  installDotCraftAppProtocolHandler,
  buildDotCraftAppUrl,
  buildInteractiveToolCsp,
  handleDotCraftAppRequest
} from '../dotcraftAppProtocol'

beforeEach(() => {
  protocolHandleMock.mockReset()
  protocolRegisterSchemesAsPrivilegedMock.mockReset()
})

function fakeClient(sendRequest: (...args: unknown[]) => unknown) {
  // Only `.sendRequest` is used by the handler.
  return (() => ({ sendRequest })) as never
}

describe('registerDotCraftAppScheme', () => {
  it('registers dotcraft-app as a privileged secure scheme', () => {
    registerDotCraftAppScheme()
    expect(protocolRegisterSchemesAsPrivilegedMock).toHaveBeenCalledWith([
      expect.objectContaining({
        scheme: DOTCRAFT_APP_SCHEME,
        privileges: expect.objectContaining({ standard: true, secure: true })
      })
    ])
  })
})

describe('buildDotCraftAppUrl', () => {
  it('encodes threadId, namespace, and uri', () => {
    const parsed = new URL(buildDotCraftAppUrl('thread_1', 'oratorio', 'ui://oratorio/board'))
    expect(parsed.protocol).toBe(`${DOTCRAFT_APP_SCHEME}:`)
    expect(parsed.hostname).toBe('resource')
    expect(parsed.searchParams.get('threadId')).toBe('thread_1')
    expect(parsed.searchParams.get('namespace')).toBe('oratorio')
    expect(parsed.searchParams.get('uri')).toBe('ui://oratorio/board')
  })

  it('omits an empty namespace', () => {
    const parsed = new URL(buildDotCraftAppUrl('thread_1', null, 'ui://x/y'))
    expect(parsed.searchParams.has('namespace')).toBe(false)
  })
})

describe('buildInteractiveToolCsp', () => {
  it('allows inline script/style but no network by default', () => {
    const csp = buildInteractiveToolCsp()
    expect(csp).toContain("default-src 'none'")
    expect(csp).toContain("script-src 'unsafe-inline'")
    expect(csp).toContain("style-src 'unsafe-inline'")
    expect(csp).not.toContain('connect-src')
  })
})

describe('handleDotCraftAppRequest', () => {
  it('brokers ui/resource/read and serves HTML with a per-resource CSP', async () => {
    const sendRequest = vi.fn().mockResolvedValue({
      contents: [{ uri: 'ui://oratorio/board', mimeType: 'text/html', text: '<!doctype html><body>ok</body>' }]
    })
    installDotCraftAppProtocolHandler(fakeClient(sendRequest))

    const url = buildDotCraftAppUrl('t1', 'oratorio', 'ui://oratorio/board')
    const res = await handleDotCraftAppRequest({ url } as Request)

    expect(res.status).toBe(200)
    expect(res.headers.get('Content-Security-Policy')).toContain("script-src 'unsafe-inline'")
    expect(await res.text()).toContain('ok')
    expect(sendRequest).toHaveBeenCalledWith(
      'ui/resource/read',
      { threadId: 't1', namespace: 'oratorio', uri: 'ui://oratorio/board' },
      expect.any(Number)
    )
  })

  it('rejects a non-ui:// uri without contacting the app', async () => {
    const sendRequest = vi.fn()
    installDotCraftAppProtocolHandler(fakeClient(sendRequest))
    const res = await handleDotCraftAppRequest({
      url: `${DOTCRAFT_APP_SCHEME}://resource/?threadId=t1&uri=https%3A%2F%2Fevil`
    } as Request)
    expect(res.status).toBe(400)
    expect(sendRequest).not.toHaveBeenCalled()
  })

  it('returns 503 when no app client is connected', async () => {
    installDotCraftAppProtocolHandler((() => null) as never)
    const res = await handleDotCraftAppRequest({
      url: buildDotCraftAppUrl('t1', 'oratorio', 'ui://oratorio/board')
    } as Request)
    expect(res.status).toBe(503)
  })
})
