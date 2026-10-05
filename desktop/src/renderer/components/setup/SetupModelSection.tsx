import type { ModelPreference } from '../../../shared/modelPreference'
import type { WorkspaceSetupModelListResult } from '../../../preload/api.d'
import { useT } from '../../contexts/LocaleContext'
import type { ModelCatalogItem } from '../../stores/modelCatalogStore'
import { PreferenceModelPicker } from '../conversation/PreferenceModelPicker'
import { Button } from '../ui/Button'
import { PillSwitch } from '../ui/PillSwitch'
import { ProviderMark } from '../ui/ProviderMark'
import type { SetupSignInState } from './SetupAccessSection'

export interface SetupModelCatalog {
  key: string
  status: 'loading' | 'ready' | Exclude<WorkspaceSetupModelListResult['kind'], 'success'>
  models: ModelCatalogItem[]
}

export function SetupModelSection({
  providerName,
  catalog,
  preference,
  setAsUserDefault,
  savedNeedsSignIn,
  savedSignIn,
  onSignIn,
  onRetry,
  onPreference,
  onDefault
}: {
  providerName: string
  catalog: SetupModelCatalog
  preference: ModelPreference
  setAsUserDefault: boolean
  savedNeedsSignIn: boolean
  savedSignIn: SetupSignInState
  onSignIn(): void
  onRetry(): void
  onPreference(next: ModelPreference): void
  onDefault(next: boolean): void
}): JSX.Element {
  const t = useT()
  const manual = catalog.status !== 'loading' && catalog.status !== 'ready'
  const listEmpty = catalog.status === 'ready' && catalog.models.length === 0

  return (
    <div className="workspace-setup__stack">
      {catalog.status === 'auth-required' && savedNeedsSignIn && (
        <div className="workspace-setup__notice" data-level="info">
          <ProviderMark kind="openai" size={18} />
          <div className="workspace-setup__notice-text">
            <div className="workspace-setup__notice-body" role={savedSignIn.phase === 'failed' ? 'alert' : undefined}>
              {savedSignIn.phase === 'failed'
                ? t('setupWizard.access.chatgptFailed', { error: savedSignIn.message })
                : t('setupWizard.model.signInNeeded')}
            </div>
          </div>
          <Button size="sm" variant="secondary" loading={savedSignIn.phase === 'pending'} onClick={onSignIn}>
            {t('setupWizard.action.signIn')}
          </Button>
        </div>
      )}
      <div className="workspace-setup__group">
        <div className="workspace-setup__row workspace-setup__row--field">
          <div className="workspace-setup__row-text">
            <label className="workspace-setup__row-title" htmlFor="workspace-setup-model">{t('setupWizard.model.model')}</label>
            <div className="workspace-setup__row-hint">{t('setupWizard.model.modelHint', { provider: providerName })}</div>
          </div>
          <PreferenceModelPicker
            preference={preference}
            models={catalog.status === 'ready' ? catalog.models : []}
            loading={catalog.status === 'loading'}
            errorMessage={catalog.status === 'error' ? t('setupWizard.model.unavailable') : null}
            manualFallback={manual || listEmpty}
            onRetry={onRetry}
            onChange={onPreference}
            inputId="workspace-setup-model"
            inputAriaLabel={t('setupWizard.model.model')}
            placeholder={t('setupWizard.model.manualPlaceholder')}
          />
          {(manual || listEmpty) && (
            <div className="workspace-setup__model-note">
              <span>{t('setupWizard.model.unavailable')}</span>
              {catalog.status === 'error' && (
                <Button size="sm" variant="ghost" onClick={onRetry}>{t('common.retry')}</Button>
              )}
            </div>
          )}
        </div>
        <div className="workspace-setup__row">
          <div className="workspace-setup__row-text">
            <div className="workspace-setup__row-title" id="workspace-setup-default">{t('setupWizard.model.default')}</div>
            <div className="workspace-setup__row-hint">{t('setupWizard.model.defaultHint')}</div>
          </div>
          <PillSwitch checked={setAsUserDefault} onChange={onDefault} aria-labelledby="workspace-setup-default" />
        </div>
      </div>
    </div>
  )
}
