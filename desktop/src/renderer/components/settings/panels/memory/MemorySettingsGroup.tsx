import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { ActionTooltip } from '../../../ui/ActionTooltip'
import { Button } from '../../../ui/Button'
import { PillSwitch } from '../../../ui/PillSwitch'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import type { DreamsSettings } from './useDreamsSettings'

interface MemorySettingsGroupProps {
  memoryToggle: {
    enabled: boolean
    applying: boolean
    onToggle: (checked: boolean) => void
  } | null
  dreams: DreamsSettings | null
  onManageDreams: () => void
  deleteMemories: {
    deleting: boolean
    onDelete: () => void
  } | null
}

export function MemorySettingsGroup({
  memoryToggle,
  dreams,
  onManageDreams,
  deleteMemories
}: MemorySettingsGroupProps): JSX.Element {
  const t = useT()
  const memoryEnabled = memoryToggle?.enabled ?? true

  return (
    <SettingsGroup
      title={t('settings.personalization.group.memory')}
      description={t('settings.personalization.memory.subtitle')}
    >
      {memoryToggle && (
        <SettingsRow
          label={t('settings.personalization.memory.enable')}
          description={t('settings.personalization.memory.enableHint')}
          control={
            <PillSwitch
              checked={memoryToggle.enabled}
              disabled={memoryToggle.applying}
              aria-label={t('settings.personalization.memory.enable')}
              onChange={memoryToggle.onToggle}
            />
          }
        />
      )}
      {dreams && (
        <SettingsRow
          label={t('settings.personalization.dreams')}
          description={t('settings.personalization.dreamsHint')}
          control={
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              <Button aria-label={t('settings.personalization.dreamsManageAria')} onClick={onManageDreams}>
                {t('settings.personalization.dreamsManage')}
              </Button>
              <ActionTooltip
                label=""
                disabledReason={memoryEnabled ? undefined : t('settings.personalization.memory.requiredForDreams')}
              >
                <PillSwitch
                  checked={dreams.enabled}
                  disabled={!memoryEnabled || dreams.settingsBusy}
                  aria-label={t('settings.personalization.dreams')}
                  onChange={(checked) => {
                    void dreams.toggleEnabled(checked)
                  }}
                />
              </ActionTooltip>
            </div>
          }
        />
      )}
      {deleteMemories && (
        <SettingsRow
          label={t('settings.personalization.memory.delete')}
          description={t('settings.personalization.memory.deleteHint')}
          control={
            <Button
              variant="danger"
              disabled={deleteMemories.deleting}
              onClick={deleteMemories.onDelete}
            >
              {deleteMemories.deleting
                ? t('settings.personalization.memory.deleting')
                : t('settings.personalization.memory.deleteButton')}
            </Button>
          }
        />
      )}
    </SettingsGroup>
  )
}
