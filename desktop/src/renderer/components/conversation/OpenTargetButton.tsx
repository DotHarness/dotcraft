import type { ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useOpenTargetsStore } from '../../stores/openTargetsStore'
import { SplitButton, type SplitButtonItem } from '../ui/SplitButton'
import type { ButtonVariant } from '../ui/Button'
import { ExplorerIcon } from '../ui/AppIcons'
import {
  OPEN_TARGET_ICON_SIZE,
  openPathInTarget,
  openTargetLabel,
  openTargetStatusLabel,
  renderOpenTargetIcon,
  useOpenTargets
} from '../../utils/openTargets'

interface OpenTargetButtonProps {
  targetPath: string
  projectRoot: string
  rememberChoice?: boolean
  tooltipLabel: string
  menuAriaLabel: string
  showPrimaryLabel?: boolean
  primaryButtonLabel?: string
  primaryIcon?: ReactNode
  tooltipPlacement?: 'top' | 'bottom' | 'left' | 'right'
  variant?: ButtonVariant
}

export function OpenTargetButton({
  targetPath,
  projectRoot,
  rememberChoice = false,
  tooltipLabel,
  menuAriaLabel,
  showPrimaryLabel = false,
  primaryButtonLabel,
  primaryIcon,
  tooltipPlacement = 'bottom',
  variant = 'secondary'
}: OpenTargetButtonProps): JSX.Element {
  const t = useT()
  const view = useOpenTargets(projectRoot)
  const effective = view.effective

  function pick(targetId: string): void {
    if (rememberChoice) useOpenTargetsStore.getState().remember(targetId, projectRoot)
    void openPathInTarget(targetId, targetPath, t)
  }

  const statusLabel = openTargetStatusLabel(view.status, t)
  const items: SplitButtonItem[] = statusLabel
    ? [{ key: 'status', label: statusLabel, disabled: true, onClick: () => {} }]
    : view.targets.map((target) => ({
        key: target.id,
        label: openTargetLabel(target, t),
        icon: renderOpenTargetIcon(target, OPEN_TARGET_ICON_SIZE),
        selected: target.id === effective?.id,
        onClick: () => pick(target.id)
      }))

  return (
    <SplitButton
      variant={variant}
      label={showPrimaryLabel ? (primaryButtonLabel ?? t('threadHeader.open')) : undefined}
      ariaLabel={effective ? t('threadHeader.openIn', { app: openTargetLabel(effective, t) }) : t('threadHeader.open')}
      icon={primaryIcon ?? (effective ? renderOpenTargetIcon(effective, OPEN_TARGET_ICON_SIZE) : <ExplorerIcon size={OPEN_TARGET_ICON_SIZE} />)}
      onClick={() => {
        if (effective) void openPathInTarget(effective.id, targetPath, t)
      }}
      items={items}
      menuLabel={menuAriaLabel}
      tooltip={tooltipLabel}
      disabledReason={effective ? undefined : t('openTargets.loading')}
      tooltipPlacement={tooltipPlacement}
      disabled={!effective && view.status !== 'error'}
      onMenuOpen={() => { void view.refresh() }}
    />
  )
}
