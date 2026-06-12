import type { ReactNode } from 'react'
import { GitBranch, Laptop, MessageSquare, Shield } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { MenuHeading, MenuOption, PillDropdown } from '../ui/PillDropdown'

type TargetMode = 'project' | 'isolated' | 'bound'
type WorkspaceMode = 'project' | 'isolated'
type ApprovalPolicy = 'workspaceScope' | 'fullAuto'

const triggerIconProps = { size: 13, strokeWidth: 1.8, 'aria-hidden': true } as const
const optionIconProps = { size: 14, strokeWidth: 1.8, 'aria-hidden': true } as const

function workspaceIcon(mode: WorkspaceMode, forTrigger: boolean): JSX.Element {
  const props = forTrigger ? triggerIconProps : optionIconProps
  return mode === 'isolated' ? <GitBranch {...props} /> : <Laptop {...props} />
}

/** Option hint shown as a tooltip beside the row instead of inline copy. */
function OptionHint({ hint, children }: { hint: string; children: ReactNode }): JSX.Element {
  return (
    <ActionTooltip
      label={hint}
      placement="right"
      multiline
      wrapperStyle={{ width: '100%', display: 'flex' }}
    >
      {children}
    </ActionTooltip>
  )
}

/**
 * Where the task runs — mirrors the composer's Local/Worktree picker, plus Chat
 * (bind to an existing thread). Replaces the old target pill + redundant
 * "agent workspace" select.
 */
export function TargetDropdown({
  mode,
  boundName,
  onProject,
  onIsolated,
  onBind,
  onUnbind
}: {
  mode: TargetMode
  boundName: string | null
  onProject(): void
  onIsolated(): void
  onBind(): void
  onUnbind(): void
}): JSX.Element {
  const t = useT()
  const label =
    mode === 'bound'
      ? boundName ?? t('auto.newTask.targetBindThread')
      : mode === 'isolated'
        ? t('auto.newTask.targetIsolated')
        : t('auto.newTask.targetProject')
  const icon =
    mode === 'bound' ? <MessageSquare {...triggerIconProps} /> : workspaceIcon(mode, true)

  return (
    <PillDropdown
      ariaLabel={t('auto.newTask.targetLabel')}
      label={label}
      icon={icon}
      accent={mode === 'bound'}
      panelMinWidth={240}
    >
      {(close) => (
        <>
          <MenuHeading>{t('auto.newTask.targetLabel')}</MenuHeading>
          <OptionHint hint={t('auto.newTask.workspaceProject')}>
            <MenuOption
              selected={mode === 'project'}
              icon={workspaceIcon('project', false)}
              onClick={() => {
                onProject()
                close()
              }}
            >
              {t('auto.newTask.targetProject')}
            </MenuOption>
          </OptionHint>
          <OptionHint hint={t('auto.newTask.workspaceIsolated')}>
            <MenuOption
              selected={mode === 'isolated'}
              icon={workspaceIcon('isolated', false)}
              onClick={() => {
                onIsolated()
                close()
              }}
            >
              {t('auto.newTask.targetIsolated')}
            </MenuOption>
          </OptionHint>
          <OptionHint hint={t('auto.newTask.targetChatHint')}>
            <MenuOption
              selected={mode === 'bound'}
              icon={<MessageSquare {...optionIconProps} />}
              onClick={() => {
                onBind()
                close()
              }}
            >
              {mode === 'bound' && boundName ? boundName : t('auto.newTask.targetBindThread')}
            </MenuOption>
          </OptionHint>
          {mode === 'bound' && (
            <MenuOption
              onClick={() => {
                onUnbind()
                close()
              }}
            >
              {t('auto.newTask.unbind')}
            </MenuOption>
          )}
        </>
      )}
    </PillDropdown>
  )
}

/** Local vs. worktree workspace, used for template defaults (no thread binding). */
export function WorkspaceModeDropdown({
  value,
  onChange
}: {
  value: WorkspaceMode
  onChange(value: WorkspaceMode): void
}): JSX.Element {
  const t = useT()
  const label =
    value === 'isolated' ? t('auto.newTask.targetIsolated') : t('auto.newTask.targetProject')
  return (
    <PillDropdown
      ariaLabel={t('auto.newTask.agentWorkspaceLabel')}
      label={label}
      icon={workspaceIcon(value, true)}
      panelMinWidth={240}
    >
      {(close) => (
        <>
          <OptionHint hint={t('auto.newTask.workspaceProject')}>
            <MenuOption
              selected={value === 'project'}
              icon={workspaceIcon('project', false)}
              onClick={() => {
                onChange('project')
                close()
              }}
            >
              {t('auto.newTask.targetProject')}
            </MenuOption>
          </OptionHint>
          <OptionHint hint={t('auto.newTask.workspaceIsolated')}>
            <MenuOption
              selected={value === 'isolated'}
              icon={workspaceIcon('isolated', false)}
              onClick={() => {
                onChange('isolated')
                close()
              }}
            >
              {t('auto.newTask.targetIsolated')}
            </MenuOption>
          </OptionHint>
        </>
      )}
    </PillDropdown>
  )
}

/** Tool approval policy: workspace-scoped (default) or full auto. */
export function PolicyDropdown({
  value,
  onChange
}: {
  value: ApprovalPolicy
  onChange(value: ApprovalPolicy): void
}): JSX.Element {
  const t = useT()
  const label =
    value === 'fullAuto'
      ? t('auto.newTask.policyFullAutoShort')
      : t('auto.newTask.policyWorkspaceShort')
  return (
    <PillDropdown
      ariaLabel={t('auto.newTask.toolPolicyLabel')}
      label={label}
      icon={<Shield {...triggerIconProps} />}
      panelMinWidth={260}
    >
      {(close) => (
        <>
          <MenuHeading>{t('auto.newTask.toolPolicyLabel')}</MenuHeading>
          <OptionHint hint={t('auto.newTask.policyWorkspace')}>
            <MenuOption
              selected={value === 'workspaceScope'}
              onClick={() => {
                onChange('workspaceScope')
                close()
              }}
            >
              {t('auto.newTask.policyWorkspaceShort')}
            </MenuOption>
          </OptionHint>
          <OptionHint hint={t('auto.newTask.policyFullAuto')}>
            <MenuOption
              selected={value === 'fullAuto'}
              onClick={() => {
                onChange('fullAuto')
                close()
              }}
            >
              {t('auto.newTask.policyFullAutoShort')}
            </MenuOption>
          </OptionHint>
        </>
      )}
    </PillDropdown>
  )
}
