import type { ApprovalOptionSpec, PendingApproval } from '../stores/conversationStore'
import type { ApprovalShellInfo } from '../types/conversation'

type Translate = (key: string, vars?: Record<string, string | number>) => string

const NARROWEST_ALLOW = ['accept', 'allowOnce', 'acceptForSession']
const DENY_SCOPES = new Set(['blockDomain'])

export interface ApprovalRoles {
  deny: ApprovalOptionSpec | undefined
  denyScopes: ApprovalOptionSpec[]
  primary: ApprovalOptionSpec
  wider: ApprovalOptionSpec[]
}

export function approvalOptions(request: PendingApproval, t: Translate): ApprovalOptionSpec[] {
  if (request.options) return request.options
  if (request.approvalType === 'computerUse') {
    return [
      {
        value: 'acceptForSession',
        label: t('approval.computerUse.allowThread.label'),
        description: t('approval.computerUse.allowThread.description')
      },
      {
        value: 'acceptAlways',
        label: t('approval.computerUse.alwaysAllow.label'),
        description: t('approval.computerUse.alwaysAllow.description')
      },
      {
        value: 'decline',
        label: t('approval.option.decline.label'),
        description: t('approval.computerUse.decline.description')
      }
    ]
  }
  const shell = request.shell
  return [
    {
      value: 'accept',
      label: t('approval.option.accept.label'),
      description: t('approval.option.accept.description')
    },
    {
      value: 'acceptForSession',
      label: t('approval.option.acceptForSession.label'),
      description: shell
        ? t('approval.option.acceptForSession.shellDescription')
        : t('approval.option.acceptForSession.description')
    },
    {
      value: 'acceptAlways',
      label: t('approval.option.acceptAlways.label'),
      description: shell ? rememberDescription(shell, t) : t('approval.option.acceptAlways.description')
    },
    {
      value: 'decline',
      label: t('approval.option.decline.label'),
      description: t('approval.option.decline.description')
    }
  ]
}

export function approvalRoles(options: ApprovalOptionSpec[], declineValue: string): ApprovalRoles {
  const deny = options.find((option) => option.value === declineValue)
  const denyScopes = options.filter((option) => option !== deny && DENY_SCOPES.has(option.value))
  const allows = options.filter((option) => option !== deny && !DENY_SCOPES.has(option.value))
  const primary = NARROWEST_ALLOW
    .map((value) => allows.find((option) => option.value === value))
    .find((option) => option !== undefined) ?? allows[0]
  return { deny, denyScopes, primary, wider: allows.filter((option) => option !== primary) }
}

function rememberDescription(shell: ApprovalShellInfo, t: Translate): string {
  const prefixes = shell.rememberedPrefixes.map((words) => words.join(' ')).join(', ')
  if (prefixes.length === 0) return t('approval.remember.exact')
  return t(shell.remembersExactCommand ? 'approval.remember.both' : 'approval.remember.prefixes', { prefixes })
}
