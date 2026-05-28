import { useMemo } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { ChannelConnectionState } from './ChannelCard'
import { FieldCard, FormActions, StatusPill, formStyles } from './FormShared'
import { ToggleSwitch } from './ToggleSwitch'

export interface ExternalChannelConfigWire {
  name: string
  enabled: boolean
  transport: 'subprocess' | 'websocket' | 'managedWebsocket'
  builtinModule?: string | null
  command?: string | null
  args?: string[] | null
  workingDirectory?: string | null
  env?: Record<string, string> | null
}

interface ExternalChannelConfigFormProps {
  value: ExternalChannelConfigWire
  saving: boolean
  deleting: boolean
  isNew: boolean
  logoPath?: string
  headerTitle?: string
  hideHeader?: boolean
  status: ChannelConnectionState
  statusLabel: string
  onChange: (next: ExternalChannelConfigWire) => void
  onSave: () => void
  onDelete?: () => void
}

function envToText(env: Record<string, string> | null | undefined): string {
  if (!env) return ''
  return Object.entries(env)
    .map(([key, value]) => `${key}=${value}`)
    .join('\n')
}

function textToEnv(text: string): Record<string, string> {
  const out: Record<string, string> = {}
  for (const line of text.split(/\r?\n/)) {
    const trimmed = line.trim()
    if (!trimmed) continue
    const idx = trimmed.indexOf('=')
    if (idx < 0) {
      out[trimmed] = ''
      continue
    }
    const key = trimmed.slice(0, idx).trim()
    const value = trimmed.slice(idx + 1)
    if (key) out[key] = value
  }
  return out
}

export function ExternalChannelConfigForm({
  value,
  saving,
  deleting,
  isNew,
  logoPath,
  headerTitle,
  hideHeader = false,
  status,
  statusLabel,
  onChange,
  onSave,
  onDelete
}: ExternalChannelConfigFormProps): JSX.Element {
  const t = useT()
  const envText = useMemo(() => envToText(value.env), [value.env])
  const hasProcessLauncher = value.transport === 'subprocess' || value.transport === 'managedWebsocket'
  const title = isNew ? t('channels.external.new') : headerTitle || value.name || t('channels.external.title')

  return (
    <div>
      {!hideHeader && (
        <div style={formStyles.header}>
          {logoPath ? (
            <img
              src={logoPath}
              alt={title}
              width={32}
              height={32}
              style={formStyles.headerLogo}
            />
          ) : (
            <div
              style={{
                width: 32,
                height: 32,
                borderRadius: 8,
                backgroundColor: 'var(--bg-secondary)',
                color: 'var(--text-secondary)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: 14,
                fontWeight: 700,
                flexShrink: 0
              }}
            >
              {value.name.trim().slice(0, 1).toUpperCase() || 'E'}
            </div>
          )}
          <div>
            <div style={formStyles.headerTitle}>{title}</div>
            <StatusPill status={status} label={statusLabel} />
          </div>
        </div>
      )}

      <FieldCard>
        <div style={formStyles.fieldGroup}>
          <label style={formStyles.label}>{t('channels.external.name')}</label>
          <input
            type="text"
            value={value.name}
            onChange={(e) => onChange({ ...value, name: e.target.value })}
            style={formStyles.input}
            onFocus={formStyles.inputFocus}
            onBlur={formStyles.inputBlur}
          />
        </div>
        <ToggleSwitch
          checked={value.enabled}
          onChange={(checked) => onChange({ ...value, enabled: checked })}
          label={t('channels.enableChannel')}
        />
      </FieldCard>

      <div
        style={{
          opacity: value.enabled ? 1 : 0.5,
          pointerEvents: value.enabled ? 'auto' : 'none'
        }}
      >
        <FieldCard>
          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.transport')}</label>
            <select
              value={value.transport}
              onChange={(e) => {
                const nextTransport = e.target.value as ExternalChannelConfigWire['transport']
                const nextHasProcessLauncher =
                  nextTransport === 'subprocess' || nextTransport === 'managedWebsocket'
                onChange({
                  ...value,
                  transport: nextTransport,
                  command: nextHasProcessLauncher ? value.command ?? '' : null,
                  args: nextHasProcessLauncher ? value.args ?? [] : null,
                  workingDirectory: nextHasProcessLauncher ? value.workingDirectory ?? '' : null,
                  env: nextHasProcessLauncher ? value.env ?? {} : null
                })
              }}
              style={formStyles.input}
              onFocus={formStyles.inputFocus as never}
              onBlur={formStyles.inputBlur as never}
            >
              <option value="subprocess">Subprocess</option>
              <option value="managedWebsocket">Managed WebSocket</option>
              <option value="websocket">WebSocket</option>
            </select>
          </div>

          {hasProcessLauncher ? (
            <>
              <div style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{t('channels.external.command')}</label>
                <input
                  type="text"
                  value={value.command ?? ''}
                  onChange={(e) => onChange({ ...value, command: e.target.value })}
                  style={formStyles.input}
                  onFocus={formStyles.inputFocus}
                  onBlur={formStyles.inputBlur}
                />
              </div>

              <div style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{t('channels.external.args')}</label>
                <textarea
                  value={(value.args ?? []).join('\n')}
                  onChange={(e) =>
                    onChange({
                      ...value,
                      args: e.target.value.split(/\r?\n/)
                    })
                  }
                  style={{ ...formStyles.input, minHeight: 90, padding: '8px 10px' }}
                />
              </div>

              <div style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{t('channels.external.workingDirectory')}</label>
                <input
                  type="text"
                  value={value.workingDirectory ?? ''}
                  onChange={(e) => onChange({ ...value, workingDirectory: e.target.value })}
                  style={formStyles.input}
                  onFocus={formStyles.inputFocus}
                  onBlur={formStyles.inputBlur}
                />
              </div>

              <div style={{ ...formStyles.fieldGroup, marginBottom: 0 }}>
                <label style={formStyles.label}>{t('channels.external.env')}</label>
                <textarea
                  value={envText}
                  onChange={(e) => onChange({ ...value, env: textToEnv(e.target.value) })}
                  style={{ ...formStyles.input, minHeight: 110, padding: '8px 10px' }}
                />
              </div>
            </>
          ) : (
            <p
              style={{
                margin: 0,
                fontSize: '12px',
                color: 'var(--text-secondary)',
                lineHeight: 1.5
              }}
            >
              {t('channels.external.websocketNote')}
            </p>
          )}
        </FieldCard>
      </div>

      <div style={{ display: 'flex', gap: 10 }}>
        <div style={{ flex: 1 }}>
          <FormActions saving={saving} onSave={onSave} />
        </div>
        {onDelete && !isNew && (
          <button
            type="button"
            onClick={onDelete}
            disabled={deleting}
            style={{
              width: 120,
              height: 38,
              borderRadius: 8,
              border: '1px solid var(--border-default)',
              background: deleting ? 'var(--bg-tertiary)' : 'transparent',
              color: 'var(--danger)',
              fontSize: 13,
              fontWeight: 600,
              cursor: deleting ? 'default' : 'pointer',
              marginTop: 4
            }}
          >
            {deleting ? t('channels.saving') : t('channels.external.delete')}
          </button>
        )}
      </div>
    </div>
  )
}
