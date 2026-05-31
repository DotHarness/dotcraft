import { SUPPORTED_LOCALE_VALUES, type AppLocale, type EnglishRequiredLocalizedText } from './locales'

export type WhatsNewIcon = 'message' | 'dreams' | 'goal' | 'teams' | 'app' | 'subscription'
export const WHATS_NEW_ICONS: readonly WhatsNewIcon[] = [
  'message',
  'dreams',
  'goal',
  'teams',
  'app',
  'subscription'
]

export type LocalizedWhatsNewText = EnglishRequiredLocalizedText

export interface WhatsNewMedia {
  fileName: string
  url: string
  sizeBytes: number
  sha256: string
}

export interface WhatsNewCard {
  id: string
  icon: WhatsNewIcon
  title: LocalizedWhatsNewText
  summary: LocalizedWhatsNewText
  media?: WhatsNewMedia
  docsUrl?: string
}

export interface WhatsNewRelease {
  version: string
  cards: WhatsNewCard[]
}

export const WHATS_NEW_MEDIA_BYTES_PER_CARD_LIMIT = 8 * 1024 * 1024
export const WHATS_NEW_REMOTE_MEDIA_BASE_URL =
  'https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/'

export type WhatsNewMediaStatus = 'missing' | 'downloading' | 'ready' | 'failed'

export interface WhatsNewMediaState {
  releaseVersion: string
  cardId: string
  status: WhatsNewMediaStatus
  cachedUrl?: string
  error?: string
}

const VERSION_PATTERN = /^(\d+)\.(\d+)\.(\d+)(?:[-+][0-9A-Za-z.-]+)?$/

export function isValidAppVersion(version: unknown): version is string {
  return typeof version === 'string' && VERSION_PATTERN.test(version.trim())
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function isWhatsNewIcon(value: unknown): value is WhatsNewIcon {
  return typeof value === 'string' && WHATS_NEW_ICONS.includes(value as WhatsNewIcon)
}

const SUPPORTED_LOCALE_SET = new Set<string>(SUPPORTED_LOCALE_VALUES)

function parseLocalizedWhatsNewText(value: unknown): LocalizedWhatsNewText | null {
  if (!isRecord(value)) return null
  const en = value.en
  if (typeof en !== 'string') return null
  const localized: Record<string, string> = { en }
  for (const [key, item] of Object.entries(value)) {
    if (key === 'en') continue
    if (!SUPPORTED_LOCALE_SET.has(key)) return null
    if (typeof item !== 'string') return null
    localized[key] = item
  }
  return localized as LocalizedWhatsNewText
}

function parseWhatsNewMedia(value: unknown): WhatsNewMedia | null {
  if (!isRecord(value)) return null
  const fileName = value.fileName
  const url = value.url
  const sizeBytes = value.sizeBytes
  const sha256 = value.sha256
  if (typeof fileName !== 'string' || !/^[a-z0-9._-]+\.gif$/.test(fileName)) return null
  if (typeof url !== 'string' || url !== `${WHATS_NEW_REMOTE_MEDIA_BASE_URL}${fileName}`) return null
  if (
    typeof sizeBytes !== 'number' ||
    !Number.isSafeInteger(sizeBytes) ||
    sizeBytes <= 0 ||
    sizeBytes > WHATS_NEW_MEDIA_BYTES_PER_CARD_LIMIT
  ) return null
  if (typeof sha256 !== 'string' || !/^[0-9a-fA-F]{64}$/.test(sha256)) return null
  return {
    fileName,
    url,
    sizeBytes,
    sha256: sha256.toUpperCase()
  }
}

function parseWhatsNewCard(value: unknown): WhatsNewCard | null {
  if (!isRecord(value)) return null
  const id = value.id
  const icon = value.icon
  const title = parseLocalizedWhatsNewText(value.title)
  const summary = parseLocalizedWhatsNewText(value.summary)
  if (typeof id !== 'string' || id.trim().length === 0) return null
  if (!isWhatsNewIcon(icon) || !title || !summary) return null

  const media = value.media === undefined ? undefined : parseWhatsNewMedia(value.media)
  if (value.media !== undefined && !media) return null
  const docsUrl = value.docsUrl
  if (docsUrl !== undefined && typeof docsUrl !== 'string') return null

  return {
    id: id.trim(),
    icon,
    title,
    summary,
    ...(media ? { media } : {}),
    ...(typeof docsUrl === 'string' && docsUrl.trim().length > 0 ? { docsUrl: docsUrl.trim() } : {})
  }
}

export function parseWhatsNewRelease(value: unknown): WhatsNewRelease | null {
  if (!isRecord(value)) return null
  const version = value.version
  const cards = value.cards
  if (!isValidAppVersion(version) || !Array.isArray(cards)) return null
  const parsedCards = cards.map(parseWhatsNewCard)
  if (parsedCards.some((card) => card == null)) return null
  return {
    version: version.trim(),
    cards: parsedCards as WhatsNewCard[]
  }
}

function versionCore(version: string): [number, number, number] | null {
  const match = VERSION_PATTERN.exec(version.trim())
  if (!match) return null
  return [Number(match[1]), Number(match[2]), Number(match[3])]
}

export function compareAppVersions(a: string, b: string): number {
  const left = versionCore(a)
  const right = versionCore(b)
  if (!left && !right) return a.localeCompare(b)
  if (!left) return -1
  if (!right) return 1
  for (let i = 0; i < left.length; i++) {
    const delta = left[i] - right[i]
    if (delta !== 0) return delta
  }
  return a.trim().localeCompare(b.trim())
}

export function sortWhatsNewReleasesNewestFirst(releases: WhatsNewRelease[]): WhatsNewRelease[] {
  return [...releases].sort((a, b) => compareAppVersions(b.version, a.version))
}

export function getWhatsNewReleasesUpTo(
  releases: WhatsNewRelease[],
  currentVersion: string
): WhatsNewRelease[] {
  if (!isValidAppVersion(currentVersion)) return []
  return sortWhatsNewReleasesNewestFirst(
    releases.filter((release) => compareAppVersions(release.version, currentVersion) <= 0)
  )
}

export function getUnseenWhatsNewReleases(
  releases: WhatsNewRelease[],
  currentVersion: string,
  lastSeenVersion: string | undefined | null
): WhatsNewRelease[] {
  const available = getWhatsNewReleasesUpTo(releases, currentVersion)
  if (!isValidAppVersion(lastSeenVersion)) return available
  return available.filter((release) => compareAppVersions(release.version, lastSeenVersion) > 0)
}

export function getLatestWhatsNewVersion(releases: WhatsNewRelease[]): string | undefined {
  return sortWhatsNewReleasesNewestFirst(releases)[0]?.version
}

export function getWhatsNewMediaStateKey(releaseVersion: string, cardId: string): string {
  return `${releaseVersion}:${cardId}`
}

export function getWhatsNewMediaStatesKey(state: Pick<WhatsNewMediaState, 'releaseVersion' | 'cardId'>): string {
  return getWhatsNewMediaStateKey(state.releaseVersion, state.cardId)
}

export function getWhatsNewReleasesByVersions(
  releases: WhatsNewRelease[],
  releaseVersions: string[]
): WhatsNewRelease[] {
  const requested = new Set(releaseVersions)
  return sortWhatsNewReleasesNewestFirst(releases.filter((release) => requested.has(release.version)))
}

export function getLocalizedWhatsNewText(
  value: LocalizedWhatsNewText,
  locale: AppLocale
): string {
  return value[locale] || value.en
}
