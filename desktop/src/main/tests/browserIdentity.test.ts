import { describe, expect, it, vi } from 'vitest'
import {
  buildAcceptLanguageHeader,
  buildAcceptLanguages,
  cleanEmbeddedBrowserUserAgent,
  configureEmbeddedBrowserIdentity
} from '../browserIdentity'

describe('cleanEmbeddedBrowserUserAgent', () => {
  it('removes only Electron and app product tokens while preserving the runtime platform', () => {
    const cases = [
      [
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/148.0.7778.97 Electron/42.0.1 DotCraft/0.7.2 Safari/537.36',
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/148.0.7778.97 Safari/537.36'
      ],
      [
        'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 Electron/42.0.1 Chrome/148.0.7778.97 Safari/537.36',
        'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 Chrome/148.0.7778.97 Safari/537.36'
      ],
      [
        'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/148.0.7778.97 Safari/537.36 DotCraft/0.7.2',
        'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/148.0.7778.97 Safari/537.36'
      ]
    ]
    for (const [source, expected] of cases) {
      expect(cleanEmbeddedBrowserUserAgent(source, 'DotCraft')).toBe(expected)
    }
  })
})

describe('accept languages', () => {
  it('builds the deduplicated session list and weighted header in the same order, with a stable fallback', () => {
    expect(buildAcceptLanguages(['zh-CN', 'en-US', 'ZH-cn', 'ja'], 'en-US')).toBe('zh-CN,en-US,ja')
    expect(buildAcceptLanguageHeader(['zh-CN', 'en-US', 'ZH-cn', 'ja'], 'en-US')).toBe('zh-CN, en-US;q=0.9, ja;q=0.8')
    expect(buildAcceptLanguages([], '')).toBe('en-US')
  })
})

describe('configureEmbeddedBrowserIdentity', () => {
  it('configures the session and all network request headers consistently', () => {
    let beforeSendHeaders!: (
      details: { requestHeaders: Record<string, string> },
      callback: (response: { requestHeaders?: Record<string, string | string[]> }) => void
    ) => void
    const setUserAgent = vi.fn()
    const browserSession = {
      getUserAgent: () => 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/148.0.7778.97 Electron/42.0.1 DotCraft/0.7.2 Safari/537.36',
      setUserAgent,
      webRequest: {
        onBeforeSendHeaders: vi.fn((listener) => { beforeSendHeaders = listener })
      }
    } as unknown as Electron.Session

    const identity = configureEmbeddedBrowserIdentity(browserSession, {
      appName: 'DotCraft',
      preferredLanguages: ['zh-CN', 'en-US'],
      fallbackLocale: 'en-US'
    })

    expect(setUserAgent).toHaveBeenCalledOnce()
    expect(setUserAgent).toHaveBeenCalledWith(identity.userAgent, 'zh-CN,en-US')
    expect(identity.userAgent).toContain('Chrome/148.0.7778.97')
    expect(identity.userAgent).not.toMatch(/Electron|DotCraft/)

    const callback = vi.fn()
    beforeSendHeaders({
      requestHeaders: {
        'user-agent': 'stale',
        'accept-language': 'stale',
        'sec-ch-ua': '"Chromium";v="148"'
      }
    }, callback)

    expect(callback).toHaveBeenCalledWith({
      requestHeaders: {
        'User-Agent': identity.userAgent,
        'Accept-Language': identity.acceptLanguageHeader,
        'sec-ch-ua': '"Chromium";v="148"'
      }
    })
  })
})
