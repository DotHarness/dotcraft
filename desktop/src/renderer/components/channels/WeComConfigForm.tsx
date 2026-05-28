import { useT } from '../../contexts/LocaleContext'
import type { WeComChannelConfig } from './useChannelConfig'
import { ToggleSwitch } from './ToggleSwitch'
import type { ChannelConnectionState } from './ChannelCard'
import { formStyles, StatusPill, FieldCard, FormActions, SecretInput } from './FormShared'

interface WeComConfigFormProps {
  value: WeComChannelConfig
  saving: boolean
  logoPath: string
  status: ChannelConnectionState
  statusLabel: string
  onChange: (next: WeComChannelConfig) => void
  onSave: () => void
}

export function WeComConfigForm({
  value,
  saving,
  logoPath,
  status,
  statusLabel,
  onChange,
  onSave
}: WeComConfigFormProps): JSX.Element {
  const t = useT()

  return (
    <div>
      {/* Channel header */}
      <div style={formStyles.header}>
        <img
          src={logoPath}
          alt={t('channels.channel.wecom')}
          width={32}
          height={32}
          style={formStyles.headerLogo}
        />
        <div>
          <div style={formStyles.headerTitle}>{t('channels.wecom.title')}</div>
          <StatusPill status={status} label={statusLabel} />
        </div>
      </div>

      {/* Enable toggle card */}
      <FieldCard>
        <ToggleSwitch
          checked={value.Enabled}
          onChange={(checked) => onChange({ ...value, Enabled: checked })}
          label={t('channels.enableChannel')}
        />
      </FieldCard>

      {/* Config fields */}
      <div style={{ opacity: value.Enabled ? 1 : 0.5, pointerEvents: value.Enabled ? 'auto' : 'none' }}>
        <FieldCard>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '14px' }}>
            <span
              style={{
                fontSize: '11px',
                fontWeight: 600,
                color: 'var(--text-secondary)',
                textTransform: 'uppercase',
                letterSpacing: '0.04em'
              }}
            >
              {t('channels.transport')}
            </span>
            <span
              style={{
                fontSize: '12px',
                fontWeight: 500,
                color: 'var(--text-primary)',
                backgroundColor: 'var(--bg-tertiary)',
                border: '1px solid var(--border-default)',
                borderRadius: '4px',
                padding: '1px 6px'
              }}
            >
              Native
            </span>
          </div>
          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.wecom.host')}</label>
            <input
              type="text"
              value={value.Host}
              onChange={(e) => onChange({ ...value, Host: e.target.value })}
              style={formStyles.input}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
            />
          </div>
          <div style={{ ...formStyles.fieldGroup, marginBottom: 0 }}>
            <label style={formStyles.label}>{t('channels.wecom.port')}</label>
            <input
              type="number"
              value={String(value.Port)}
              onChange={(e) =>
                onChange({ ...value, Port: Number.parseInt(e.target.value || '0', 10) || 0 })
              }
              style={formStyles.input}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
            />
          </div>
        </FieldCard>

        <FieldCard>
          <div
            style={{
              fontSize: '12px',
              fontWeight: 600,
              color: 'var(--text-primary)',
              marginBottom: '12px'
            }}
          >
            {t('channels.wecom.robots')}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginBottom: '8px' }}>
            {value.Robots.map((robot, index) => (
              <div
                key={`${robot.Path}-${index}`}
                style={{
                  border: '1px solid var(--border-default)',
                  borderRadius: '8px',
                  padding: '10px',
                  backgroundColor: 'var(--bg-primary)'
                }}
              >
                <div
                  style={{ display: 'grid', gridTemplateColumns: '1fr', gap: '8px', marginBottom: '8px' }}
                >
                  <input
                    type="text"
                    value={robot.Path}
                    placeholder={t('channels.wecom.robotPath')}
                    onChange={(e) => {
                      const next = [...value.Robots]
                      next[index] = { ...next[index], Path: e.target.value }
                      onChange({ ...value, Robots: next })
                    }}
                    style={formStyles.input}
                    onFocus={formStyles.inputFocus}
                    onBlur={formStyles.inputBlur}
                  />
                  <SecretInput
                    value={robot.Token}
                    placeholder={t('channels.wecom.robotToken')}
                    onChange={(nextValue) => {
                      const next = [...value.Robots]
                      next[index] = { ...next[index], Token: nextValue }
                      onChange({ ...value, Robots: next })
                    }}
                    onFocus={formStyles.inputFocus}
                    onBlur={formStyles.inputBlur}
                    style={formStyles.input}
                  />
                  <SecretInput
                    value={robot.AesKey}
                    placeholder={t('channels.wecom.robotAesKey')}
                    onChange={(nextValue) => {
                      const next = [...value.Robots]
                      next[index] = { ...next[index], AesKey: nextValue }
                      onChange({ ...value, Robots: next })
                    }}
                    onFocus={formStyles.inputFocus}
                    onBlur={formStyles.inputBlur}
                    style={formStyles.input}
                  />
                </div>
                <button
                  type="button"
                  onClick={() =>
                    onChange({ ...value, Robots: value.Robots.filter((_, i) => i !== index) })
                  }
                  style={{
                    padding: '4px 10px',
                    border: '1px solid var(--border-default)',
                    borderRadius: '6px',
                    backgroundColor: 'transparent',
                    color: 'var(--text-secondary)',
                    cursor: 'pointer',
                    fontSize: '12px'
                  }}
                >
                  {t('channels.wecom.removeRobot')}
                </button>
              </div>
            ))}
          </div>
          <button
            type="button"
            onClick={() =>
              onChange({ ...value, Robots: [...value.Robots, { Path: '', Token: '', AesKey: '' }] })
            }
            style={{
              padding: '6px 12px',
              border: '1px solid var(--border-default)',
              borderRadius: '6px',
              backgroundColor: 'transparent',
              color: 'var(--text-primary)',
              cursor: 'pointer',
              fontSize: '12px'
            }}
          >
            {t('channels.wecom.addRobot')}
          </button>
        </FieldCard>
      </div>

      <FormActions saving={saving} onSave={onSave} />
    </div>
  )
}
