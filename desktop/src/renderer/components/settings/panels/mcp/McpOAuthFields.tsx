import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import type { McpOAuthConfigWire } from '../../../../stores/mcpStore'
import { SecretInput } from '../../../channels/FormShared'
import { Input } from '../../../ui/Input'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { settingsErrorTextStyle } from '../../settingsTypography'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { MASKED_CLIENT_SECRET } from './useMcpDraft'
import styles from './McpOAuthFields.module.css'

type Registration = 'automatic' | 'preRegistered'
type SecretSource = 'value' | 'environment'

export function McpOAuthFields({
  value,
  onChange,
  authorizationConflict = false
}: {
  value: McpOAuthConfigWire | null | undefined
  onChange(value: McpOAuthConfigWire | null): void
  authorizationConflict?: boolean
}): JSX.Element {
  const t = useT()
  const registration: Registration = value != null ? 'preRegistered' : 'automatic'
  const source: SecretSource = value?.clientSecretEnvVar != null ? 'environment' : 'value'
  const secretSaved = value?.hasClientSecret === true && value.clientSecret === MASKED_CLIENT_SECRET
  const update = (patch: Partial<McpOAuthConfigWire>) => onChange({ ...value, ...patch })

  return (
    <SettingsGroup title={t('settings.mcp.oauth.title')}>
      <SettingsRow
        label={t('settings.mcp.oauth.registration')}
        description={
          <>
            {registration === 'preRegistered'
              ? t('settings.mcp.oauth.preRegisteredHint')
              : t('settings.mcp.oauth.automaticHint')}
            {registration === 'preRegistered' && authorizationConflict && (
              <span role="alert" style={{ ...settingsErrorTextStyle(), display: 'block' }}>
                {t('settings.mcp.oauth.authorizationConflict')}
              </span>
            )}
          </>
        }
        control={
          <SegmentedControl<Registration>
            value={registration}
            options={[
              { value: 'automatic', label: t('settings.mcp.oauth.automatic') },
              { value: 'preRegistered', label: t('settings.mcp.oauth.preRegistered') }
            ]}
            onChange={(next) => onChange(next === 'preRegistered' ? { clientId: '' } : null)}
            ariaLabel={t('settings.mcp.oauth.registration')}
          />
        }
      />
      {value != null && (
        <>
          <SettingsRow
            label={t('settings.mcp.oauth.clientId')}
            htmlFor="mcp-oauth-client-id"
            orientation="block"
          >
            <Input
              id="mcp-oauth-client-id"
              value={value.clientId ?? ''}
              onChange={(event) => update({ clientId: event.target.value })}
              placeholder={t('settings.mcp.oauth.clientIdPlaceholder')}
              mono
            />
          </SettingsRow>
          <SettingsRow
            label={t('settings.mcp.oauth.secret')}
            description={
              source === 'environment'
                ? t('settings.mcp.oauth.secretEnvHint')
                : secretSaved
                  ? t('settings.mcp.oauth.secretSavedHint')
                  : t('settings.mcp.oauth.secretHint')
            }
            htmlFor="mcp-oauth-client-secret"
            orientation="block"
          >
            <div className={styles.secret}>
              <SegmentedControl<SecretSource>
                value={source}
                options={[
                  { value: 'value', label: t('settings.mcp.oauth.literal') },
                  { value: 'environment', label: t('settings.mcp.oauth.environment') }
                ]}
                onChange={(next) =>
                  update({
                    clientSecret: null,
                    clientSecretEnvVar: next === 'environment' ? '' : null,
                    hasClientSecret: false
                  })
                }
                ariaLabel={t('settings.mcp.oauth.secretSource')}
              />
              {source === 'environment' ? (
                <Input
                  id="mcp-oauth-client-secret"
                  aria-label={t('settings.mcp.oauth.secretEnv')}
                  value={value.clientSecretEnvVar ?? ''}
                  onChange={(event) => update({ clientSecretEnvVar: event.target.value })}
                  placeholder="MCP_CLIENT_SECRET"
                  mono
                />
              ) : (
                <SecretInput
                  id="mcp-oauth-client-secret"
                  ariaLabel={t('settings.mcp.oauth.secret')}
                  value={value.clientSecret ?? ''}
                  onChange={(clientSecret) => update({ clientSecret })}
                  mono
                />
              )}
            </div>
          </SettingsRow>
          <SettingsRow
            label={t('settings.mcp.oauth.callbackUrl')}
            description={t('settings.mcp.oauth.callbackUrlHint')}
            htmlFor="mcp-oauth-callback-url"
            orientation="block"
          >
            <Input
              id="mcp-oauth-callback-url"
              value={value.callbackUrl ?? ''}
              onChange={(event) => update({ callbackUrl: event.target.value || null })}
              placeholder="http://127.0.0.1:33418/callback"
              mono
            />
          </SettingsRow>
          <SettingsRow
            label={t('settings.mcp.oauth.callbackPort')}
            description={t('settings.mcp.oauth.callbackPortHint')}
            htmlFor="mcp-oauth-callback-port"
            control={
              <Input
                id="mcp-oauth-callback-port"
                type="number"
                className={`dc-plain-number ${styles.port}`}
                min={1}
                max={65535}
                value={value.callbackPort ?? ''}
                onChange={(event) =>
                  update({ callbackPort: event.target.value ? Number(event.target.value) : null })
                }
                placeholder={t('settings.mcp.oauth.callbackPortPlaceholder')}
                mono
              />
            }
          />
        </>
      )}
    </SettingsGroup>
  )
}
