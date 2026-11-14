import { useEffect, useState, type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { SettingsEmptyRow, SettingsGroup } from '../../SettingsGroup'
import { SettingsPanelSkeleton } from '../../SettingsSkeleton'
import { WorktreeSettingsGroup } from './WorktreeSettingsGroup'

export function WorktreeGeneralTab({ workspacePath }: { workspacePath?: string }): JSX.Element {
  const t = useT()
  const [git, setGit] = useState<boolean | null>(null)

  useEffect(() => {
    let active = true
    setGit(null)
    window.api.appServer.sendRequest('sourceControl/get', {}).then(
      (result) => {
        if (active) setGit((result as { effectiveProvider?: string } | null)?.effectiveProvider === 'git')
      },
      () => {
        if (active) setGit(false)
      }
    )
    return () => { active = false }
  }, [workspacePath])

  if (git === null) return <SettingsPanelSkeleton />
  if (!git) {
    return (
      <SettingsGroup>
        <SettingsEmptyRow>{t('settings.worktrees.general.unavailable')}</SettingsEmptyRow>
      </SettingsGroup>
    )
  }
  return <WorktreeSettingsGroup workspacePath={workspacePath} />
}
