import {
  CircleCheck,
  CircleDot,
  CircleSlash,
  CircleX,
  GitMerge,
  GitPullRequest,
  GitPullRequestClosed,
  GitPullRequestDraft,
  type LucideIcon
} from 'lucide-react'
import type { JSX } from 'react'
import type { GithubChecks, GithubPullSummary, GithubRefSummary } from '../../../../shared/githubReferences'

export type GithubTone = 'open' | 'merged' | 'closed' | 'draft' | 'conflict'

type Translate = (key: string, vars?: Record<string, string | number>) => string

interface GithubRefStatePresentation {
  Icon: LucideIcon
  tone: GithubTone
  labelKey: string
}

export function githubRefState(ref: GithubRefSummary): GithubRefStatePresentation {
  if (ref.kind === 'issue') {
    switch (ref.state) {
      case 'open':
        return { Icon: CircleDot, tone: 'open', labelKey: 'githubRef.issue.open' }
      case 'completed':
        return { Icon: CircleCheck, tone: 'merged', labelKey: 'githubRef.issue.completed' }
      case 'notPlanned':
        return { Icon: CircleSlash, tone: 'draft', labelKey: 'githubRef.issue.notPlanned' }
    }
  }
  switch (ref.state) {
    case 'draft':
      return { Icon: GitPullRequestDraft, tone: 'draft', labelKey: 'githubRef.pull.draft' }
    case 'approved':
      return { Icon: CircleCheck, tone: 'open', labelKey: 'githubRef.pull.approved' }
    case 'changesRequested':
      return { Icon: CircleX, tone: 'closed', labelKey: 'githubRef.pull.changesRequested' }
    case 'conflicting':
      return { Icon: GitPullRequest, tone: 'conflict', labelKey: 'githubRef.pull.conflicting' }
    case 'merged':
      return { Icon: GitMerge, tone: 'merged', labelKey: 'githubRef.pull.merged' }
    case 'closed':
      return { Icon: GitPullRequestClosed, tone: 'closed', labelKey: 'githubRef.pull.closed' }
    default:
      return { Icon: GitPullRequest, tone: 'open', labelKey: 'githubRef.pull.open' }
  }
}

export function liveChecks(ref: GithubRefSummary): GithubChecks | null {
  if (ref.kind !== 'pull' || ref.checks.state === 'none') return null
  return ref.state === 'merged' || ref.state === 'closed' ? null : ref.checks
}

export function checksLabel(checks: GithubChecks, t: Translate): string {
  if (checks.state === 'failing') {
    return t(checks.failed === 1 ? 'githubRef.checks.failing.one' : 'githubRef.checks.failing.other', { count: checks.failed })
  }
  return t(checks.state === 'pending' ? 'githubRef.checks.pending' : 'githubRef.checks.passing')
}

export function pullRequestLabel(pull: GithubPullSummary, t: Translate): string {
  const checks = liveChecks(pull)
  return [
    `${t('githubRef.pullRequest')} #${pull.number}`,
    t(githubRefState(pull).labelKey),
    checks ? checksLabel(checks, t) : null
  ]
    .filter(Boolean)
    .join(' · ')
}

export function GithubRefGlyph({ refData, size }: { refData: GithubRefSummary; size: number }): JSX.Element {
  const { Icon, tone } = githubRefState(refData)
  const checks = liveChecks(refData)
  return (
    <span
      className="dc-gh-glyph"
      data-tone={tone}
      data-ci={checks ? 'true' : undefined}
      style={{ width: size, height: size }}
    >
      <Icon size={checks ? size - 2 : size} strokeWidth={1.9} aria-hidden />
      {checks && <span className="dc-gh-glyph__ci" data-checks={checks.state} aria-hidden />}
    </span>
  )
}
