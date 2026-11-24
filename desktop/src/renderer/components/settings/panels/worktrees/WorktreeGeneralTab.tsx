import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { workspaceResourceKey } from '../../../../stores/workspaceResourceScope'
import { SettingsEmptyRow, SettingsGroup } from '../../SettingsGroup'
import { useSettingsResourceScope } from '../../useSettingsProviderResources'
import { WorktreeLoadingRow } from './WorktreeLoadingRow'
import { WorktreeSettingsGroup } from './WorktreeSettingsGroup'
import { useWorktreeAvailability } from './worktreeSettingsResource'

export function WorktreeGeneralTab({ workspacePath }: { workspacePath?: string }): JSX.Element {
  const t = useT()
  const availability = useWorktreeAvailability(workspacePath)
  const scope = useSettingsResourceScope(Boolean(workspacePath))

  if (availability === 'unavailable') {
    return (
      <SettingsGroup>
        <SettingsEmptyRow>{t('settings.worktrees.general.unavailable')}</SettingsEmptyRow>
      </SettingsGroup>
    )
  }
  if (availability === 'loading' || !scope) {
    return <WorktreeLoadingRow key={workspacePath} standalone />
  }
  return <WorktreeSettingsGroup key={workspaceResourceKey(scope)} scope={scope} />
}
