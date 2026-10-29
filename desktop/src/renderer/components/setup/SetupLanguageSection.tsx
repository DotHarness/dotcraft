import { SUPPORTED_LOCALES, type AppLocale } from '../../../shared/locales'
import { useT } from '../../contexts/LocaleContext'
import { SettingsSelect } from '../settings/ui/SettingsSelect'

export function SetupLanguageSection({
  locale,
  onLocale
}: {
  locale: AppLocale
  onLocale(next: AppLocale): void
}): JSX.Element {
  const t = useT()
  return (
    <div className="workspace-setup__group">
      <div className="workspace-setup__row">
        <div className="workspace-setup__row-text">
          <div className="workspace-setup__row-title">{t('setupWizard.language.label')}</div>
          <div className="workspace-setup__row-hint">{t('setupWizard.language.hint')}</div>
        </div>
        <SettingsSelect<AppLocale>
          value={locale}
          onValueChange={onLocale}
          ariaLabel={t('setupWizard.language.label')}
          style={{ width: '180px' }}
          options={SUPPORTED_LOCALES.map((item) => ({ value: item.value, label: item.nativeName }))}
        />
      </div>
    </div>
  )
}
