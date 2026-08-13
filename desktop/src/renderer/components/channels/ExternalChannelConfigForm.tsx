import { useMemo } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { formStyles } from './FormShared'
import { Select } from '../ui/Select'
import { Button } from '../ui/Button'
import { Input, Textarea } from '../ui/Input'
import styles from './ExternalChannelConfigForm.module.css'

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
  onChange: (next: ExternalChannelConfigWire) => void
  onSave: () => void
  onCancel: () => void
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

/** Plain WebSocket needs none: the note replacing its process fields says as much. */
function transportHintKey(transport: ExternalChannelConfigWire['transport']): string | null {
  if (transport === 'managedWebsocket') return 'channels.external.transportHint.managedWebsocket'
  if (transport === 'subprocess') return 'channels.external.transportHint.subprocess'
  return null
}

/**
 * Transport fields stay editable at all times: whether the channel runs is
 * decided by Connect on its detail page, not by a toggle inside the form.
 */
export function ExternalChannelConfigForm({
  value,
  saving,
  deleting,
  isNew,
  onChange,
  onSave,
  onCancel,
  onDelete
}: ExternalChannelConfigFormProps): JSX.Element {
  const t = useT()
  const envText = useMemo(() => envToText(value.env), [value.env])
  const hasProcessLauncher = value.transport === 'subprocess' || value.transport === 'managedWebsocket'
  const transportHint = transportHintKey(value.transport)

  return (
    <div>
      <section className={styles.configuration}>
        <h2>{t('channels.detail.configuration')}</h2>
        <div className={styles.configurationBody}>
          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.external.name')}</label>
            <Input
              value={value.name}
              onChange={(e) => onChange({ ...value, name: e.target.value })}
            />
            <p className={styles.hint}>{t('channels.external.nameHint')}</p>
          </div>

          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.transport')}</label>
            <Select<ExternalChannelConfigWire['transport']>
              value={value.transport}
              onValueChange={(nextTransport) => {
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
              ariaLabel={t('channels.transport')}
              options={[
                { value: 'subprocess', label: 'Subprocess' },
                { value: 'managedWebsocket', label: 'Managed WebSocket' },
                { value: 'websocket', label: 'WebSocket' }
              ]}
            />
            {transportHint && <p className={styles.hint}>{t(transportHint)}</p>}
          </div>

          {hasProcessLauncher ? (
            <>
              <div style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{t('channels.external.command')}</label>
                <Input
                  value={value.command ?? ''}
                  onChange={(e) => onChange({ ...value, command: e.target.value })}
                />
              </div>

              <div style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{t('channels.external.args')}</label>
                <Textarea
                  value={(value.args ?? []).join('\n')}
                  onChange={(e) =>
                    onChange({
                      ...value,
                      args: e.target.value.split(/\r?\n/)
                    })
                  }
                  style={{ minHeight: 76, height: 'auto', padding: '8px 10px' }}
                />
                <p className={styles.hint}>{t('channels.external.argsHint')}</p>
              </div>

              <div style={formStyles.fieldGroup}>
                <label style={formStyles.label}>{t('channels.external.workingDirectory')}</label>
                <Input
                  value={value.workingDirectory ?? ''}
                  onChange={(e) => onChange({ ...value, workingDirectory: e.target.value })}
                />
              </div>

              <div style={{ ...formStyles.fieldGroup, marginBottom: 0 }}>
                <label style={formStyles.label}>{t('channels.external.env')}</label>
                <Textarea
                  value={envText}
                  onChange={(e) => onChange({ ...value, env: textToEnv(e.target.value) })}
                  style={{ minHeight: 92, height: 'auto', padding: '8px 10px' }}
                />
              </div>
            </>
          ) : (
            <p className={styles.note}>{t('channels.external.websocketNote')}</p>
          )}
        </div>
      </section>

      <div className={styles.actions} data-has-delete={onDelete && !isNew ? 'true' : undefined}>
        {onDelete && !isNew && (
          <Button variant="danger" onClick={onDelete} loading={deleting}>
            {t('channels.external.delete')}
          </Button>
        )}
        <span className={styles.actionsPrimary}>
          <Button variant="secondary" onClick={onCancel}>
            {t('common.cancel')}
          </Button>
          <Button
            variant="primary"
            onClick={onSave}
            loading={saving}
            disabled={value.name.trim() === ''}
          >
            {isNew ? t('channels.external.create') : t('channels.save')}
          </Button>
        </span>
      </div>
    </div>
  )
}
