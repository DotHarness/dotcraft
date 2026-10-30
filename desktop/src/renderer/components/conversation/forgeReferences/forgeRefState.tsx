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
import { forgeRefNumberLabel, type ForgeChecks, type ForgePullSummary, type ForgeRefSummary } from '../../../../shared/forgeReferences'

export type ForgeTone = 'open' | 'merged' | 'closed' | 'draft' | 'conflict'

type Translate = (key: string, vars?: Record<string, string | number>) => string

interface ForgeRefStatePresentation {
  Icon: LucideIcon
  tone: ForgeTone
  labelKey: string
}

export function forgeRefState(ref: ForgeRefSummary): ForgeRefStatePresentation {
  if (ref.kind === 'issue') {
    switch (ref.state) {
      case 'open':
        return { Icon: CircleDot, tone: 'open', labelKey: 'forgeRef.issue.open' }
      case 'completed':
        return { Icon: CircleCheck, tone: 'merged', labelKey: 'forgeRef.issue.completed' }
      case 'notPlanned':
        return { Icon: CircleSlash, tone: 'draft', labelKey: 'forgeRef.issue.notPlanned' }
    }
  }
  switch (ref.state) {
    case 'draft':
      return { Icon: GitPullRequestDraft, tone: 'draft', labelKey: 'forgeRef.pull.draft' }
    case 'approved':
      return { Icon: CircleCheck, tone: 'open', labelKey: 'forgeRef.pull.approved' }
    case 'changesRequested':
      return { Icon: CircleX, tone: 'closed', labelKey: 'forgeRef.pull.changesRequested' }
    case 'conflicting':
      return { Icon: GitPullRequest, tone: 'conflict', labelKey: 'forgeRef.pull.conflicting' }
    case 'merged':
      return { Icon: GitMerge, tone: 'merged', labelKey: 'forgeRef.pull.merged' }
    case 'closed':
      return { Icon: GitPullRequestClosed, tone: 'closed', labelKey: 'forgeRef.pull.closed' }
    default:
      return { Icon: GitPullRequest, tone: 'open', labelKey: 'forgeRef.pull.open' }
  }
}

export function liveChecks(ref: ForgeRefSummary): ForgeChecks | null {
  if (ref.kind !== 'pull' || ref.checks.state === 'none') return null
  return ref.state === 'merged' || ref.state === 'closed' ? null : ref.checks
}

export function checksLabel(checks: ForgeChecks, t: Translate): string {
  if (!checks.counts) {
    if (checks.state === 'failing') return t('forgeRef.pipeline.failed')
    return t(checks.state === 'pending' ? 'forgeRef.pipeline.running' : 'forgeRef.pipeline.passed')
  }
  if (checks.state === 'failing') {
    const failed = checks.counts.failed
    return t(failed === 1 ? 'forgeRef.checks.failing.one' : 'forgeRef.checks.failing.other', { count: failed })
  }
  return t(checks.state === 'pending' ? 'forgeRef.checks.pending' : 'forgeRef.checks.passing')
}

export function pullRequestLabel(pull: ForgePullSummary, t: Translate): string {
  const checks = liveChecks(pull)
  return [
    `${t(pull.provider === 'gitlab' ? 'forgeRef.mergeRequest' : 'forgeRef.pullRequest')} ${forgeRefNumberLabel(pull)}`,
    t(forgeRefState(pull).labelKey),
    checks ? checksLabel(checks, t) : null
  ]
    .filter(Boolean)
    .join(' · ')
}

export function ForgeRefGlyph({ refData, size }: { refData: ForgeRefSummary; size: number }): JSX.Element {
  const { Icon, tone } = forgeRefState(refData)
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
