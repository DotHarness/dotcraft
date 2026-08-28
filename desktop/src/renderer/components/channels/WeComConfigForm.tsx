import { useT } from '../../contexts/LocaleContext'
import type { WeComChannelConfig } from './useChannelConfig'
import { ToggleSwitch } from './ToggleSwitch'
import type { ChannelConnectionState } from './ChannelCard'
import { formStyles, StatusPill, FieldCard, FormActions, SecretInput } from './FormShared'
import { Input } from '../ui/Input'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { Trash2 } from 'lucide-react'

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

      <FieldCard>
        <ToggleSwitch
          checked={value.Enabled}
          onChange={(checked) => onChange({ ...value, Enabled: checked })}
          label={t('channels.enableChannel')}
        />
      </FieldCard>

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
            <Input
              value={value.Host}
              onChange={(e) => onChange({ ...value, Host: e.target.value })}
            />
          </div>
          <div style={{ ...formStyles.fieldGroup, marginBottom: 0 }}>
            <label style={formStyles.label}>{t('channels.wecom.port')}</label>
            <Input
              type="number"
              className="dc-plain-number"
              value={String(value.Port)}
              onChange={(e) =>
                onChange({ ...value, Port: Number.parseInt(e.target.value || '0', 10) || 0 })
              }
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
                  <Input
                    value={robot.Path}
                    placeholder={t('channels.wecom.robotPath')}
                    onChange={(e) => {
                      const next = [...value.Robots]
                      next[index] = { ...next[index], Path: e.target.value }
                      onChange({ ...value, Robots: next })
                    }}
                  />
                  <SecretInput
                    value={robot.Token}
                    placeholder={t('channels.wecom.robotToken')}
                    onChange={(nextValue) => {
                      const next = [...value.Robots]
                      next[index] = { ...next[index], Token: nextValue }
                      onChange({ ...value, Robots: next })
                    }}
                  />
                  <SecretInput
                    value={robot.AesKey}
                    placeholder={t('channels.wecom.robotAesKey')}
                    onChange={(nextValue) => {
                      const next = [...value.Robots]
                      next[index] = { ...next[index], AesKey: nextValue }
                      onChange({ ...value, Robots: next })
                    }}
                  />
                </div>
                <IconButton
                  size={28}
                  label={t('channels.wecom.removeRobot')}
                  tooltipLabel={t('channels.wecom.removeRobot')}
                  tooltipPlacement="left"
                  tone="danger"
                  onClick={() =>
                    onChange({ ...value, Robots: value.Robots.filter((_, i) => i !== index) })
                  }
                  icon={<Trash2 size={14} aria-hidden />}
                />
              </div>
            ))}
          </div>
          <Button
            size="sm"
            variant="secondary"
            onClick={() =>
              onChange({ ...value, Robots: [...value.Robots, { Path: '', Token: '', AesKey: '' }] })
            }
          >
            {t('channels.wecom.addRobot')}
          </Button>
        </FieldCard>
      </div>

      <FormActions saving={saving} onSave={onSave} />
    </div>
  )
}
