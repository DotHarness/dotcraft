import { useEffect, useState, type JSX, type MouseEvent, type ReactNode } from 'react'
import { CircleCheck, CircleX, CloudOff, Download, Gauge, KeyRound } from 'lucide-react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import type { ForgeRefLoad } from '../../../stores/forgeRefStore'
import { formatRelativeTime } from '../../../utils/relativeTime'
import {
  forgeChangesUrl,
  forgeRefNumberLabel,
  forgeRefUrl,
  type ForgeChecks,
  type ForgeRefError,
  type ForgeRefSummary,
  type ForgeRepo
} from '../../../../shared/forgeReferences'
import { Skeleton } from '../../ui/Skeleton'
import { Spinner } from '../../ui/Spinner'
import { FileDiffStats } from '../FileDiffStats'
import { checksLabel, forgeRefState, liveChecks } from './forgeRefState'

type OpenLink = (url: string, event: MouseEvent<HTMLElement>) => void
type Translate = (key: string, vars?: Record<string, string | number>) => string

const CHECK_ICONS = { passing: CircleCheck, failing: CircleX }

function ChecksIcon({ state }: { state: ForgeChecks['state'] }): JSX.Element | null {
  if (state === 'pending') return <Spinner size={14} />
  if (state === 'none') return null
  const Icon = CHECK_ICONS[state]
  return <Icon size={14} strokeWidth={1.9} aria-hidden />
}

const ERROR_ICONS = { notInstalled: Download, auth: KeyRound, rateLimit: Gauge, unavailable: CloudOff } satisfies Record<ForgeRefError, unknown>

const avatarRequests = new Map<string, Promise<string | null>>()

function requestAvatar(key: string, load: () => Promise<string | null>): Promise<string | null> {
  let request = avatarRequests.get(key)
  if (!request) {
    request = Promise.resolve().then(load).catch(() => null)
    avatarRequests.set(key, request)
  }
  return request
}

function useAuthorAvatar({ provider, host, author, authorAvatarUrl }: ForgeRefSummary): string | null {
  const [avatar, setAvatar] = useState<string | null>(null)
  useEffect(() => {
    let live = true
    setAvatar(null)
    const request = provider === 'github'
      ? requestAvatar(`github|${author.toLowerCase()}`, () =>
          window.api.profile.getGithubIdentity(author).then((identity) => identity?.avatarDataUrl ?? null))
      : authorAvatarUrl
        ? requestAvatar(`${host}|${authorAvatarUrl}`, () => window.api.forge.getAuthorAvatar(host, authorAvatarUrl))
        : null
    void request?.then((url) => {
      if (live) setAvatar(url)
    })
    return () => {
      live = false
    }
  }, [author, authorAvatarUrl, host, provider])
  return avatar
}

function AuthorAvatar({ refData }: { refData: ForgeRefSummary }): JSX.Element {
  const avatar = useAuthorAvatar(refData)
  return avatar
    ? <img className="dc-gh-card__avatar" src={avatar} alt="" aria-hidden />
    : <span className="dc-gh-card__avatar" aria-hidden>{refData.author.slice(0, 1).toUpperCase()}</span>
}

function CardLink({ href, className, onOpenLink, children }: {
  href: string
  className: string
  onOpenLink: OpenLink
  children: ReactNode
}): JSX.Element {
  return (
    <a
      href={href}
      className={className}
      draggable={false}
      onClick={(event) => {
        event.preventDefault()
        onOpenLink(href, event)
      }}
    >
      {children}
    </a>
  )
}

function ChangeStats({ refData, onOpenLink }: { refData: ForgeRefSummary; onOpenLink: OpenLink }): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  if (refData.kind !== 'pull') return null
  const { additions, deletions, changedFiles } = refData
  if (additions === null && deletions === null && changedFiles === null) return null
  return (
    <CardLink href={forgeChangesUrl(refData)} className="dc-gh-card__stats" onOpenLink={onOpenLink}>
      <FileDiffStats additions={additions ?? 0} deletions={deletions ?? 0} />
      {changedFiles !== null && (
        <span>
          {t(changedFiles === 1 ? 'forgeRef.files.one' : 'forgeRef.files.other', {
            count: changedFiles.toLocaleString(locale)
          })}
        </span>
      )}
    </CardLink>
  )
}

function ReadyBody({ refData, onOpenLink }: { refData: ForgeRefSummary; onOpenLink: OpenLink }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const { Icon, tone, labelKey } = forgeRefState(refData)
  const url = forgeRefUrl(refData)
  const checks = liveChecks(refData)
  return (
    <>
      <div className="dc-gh-card__header">
        <span className="dc-gh-card__state" data-tone={tone}>
          <Icon size={12} strokeWidth={2.1} aria-hidden />
          {t(labelKey)}
        </span>
        <CardLink href={url} className="dc-gh-card__repo" onOpenLink={onOpenLink}>
          <span className="dc-gh-card__repo-name">{refData.path}</span>
          <span>&nbsp;{forgeRefNumberLabel(refData)}</span>
        </CardLink>
        {refData.createdAt && (
          <span className="dc-gh-card__time">{formatRelativeTime(refData.createdAt, new Date(), locale)}</span>
        )}
      </div>
      <CardLink href={url} className="dc-gh-card__title" onOpenLink={onOpenLink}>
        {refData.title}
      </CardLink>
      {checks && (
        <div className="dc-gh-card__checks" data-checks={checks.state}>
          <ChecksIcon state={checks.state} />
          <span>{checksLabel(checks, t)}</span>
          {checks.counts && (
            <span className="dc-gh-card__checks-count">
              {t('forgeRef.checksCount', { passed: checks.counts.passed, total: checks.counts.total })}
            </span>
          )}
        </div>
      )}
      <div className="dc-gh-card__footer">
        <span className="dc-gh-card__author">
          <AuthorAvatar refData={refData} />
          <span className="dc-gh-card__author-name">{refData.author}</span>
        </span>
        <ChangeStats refData={refData} onOpenLink={onOpenLink} />
      </div>
    </>
  )
}

function LoadingBody(): JSX.Element {
  const t = useT()
  return (
    <div className="dc-gh-card__loading" role="status" aria-label={t('forgeRef.loading')}>
      <div className="dc-gh-card__header">
        <Skeleton width={56} height={18} radius={6} />
        <Skeleton width={128} height={10} />
      </div>
      <Skeleton width="92%" height={12} />
      <Skeleton width="58%" height={12} />
      <div className="dc-gh-card__footer">
        <span className="dc-gh-card__author">
          <Skeleton width={16} height={16} circle />
          <Skeleton width={72} height={10} />
        </span>
        <Skeleton width={96} height={10} />
      </div>
    </div>
  )
}

function errorMessage(repo: ForgeRepo | null, error: ForgeRefError, resetsAt: number | undefined, t: Translate): string {
  if (error === 'unavailable' || !repo) return t('forgeRef.error.unavailable')
  if (repo.provider === 'gitlab') {
    return t(`forgeRef.error.gitlab.${error}`, { host: repo.host })
  }
  if (error !== 'rateLimit') return t(`forgeRef.error.github.${error}`)
  const minutes = resetsAt ? Math.ceil((resetsAt - Date.now()) / 60_000) : 0
  return minutes > 0
    ? t('forgeRef.error.github.rateLimit.minutes', { minutes })
    : t('forgeRef.error.github.rateLimit.later')
}

function ErrorBody({ repo, error, resetsAt }: { repo: ForgeRepo | null; error: ForgeRefError; resetsAt?: number }): JSX.Element {
  const t = useT()
  const Icon = ERROR_ICONS[error]
  return (
    <p className="dc-gh-card__error">
      <Icon size={14} strokeWidth={1.8} aria-hidden />
      <span>{errorMessage(repo, error, resetsAt, t)}</span>
    </p>
  )
}

export function ForgeRefCardBody({ repo, load, onOpenLink }: { repo: ForgeRepo | null; load: ForgeRefLoad; onOpenLink: OpenLink }): JSX.Element {
  if (load.status === 'ready') return <ReadyBody refData={load.ref} onOpenLink={onOpenLink} />
  if (load.status === 'loading') return <LoadingBody />
  return <ErrorBody repo={repo} error={load.error} resetsAt={load.resetsAt} />
}
