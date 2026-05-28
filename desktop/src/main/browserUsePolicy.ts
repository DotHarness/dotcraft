import type { BrowserUseSettings } from './settings'

const LOCAL_HOSTS = new Set(['localhost', '127.0.0.1', '::1', '[::1]'])
const VIEWER_SCHEME = 'dotcraft-viewer:'

export type BrowserUseNavigationDecision =
  | { kind: 'allow'; local: boolean; domain?: string }
  | { kind: 'needs-approval'; domain: string }
  | { kind: 'block'; domain?: string; reason: string }

export function normalizeBrowserUseDomainInput(input: string): string | null {
  const trimmed = input.trim()
  if (!trimmed || /[\u0000-\u001f]/.test(trimmed)) return null

  const candidate = /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)
    ? trimmed
    : `https://${trimmed}`
  try {
    const parsed = new URL(candidate)
    const hostname = parsed.hostname.trim().toLowerCase().replace(/\.+$/, '')
    return hostname || null
  } catch {
    return null
  }
}

export function normalizeBrowserUseDomainList(value: unknown): string[] {
  if (!Array.isArray(value)) return []
  const seen = new Set<string>()
  const result: string[] = []
  for (const item of value) {
    if (typeof item !== 'string') continue
    const normalized = normalizeBrowserUseDomainInput(item)
    if (!normalized || seen.has(normalized)) continue
    seen.add(normalized)
    result.push(normalized)
  }
  return result
}

export function isBrowserUseLocalUrl(url: string): boolean {
  if (url === 'about:blank') return true
  try {
    const parsed = new URL(url)
    if (parsed.protocol === 'file:' || parsed.protocol === VIEWER_SCHEME) return true
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return false
    return LOCAL_HOSTS.has(parsed.hostname.toLowerCase())
  } catch {
    return false
  }
}

export function isBrowserUseUrlAllowed(url: string, settings?: BrowserUseSettings): boolean {
  return resolveBrowserUseNavigationDecision(url, settings).kind === 'allow'
}

export function domainMatchesBrowserUseRule(hostname: string, rule: string): boolean {
  const host = hostname.toLowerCase().replace(/\.+$/, '')
  const normalizedRule = rule.toLowerCase().replace(/\.+$/, '')
  return host === normalizedRule || host.endsWith(`.${normalizedRule}`)
}

export function resolveBrowserUseNavigationDecision(
  url: string,
  settings?: BrowserUseSettings
): BrowserUseNavigationDecision {
  if (isBrowserUseLocalUrl(url)) return { kind: 'allow', local: true }

  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    return { kind: 'block', reason: `Invalid browser URL: ${url}` }
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    return { kind: 'block', reason: `Blocked browser scheme: ${parsed.protocol || 'unknown'}` }
  }

  const domain = normalizeBrowserUseDomainInput(parsed.toString())
  if (!domain) {
    return { kind: 'block', reason: `Invalid browser domain: ${url}` }
  }

  const blockedDomains = normalizeBrowserUseDomainList(settings?.blockedDomains)
  if (blockedDomains.some((rule) => domainMatchesBrowserUseRule(domain, rule))) {
    return { kind: 'block', domain, reason: `Blocked browser domain: ${domain}` }
  }

  const allowedDomains = normalizeBrowserUseDomainList(settings?.allowedDomains)
  if (allowedDomains.some((rule) => domainMatchesBrowserUseRule(domain, rule))) {
    return { kind: 'allow', local: false, domain }
  }

  const approvalMode = settings?.approvalMode === 'neverAsk'
    ? 'neverAsk'
    : settings?.approvalMode === 'askUnknown'
      ? 'askUnknown'
      : 'alwaysAsk'
  if (approvalMode === 'neverAsk') return { kind: 'allow', local: false, domain }

  return { kind: 'needs-approval', domain }
}
