import type {
  DesktopPluginAssistantMessageModel,
  DesktopPluginComposerActionContext
} from '@dotcraft/plugin'

import { useLocale } from '../../contexts/LocaleContext'
import {
  isDesktopPluginContributionAvailable,
  resolveDesktopPluginLabel,
  useDesktopPluginRegistry
} from '../../plugins/desktopPluginRegistry'
import { IconButton } from '../ui/IconButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import { resolveDesktopPluginIcon } from './DesktopPluginIcon'
import { DesktopPluginContributionBoundary } from './DesktopPluginContributionBoundary'
import styles from './DesktopPluginContributions.module.css'

export function DesktopPluginComposerActions({
  context
}: {
  context: DesktopPluginComposerActionContext
}): JSX.Element | null {
  const locale = useLocale()
  const contributions = useDesktopPluginRegistry((state) => state.composerActions)
    .filter((contribution) => isDesktopPluginContributionAvailable(contribution, context))
  if (contributions.length === 0) return null

  return (
    <div className={styles.composerActions}>
      {contributions.map((contribution) => {
        const Component = contribution.component
        return (
          <DesktopPluginContributionBoundary
            key={`${contribution.contributionKey}:${contribution.revision}`}
            identity={contribution.contributionKey}
            fallback={null}
          >
            <ActionTooltip label={resolveDesktopPluginLabel(contribution.label, locale)}>
              <span>
                <Component
                  host={contribution.host}
                  contributionId={contribution.id}
                  context={context}
                />
              </span>
            </ActionTooltip>
          </DesktopPluginContributionBoundary>
        )
      })}
    </div>
  )
}

export function DesktopPluginMessageActions({
  message,
  visible
}: {
  message: DesktopPluginAssistantMessageModel
  visible: boolean
}): JSX.Element | null {
  const locale = useLocale()
  const contributions = useDesktopPluginRegistry((state) => state.messageActions)
    .filter((contribution) => isDesktopPluginContributionAvailable(contribution, message))
  if (contributions.length === 0) return null

  return (
    <div className={styles.messageActions} data-visible={visible}>
      {contributions.map((contribution) => {
        const Icon = resolveDesktopPluginIcon(contribution.icon)
        const label = resolveDesktopPluginLabel(contribution.label, locale)
        return (
          <DesktopPluginContributionBoundary
            key={`${contribution.contributionKey}:${contribution.revision}`}
            identity={contribution.contributionKey}
            fallback={null}
          >
            <IconButton
              size={24}
              label={label}
              tooltipLabel={`${label} · ${contribution.host.plugin.displayName}`}
              tooltipPlacement="top"
              icon={<Icon size={14} aria-hidden />}
              onClick={() => {
                void contribution.execute(message, contribution.host)
              }}
            />
          </DesktopPluginContributionBoundary>
        )
      })}
    </div>
  )
}
