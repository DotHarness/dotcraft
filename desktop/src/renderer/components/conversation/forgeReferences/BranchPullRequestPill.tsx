import { useCallback, useEffect, useMemo, type JSX, type MouseEvent } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useBranchPullRequest, useForgeRefStore, type ForgeRefLoad } from '../../../stores/forgeRefStore'
import { forgeRefNumberLabel, forgeRefUrl } from '../../../../shared/forgeReferences'
import { ForgeRefGlyph, pullRequestLabel } from './forgeRefState'
import { openForgeLink } from './openForgeLink'
import { useForgeRefHoverCard } from './useForgeRefHoverCard'

const OPEN_PULL_REFRESH_MS = 30_000

export function BranchPullRequestPill({ gitPath, branch }: { gitPath: string; branch: string | null }): JSX.Element | null {
  const t = useT()
  const enabled = Boolean(gitPath && branch)
  const pull = useBranchPullRequest(gitPath, branch, enabled)
  const pullOpen = pull != null && pull.state !== 'merged' && pull.state !== 'closed'
  const label = pull ? pullRequestLabel(pull, t) : ''
  const openLink = (url: string, event: MouseEvent<HTMLElement>): void => {
    void openForgeLink(url, event, t)
  }
  const load = useMemo<ForgeRefLoad>(() => (pull ? { status: 'ready', ref: pull } : { status: 'loading' }), [pull])
  const revalidate = useCallback(() => {
    if (branch) void useForgeRefStore.getState().ensureBranchPull(gitPath, branch)
  }, [branch, gitPath])
  const { anchorProps, card } = useForgeRefHoverCard<HTMLButtonElement>({
    repo: pull,
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
      void useForgeRefStore.getState().ensureBranchPull(gitPath, branch, { force: true })
    }
    window.addEventListener('focus', refresh)
    return () => window.removeEventListener('focus', refresh)
  }, [branch, enabled, gitPath])

  useEffect(() => {
    if (!enabled || !branch || !pullOpen) return
    const timer = window.setInterval(() => {
      if (document.hasFocus()) void useForgeRefStore.getState().ensureBranchPull(gitPath, branch, { force: true })
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
        onClick={(event) => openLink(forgeRefUrl(pull), event)}
      >
        <ForgeRefGlyph refData={pull} size={17} />
        <span>{forgeRefNumberLabel(pull)}</span>
      </button>
      {card}
    </>
  )
}
