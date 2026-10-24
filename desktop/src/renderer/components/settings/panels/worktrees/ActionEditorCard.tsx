import { Trash2 } from 'lucide-react'
import { useId, type JSX } from 'react'
import { PROJECT_ACTION_ICONS, PROJECT_ACTION_PLATFORMS, type ProjectActionPlatform } from '../../../../../shared/projectActions'
import { useT } from '../../../../contexts/LocaleContext'
import { DesktopPluginMenu as Menu } from '../../../desktopPlugins/DesktopPluginMenu'
import { PROJECT_ACTION_GLYPHS } from '../../../projectActions/AddProjectActionDialog'
import { IconButton } from '../../../ui/IconButton'
import { Input, Textarea } from '../../../ui/Input'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { missingActionFields, type ActionDraft } from './environmentDraft'
import { PLATFORM_LABELS } from './environmentPresentation'
import styles from './environments.module.css'

const ALL_PLATFORMS = 'all'
type PlatformChoice = ProjectActionPlatform | typeof ALL_PLATFORMS

export function ActionEditorCard({
  action,
  disabled,
  onChange,
  onDelete
}: {
  action: ActionDraft
  disabled: boolean
  onChange: (action: ActionDraft) => void
  onDelete: () => void
}): JSX.Element {
  const t = useT()
  const nameId = useId()
  const commandId = useId()
  const missing = missingActionFields(action)
  const Glyph = PROJECT_ACTION_GLYPHS[action.icon]
  const iconLabel = t('settings.environments.action.icon')

  return (
    <div className={styles.actionCard}>
      <div className={styles.field}>
        <label className={styles.label} htmlFor={nameId}>{t('settings.environments.action.name')}</label>
        <div className={styles.nameLine}>
          <Menu
            items={PROJECT_ACTION_ICONS.map((icon) => {
              const IconGlyph = PROJECT_ACTION_GLYPHS[icon]
              return {
                label: t(`projectActions.icon.${icon}`),
                icon: <IconGlyph size={14} aria-hidden />,
                checked: icon === action.icon,
                onSelect: () => onChange({ ...action, icon })
              }
            })}
          >
            <IconButton
              icon={<Glyph size={15} aria-hidden />}
              label={iconLabel}
              tooltipLabel={iconLabel}
              bordered
              disabled={disabled}
            />
          </Menu>
          <Input
            id={nameId}
            value={action.name}
            invalid={missing.name}
            disabled={disabled}
            placeholder={t('projectActions.add.namePlaceholder')}
            onChange={(event) => onChange({ ...action, name: event.target.value })}
          />
        </div>
      </div>
      <div className={styles.field}>
        <label className={styles.label} htmlFor={commandId}>{t('settings.environments.action.command')}</label>
        <Textarea
          id={commandId}
          mono
          rows={3}
          className={styles.actionCode}
          value={action.command}
          invalid={missing.command}
          disabled={disabled}
          placeholder={t('projectActions.add.commandPlaceholder')}
          onChange={(event) => onChange({ ...action, command: event.target.value })}
        />
      </div>
      <div className={styles.cardFooter}>
        <div className={styles.field}>
          <span className={styles.label}>{t('settings.environments.action.platforms')}</span>
          <div>
            <SegmentedControl<PlatformChoice>
              ariaLabel={t('settings.environments.action.platforms')}
              value={action.platform ?? ALL_PLATFORMS}
              disabled={disabled}
              options={[
                { value: ALL_PLATFORMS, label: t('projectActions.add.anyPlatform') },
                ...PROJECT_ACTION_PLATFORMS.map((platform) => ({ value: platform, label: PLATFORM_LABELS[platform] }))
              ]}
              onChange={(value) => onChange({ ...action, platform: value === ALL_PLATFORMS ? null : value })}
            />
          </div>
        </div>
        <IconButton
          icon={<Trash2 size={15} aria-hidden />}
          label={t('settings.environments.action.delete')}
          tooltipLabel={t('settings.environments.action.delete')}
          disabled={disabled}
          onClick={onDelete}
        />
      </div>
    </div>
  )
}
