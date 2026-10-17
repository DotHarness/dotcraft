import { useEffect, useState, type JSX, type MouseEvent, type ReactNode } from 'react'
import { CircleCheck, CircleX, CloudOff, Download, Gauge, KeyRound } from 'lucide-react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import type { GithubRefLoad } from '../../../stores/githubRefStore'
import { formatRelativeTime } from '../../../utils/relativeTime'
import { githubRefUrl, type GithubChecks, type GithubRefError, type GithubRefSummary } from '../../../../shared/githubReferences'
import { Skeleton } from '../../ui/Skeleton'
import { Spinner } from '../../ui/Spinner'
import { FileDiffStats } from '../FileDiffStats'
import { checksLabel, githubRefState, liveChecks } from './githubRefState'

type OpenLink = (url: string, event: MouseEvent<HTMLElement>) => void

const CHECK_ICONS = { passing: CircleCheck, failing: CircleX }

function ChecksIcon({ state }: { state: GithubChecks['state'] }): JSX.Element | null {
  if (state === 'pending') return <Spinner size={14} />
  if (state === 'none') return null
  const Icon = CHECK_ICONS[state]
  return <Icon size={14} strokeWidth={1.9} aria-hidden />
}

const ERROR_ICONS = { notInstalled: Download, auth: KeyRound, rateLimit: Gauge, unavailable: CloudOff } satisfies Record<GithubRefError, unknown>

const avatarRequests = new Map<string, Promise<string | null>>()

function useGithubAvatar(login: string): string | null {
  const [avatar, setAvatar] = useState<string | null>(null)
  useEffect(() => {
    let live = true
    const key = login.toLowerCase()
    let request = avatarRequests.get(key)
    if (!request) {
      request = Promise.resolve()
        .then(() => window.api.profile.getGithubIdentity(login))
        .then((identity) => identity?.avatarDataUrl ?? null, () => null)
      avatarRequests.set(key, request)
    }
    void request.then((url) => {
      if (live) setAvatar(url)
    })
    return () => {
      live = false
    }
  }, [login])
  return avatar
}

function AuthorAvatar({ login }: { login: string }): JSX.Element {
  const avatar = useGithubAvatar(login)
  return avatar
    ? <img className="dc-gh-card__avatar" src={avatar} alt="" aria-hidden />
    : <span className="dc-gh-card__avatar" aria-hidden>{login.slice(0, 1).toUpperCase()}</span>
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

function ReadyBody({ refData, onOpenLink }: { refData: GithubRefSummary; onOpenLink: OpenLink }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const { Icon, tone, labelKey } = githubRefState(refData)
  const url = githubRefUrl(refData)
  const checks = liveChecks(refData)
  return (
    <>
      <div className="dc-gh-card__header">
        <span className="dc-gh-card__state" data-tone={tone}>
          <Icon size={12} strokeWidth={2.1} aria-hidden />
          {t(labelKey)}
        </span>
        <CardLink href={url} className="dc-gh-card__repo" onOpenLink={onOpenLink}>
          <span className="dc-gh-card__repo-name">{refData.owner}/{refData.repo}</span>
          <span>&nbsp;#{refData.number}</span>
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
          <span className="dc-gh-card__checks-count">
            {t('githubRef.checksCount', { passed: checks.passed, total: checks.total })}
          </span>
        </div>
      )}
      <div className="dc-gh-card__footer">
        <span className="dc-gh-card__author">
          <AuthorAvatar login={refData.author} />
          <span className="dc-gh-card__author-name">{refData.author}</span>
        </span>
        {refData.kind === 'pull' && (
          <CardLink href={`${url}/files`} className="dc-gh-card__stats" onOpenLink={onOpenLink}>
            <FileDiffStats additions={refData.additions} deletions={refData.deletions} />
            <span>
              {t(refData.changedFiles === 1 ? 'githubRef.files.one' : 'githubRef.files.other', {
                count: refData.changedFiles.toLocaleString(locale)
              })}
            </span>
          </CardLink>
        )}
      </div>
    </>
  )
}

function LoadingBody(): JSX.Element {
  const t = useT()
  return (
    <div className="dc-gh-card__loading" role="status" aria-label={t('githubRef.loading')}>
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

function errorMessage(error: GithubRefError, resetsAt: number | undefined, t: (key: string, vars?: Record<string, string | number>) => string): string {
  if (error !== 'rateLimit') return t(`githubRef.error.${error}`)
  const minutes = resetsAt ? Math.ceil((resetsAt - Date.now()) / 60_000) : 0
  return minutes > 0
    ? t('githubRef.error.rateLimit.minutes', { minutes })
    : t('githubRef.error.rateLimit.later')
}

function ErrorBody({ error, resetsAt }: { error: GithubRefError; resetsAt?: number }): JSX.Element {
  const t = useT()
  const Icon = ERROR_ICONS[error]
  return (
    <p className="dc-gh-card__error">
      <Icon size={14} strokeWidth={1.8} aria-hidden />
      <span>{errorMessage(error, resetsAt, t)}</span>
    </p>
  )
}

export function GithubRefCardBody({ load, onOpenLink }: { load: GithubRefLoad; onOpenLink: OpenLink }): JSX.Element {
  if (load.status === 'ready') return <ReadyBody refData={load.ref} onOpenLink={onOpenLink} />
  if (load.status === 'loading') return <LoadingBody />
  return <ErrorBody error={load.error} resetsAt={load.resetsAt} />
}
