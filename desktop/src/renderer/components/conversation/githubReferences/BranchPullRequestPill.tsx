import { useCallback, useEffect, useMemo, type JSX, type MouseEvent } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useBranchPullRequest, useGithubRefStore, type GithubRefLoad } from '../../../stores/githubRefStore'
import { githubRefUrl } from '../../../../shared/githubReferences'
import { GithubRefGlyph, pullRequestLabel } from './githubRefState'
import { openGithubLink } from './openGithubLink'
import { useGithubRefHoverCard } from './useGithubRefHoverCard'

const OPEN_PULL_REFRESH_MS = 30_000

export function BranchPullRequestPill({ gitPath, branch }: { gitPath: string; branch: string | null }): JSX.Element | null {
  const t = useT()
  const enabled = Boolean(gitPath && branch)
  const pull = useBranchPullRequest(gitPath, branch, enabled)
  const pullOpen = pull != null && pull.state !== 'merged' && pull.state !== 'closed'
  const label = pull ? pullRequestLabel(pull, t) : ''
  const openLink = (url: string, event: MouseEvent<HTMLElement>): void => {
    void openGithubLink(url, event, t)
  }
  const load = useMemo<GithubRefLoad>(() => (pull ? { status: 'ready', ref: pull } : { status: 'loading' }), [pull])
  const revalidate = useCallback(() => {
    if (branch) void useGithubRefStore.getState().ensureBranchPull(gitPath, branch)
  }, [branch, gitPath])
  const { anchorProps, card } = useGithubRefHoverCard<HTMLButtonElement>({
    load,
    label,
    preferredSide: 'above',
    onOpenLink: openLink,
    onOpen: revalidate,
    overComposer: true
  })

  useEffect(() => {
    if (!enabled || !branch) return
    const refresh = (): void => {
      void useGithubRefStore.getState().ensureBranchPull(gitPath, branch, { force: true })
    }
    window.addEventListener('focus', refresh)
    return () => window.removeEventListener('focus', refresh)
  }, [branch, enabled, gitPath])

  useEffect(() => {
    if (!enabled || !branch || !pullOpen) return
    const timer = window.setInterval(() => {
      if (document.hasFocus()) void useGithubRefStore.getState().ensureBranchPull(gitPath, branch, { force: true })
    }, OPEN_PULL_REFRESH_MS)
    return () => window.clearInterval(timer)
  }, [branch, enabled, gitPath, pullOpen])

  if (!pull) return null

  return (
    <>
      <button
        type="button"
        className="dc-composer-pill dc-gh-branch-pill"
        aria-label={label}
        {...anchorProps}
        onClick={(event) => openLink(githubRefUrl(pull), event)}
      >
        <GithubRefGlyph refData={pull} size={17} />
        <span>#{pull.number}</span>
      </button>
      {card}
    </>
  )
}
