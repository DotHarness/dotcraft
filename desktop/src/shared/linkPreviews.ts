export type LinkPreviewError = 'unavailable' | 'blocked'

export type LinkPreviewResult =
  | { status: 'ready'; title: string | null; imageDataUrl?: string; faviconDataUrl?: string }
  | { status: 'error'; error: LinkPreviewError }

const LOCAL_HOST = /(^localhost$|\.localhost$|\.local$|\.internal$|\.test$)/i

export function bareHostname(hostname: string): string {
  return hostname.startsWith('[') && hostname.endsWith(']') ? hostname.slice(1, -1) : hostname
}

function isIpLiteral(hostname: string): boolean {
  const host = bareHostname(hostname)
  return host.includes(':') || /^\d{1,3}(\.\d{1,3}){3}$/.test(host)
}

export function isPreviewableUrl(url: string | undefined | null): boolean {
  if (!url) return false
  try {
    const parsed = new URL(url)
    return (
      parsed.protocol === 'https:' &&
      !parsed.username &&
      !parsed.password &&
      !LOCAL_HOST.test(parsed.hostname) &&
      !isIpLiteral(parsed.hostname)
    )
  } catch {
    return false
  }
}

export function linkPreviewKey(url: string): string {
  try {
    const parsed = new URL(url)
    parsed.hash = ''
    return parsed.href
  } catch {
    return url
  }
}

export function linkPreviewHost(url: string): string {
  try {
    const parsed = new URL(url)
    const host = parsed.hostname.replace(/^www\./i, '')
    return parsed.port ? `${host}:${parsed.port}` : host
  } catch {
    return url
  }
}
