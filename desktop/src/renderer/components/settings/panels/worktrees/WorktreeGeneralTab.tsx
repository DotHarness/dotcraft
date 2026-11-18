import { useEffect, type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useSourceControlStore } from '../../../../stores/sourceControlStore'
import { SettingsEmptyRow, SettingsGroup } from '../../SettingsGroup'
import { WorktreeLoadingRow } from './WorktreeLoadingRow'
import { WorktreeSettingsGroup } from './WorktreeSettingsGroup'

export function WorktreeGeneralTab({ workspacePath }: { workspacePath?: string }): JSX.Element {
  const t = useT()
  const ensure = useSourceControlStore((state) => state.ensure)
  const availability = useSourceControlStore((state) => {
    if (!workspacePath) return 'unavailable'
    if (state.workspacePath !== workspacePath || state.bindingStatus === 'loading') return 'loading'
    return state.effectiveProvider === 'git' ? 'git' : 'unavailable'
  })

  useEffect(() => {
    if (workspacePath) ensure(workspacePath, true)
  }, [ensure, workspacePath])

  if (availability === 'loading') return <WorktreeLoadingRow key={workspacePath} standalone />
  if (availability === 'unavailable') {
    return (
      <SettingsGroup>
        <SettingsEmptyRow>{t('settings.worktrees.general.unavailable')}</SettingsEmptyRow>
      </SettingsGroup>
    )
  }
  return <WorktreeSettingsGroup key={workspacePath} />
}
