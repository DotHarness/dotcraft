import type { JSX } from 'react'
import { Hand, OctagonAlert } from 'lucide-react'
import { useT } from '../../../../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../../../../stores/composerPreferencesStore'
import { useConfigSetting } from '../../../../stores/configStore'
import { PillSwitch } from '../../../ui/PillSwitch'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsSelect } from '../../ui/SettingsSelect'
import { CodeModeSettingsRow } from '../codeMode/CodeModeSettingsRow'

type VisibleApprovalPolicy = 'default' | 'autoApprove'

const APPROVAL_SELECT_ID = 'settings-default-approval-policy'
const APPROVAL_MENU_MIN_WIDTH = 288

export function AgentSettingsGroup({
  configManagementEnabled,
  onSharedSkillsChanged
}: {
  configManagementEnabled: boolean
  onSharedSkillsChanged: () => void
}): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const defaultApproval = useConfigSetting('Permissions.DefaultApprovalPolicy', (error) =>
    t('settings.permissions.saveFailed', { error }))
  const sharedSkills = useConfigSetting('Skills.IncludeSharedSkills', (error) =>
    t('settings.personalization.sharedSkillsSaveFailed', { error }))
  const defaultApprovalPolicy = typeof defaultApproval.value === 'string' ? defaultApproval.value : ''

  async function changeDefaultApprovalPolicy(nextPolicy: VisibleApprovalPolicy): Promise<boolean> {
    if (defaultApproval.pending) return false
    const clearNewChatPolicy = (): void => {
      void useComposerPreferencesStore.getState().saveNewChatApprovalPolicy(null).catch(() => undefined)
    }
    if (nextPolicy === defaultApprovalPolicy) {
      clearNewChatPolicy()
      return false
    }

    if (nextPolicy === 'autoApprove') {
      const confirmed = await confirm({
        title: t('settings.permissions.fullAccess.warningTitle'),
        message: t('settings.permissions.fullAccess.warningBody'),
        confirmLabel: t('settings.permissions.fullAccess.warningConfirm'),
        cancelLabel: t('common.cancel'),
        danger: true
      })
      if (!confirmed) return false
    }

    const saved = await defaultApproval.set(nextPolicy)
    if (saved) clearNewChatPolicy()
    return saved
  }

  async function toggleSharedSkills(checked: boolean): Promise<void> {
    if (await sharedSkills.set(checked)) onSharedSkillsChanged()
  }

  return (
    <SettingsGroup title={t('settings.group.agent')}>
      <SettingsRow
        label={t('settings.permissions.workspaceDefault.label')}
        description={t('settings.permissions.workspaceDefault.description')}
        htmlFor={APPROVAL_SELECT_ID}
        control={
          <SettingsSelect
            id={APPROVAL_SELECT_ID}
            value={defaultApprovalPolicy}
            disabled={defaultApproval.pending}
            ariaLabel={t('settings.permissions.workspaceDefault.label')}
            onValueChange={(nextPolicy) => changeDefaultApprovalPolicy(nextPolicy as VisibleApprovalPolicy)}
            style={{ width: '240px' }}
            menuMinWidth={APPROVAL_MENU_MIN_WIDTH}
            options={[
              {
                value: 'default',
                label: t('settings.permissions.default.label'),
                description: t('settings.permissions.default.description'),
                icon: <Hand size={15} strokeWidth={1.9} />
              },
              {
                value: 'autoApprove',
                label: t('settings.permissions.fullAccess.label'),
                description: t('settings.permissions.fullAccess.description'),
                icon: <OctagonAlert size={15} strokeWidth={1.9} />
              }
            ]}
          />
        }
      />
      {configManagementEnabled && <CodeModeSettingsRow />}
      {configManagementEnabled && (
        <SettingsRow
          label={t('settings.personalization.sharedSkills')}
          description={t('settings.personalization.sharedSkillsHint')}
          control={
            <PillSwitch
              checked={sharedSkills.value === true}
              disabled={sharedSkills.pending}
              aria-label={t('settings.personalization.sharedSkills')}
              onChange={(checked) => { void toggleSharedSkills(checked) }}
            />
          }
        />
      )}
    </SettingsGroup>
  )
}
