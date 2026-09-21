export interface EmbeddedBrowserIdentity {
  userAgent: string
  acceptLanguages: string
  acceptLanguageHeader: string
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

export function cleanEmbeddedBrowserUserAgent(userAgent: string, appName: string): string {
  const productNames = ['Electron', appName.trim()].filter(Boolean)
  let cleaned = userAgent
  for (const productName of productNames) {
    cleaned = cleaned.replace(
      new RegExp(`(?:^|\\s)${escapeRegExp(productName)}\\/[^\\s]+`, 'gi'),
      ' '
    )
  }
  cleaned = cleaned.replace(/\s+/g, ' ').trim()
  return cleaned || userAgent.trim()
}

function normalizeLanguages(preferredLanguages: readonly string[], fallbackLocale: string): string[] {
  const languages: string[] = []
  const seen = new Set<string>()
  for (const rawLanguage of [...preferredLanguages, fallbackLocale]) {
    const language = rawLanguage.trim()
    const key = language.toLowerCase()
    if (!language || seen.has(key)) continue
    seen.add(key)
    languages.push(language)
  }
  return languages.length > 0 ? languages : ['en-US']
}

export function buildAcceptLanguages(preferredLanguages: readonly string[], fallbackLocale: string): string {
  return normalizeLanguages(preferredLanguages, fallbackLocale).join(',')
}

export function buildAcceptLanguageHeader(preferredLanguages: readonly string[], fallbackLocale: string): string {
  return normalizeLanguages(preferredLanguages, fallbackLocale)
    .map((language, index) => {
      if (index === 0) return language
      const quality = Math.max(0.1, 1 - index * 0.1).toFixed(1)
      return `${language};q=${quality}`
    })
    .join(', ')
}

function setHeader(headers: Record<string, string>, name: string, value: string): void {
  for (const key of Object.keys(headers)) {
    if (key.toLowerCase() === name.toLowerCase()) delete headers[key]
  }
  headers[name] = value
}

export function configureEmbeddedBrowserIdentity(
  browserSession: Electron.Session,
  options: {
    appName: string
    preferredLanguages: readonly string[]
    fallbackLocale: string
  }
): EmbeddedBrowserIdentity {
  const identity = {
    userAgent: cleanEmbeddedBrowserUserAgent(browserSession.getUserAgent(), options.appName),
    acceptLanguages: buildAcceptLanguages(options.preferredLanguages, options.fallbackLocale),
    acceptLanguageHeader: buildAcceptLanguageHeader(options.preferredLanguages, options.fallbackLocale)
  }
  browserSession.setUserAgent(identity.userAgent, identity.acceptLanguages)
  browserSession.webRequest.onBeforeSendHeaders((details, callback) => {
    const requestHeaders = { ...details.requestHeaders }
    setHeader(requestHeaders, 'User-Agent', identity.userAgent)
    setHeader(requestHeaders, 'Accept-Language', identity.acceptLanguageHeader)
    callback({ requestHeaders })
  })
  return identity
}
