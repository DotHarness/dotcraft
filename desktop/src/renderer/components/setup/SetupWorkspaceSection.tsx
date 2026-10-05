import { Folder } from 'lucide-react'
import { SUPPORTED_LOCALES, type AppLocale } from '../../../shared/locales'
import { useT } from '../../contexts/LocaleContext'
import { SettingsSelect } from '../settings/ui/SettingsSelect'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Button } from '../ui/Button'

export function SetupWorkspaceSection({
  path,
  locale,
  onChangeFolder,
  onLocale
}: {
  path: string
  locale: AppLocale
  onChangeFolder(): void
  onLocale(next: AppLocale): void
}): JSX.Element {
  const t = useT()
  return (
    <div className="workspace-setup__group">
      <div className="workspace-setup__row">
        <span className="workspace-setup__mark"><Folder size={16} strokeWidth={1.8} /></span>
        <div className="workspace-setup__row-text">
          <ActionTooltip label={path} wrapperStyle={{ display: 'block', minWidth: 0 }}>
            <div className="workspace-setup__row-title workspace-setup__mono">{path}</div>
          </ActionTooltip>
        </div>
        <Button size="sm" variant="secondary" onClick={onChangeFolder}>{t('setupWizard.workspace.change')}</Button>
      </div>
      <div className="workspace-setup__row">
        <div className="workspace-setup__row-text">
          <div className="workspace-setup__row-title">{t('setupWizard.workspace.language')}</div>
          <div className="workspace-setup__row-hint">{t('setupWizard.workspace.languageHint')}</div>
        </div>
        <SettingsSelect<AppLocale>
          value={locale}
          onValueChange={onLocale}
          ariaLabel={t('setupWizard.workspace.language')}
          style={{ width: '180px' }}
          options={SUPPORTED_LOCALES.map((item) => ({ value: item.value, label: item.nativeName }))}
        />
      </div>
    </div>
  )
}
