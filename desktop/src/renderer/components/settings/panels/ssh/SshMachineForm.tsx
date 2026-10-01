import { useId, useState, type JSX, type ReactNode } from 'react'

import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { useT } from '../../../../contexts/LocaleContext'
import type { MessageKey } from '../../../../../shared/locales'
import {
  validateMachineEntry,
  type SshMachine,
  type SshMachineField,
  type SshMachineFieldError,
  type SshMachineValidation
} from '../../../../../shared/sshMachines'

export type MachineAuth = 'agent' | 'identity'

export interface MachineDraft {
  name: string
  hostname: string
  port: string
  auth: MachineAuth
  identityFile: string
}

export const EMPTY_MACHINE_DRAFT: MachineDraft = { name: '', hostname: '', port: '', auth: 'agent', identityFile: '' }

export interface ParsedMachineDraft {
  name: string
  hostname: string
  port?: number
  identityFile?: string
}

const ERROR_KEYS: Record<SshMachineField, Partial<Record<SshMachineFieldError, MessageKey>>> = {
  name: {
    required: 'settings.ssh.form.error.nameRequired',
    duplicate: 'settings.ssh.form.error.nameDuplicate',
    invalid: 'settings.ssh.form.error.nameInvalid'
  },
  alias: {},
  hostname: {
    required: 'settings.ssh.form.error.hostRequired',
    invalid: 'settings.ssh.form.error.hostInvalid'
  },
  port: { invalid: 'settings.ssh.form.error.portInvalid' },
  identityFile: {
    required: 'settings.ssh.form.error.identityRequired',
    invalid: 'settings.ssh.form.error.identityInvalid'
  }
}

export function parseMachineDraft(draft: MachineDraft): ParsedMachineDraft | { portError: true } {
  const portText = draft.port.trim()
  const port = portText ? Number(portText) : undefined
  if (port !== undefined && (!/^\d+$/.test(portText) || !Number.isInteger(port))) return { portError: true }
  const identityFile = draft.auth === 'identity' ? draft.identityFile.trim() : ''
  return {
    name: draft.name.trim(),
    hostname: draft.hostname.trim(),
    ...(port !== undefined ? { port } : {}),
    ...(identityFile ? { identityFile } : {})
  }
}

export function validateMachineDraft(
  draft: MachineDraft,
  machines: SshMachine[],
  options: { exceptId?: string; nameOnly?: boolean } = {}
): SshMachineValidation {
  const parsed = parseMachineDraft(draft)
  if (options.nameOnly) {
    const errors = validateMachineEntry(
      { source: 'manual', name: draft.name, hostname: 'placeholder' },
      machines,
      options.exceptId
    )
    return errors.name ? { name: errors.name } : {}
  }
  const errors: SshMachineValidation = 'portError' in parsed
    ? {
        ...validateMachineEntry({ source: 'manual', name: draft.name, hostname: draft.hostname }, machines, options.exceptId),
        port: 'invalid'
      }
    : validateMachineEntry({ source: 'manual', ...parsed }, machines, options.exceptId)
  if (draft.auth === 'identity' && !draft.identityFile.trim()) errors.identityFile = 'required'
  return errors
}

function Field({
  id,
  label,
  hint,
  error,
  children
}: {
  id: string
  label: string
  hint?: string
  error?: string
  children: ReactNode
}): JSX.Element {
  return (
    <div className="dc-ssh-field">
      <label className="dc-ssh-field__label" htmlFor={id}>
        {label}
      </label>
      {children}
      {error ? (
        <div className="dc-ssh-field__error" id={`${id}-error`} role="alert">
          {error}
        </div>
      ) : hint ? (
        <div className="dc-ssh-field__hint">{hint}</div>
      ) : null}
    </div>
  )
}

export function SshMachineForm({
  initial,
  machines,
  exceptId,
  nameOnly = false,
  submitLabel,
  serverErrors,
  onBack,
  onSubmit
}: {
  initial: MachineDraft
  machines: SshMachine[]
  exceptId?: string
  nameOnly?: boolean
  submitLabel: string
  serverErrors?: SshMachineValidation
  onBack: () => void
  onSubmit: (draft: MachineDraft) => void | Promise<void>
}): JSX.Element {
  const t = useT()
  const base = useId()
  const [draft, setDraft] = useState(initial)
  const [submitted, setSubmitted] = useState(false)
  const [saving, setSaving] = useState(false)
  const localErrors = submitted ? validateMachineDraft(draft, machines, { exceptId, nameOnly }) : {}
  const errors: SshMachineValidation = { ...serverErrors, ...localErrors }
  const set = (patch: Partial<MachineDraft>): void => setDraft((current) => ({ ...current, ...patch }))
  const message = (field: SshMachineField): string | undefined => {
    const code = errors[field]
    const key = code ? ERROR_KEYS[field][code] ?? ERROR_KEYS[field].invalid : undefined
    return key ? t(key) : undefined
  }

  return (
    <form
      className="dc-ssh-form"
      noValidate
      onSubmit={(event) => {
        event.preventDefault()
        setSubmitted(true)
        if (Object.keys(validateMachineDraft(draft, machines, { exceptId, nameOnly })).length > 0) return
        setSaving(true)
        void Promise.resolve(onSubmit(draft)).finally(() => setSaving(false))
      }}
    >
      <Field id={`${base}-name`} label={t('settings.ssh.form.name')} error={message('name')}>
        <Input
          id={`${base}-name`}
          value={draft.name}
          invalid={Boolean(errors.name)}
          placeholder="build-box"
          onChange={(event) => set({ name: event.target.value })}
        />
      </Field>
      {!nameOnly && (
        <>
          <Field id={`${base}-host`} label={t('settings.ssh.form.hostname')} error={message('hostname')}>
            <Input
              id={`${base}-host`}
              value={draft.hostname}
              invalid={Boolean(errors.hostname)}
              placeholder={t('settings.ssh.form.hostnamePlaceholder')}
              onChange={(event) => set({ hostname: event.target.value })}
            />
          </Field>
          <Field
            id={`${base}-port`}
            label={t('settings.ssh.form.port')}
            hint={t('settings.ssh.form.portHint')}
            error={message('port')}
          >
            <Input
              id={`${base}-port`}
              value={draft.port}
              inputMode="numeric"
              invalid={Boolean(errors.port)}
              placeholder="22"
              onChange={(event) => set({ port: event.target.value })}
            />
          </Field>
          <div className="dc-ssh-field">
            <span className="dc-ssh-field__label">{t('settings.ssh.form.auth')}</span>
            <div>
              <SegmentedControl<MachineAuth>
                ariaLabel={t('settings.ssh.form.auth')}
                value={draft.auth}
                options={[
                  { value: 'agent', label: t('settings.ssh.form.authAgent') },
                  { value: 'identity', label: t('settings.ssh.form.authIdentity') }
                ]}
                onChange={(auth) => set({ auth })}
              />
            </div>
          </div>
          {draft.auth === 'identity' && (
            <Field id={`${base}-identity`} label={t('settings.ssh.form.authIdentity')} error={message('identityFile')}>
              <Input
                id={`${base}-identity`}
                mono
                value={draft.identityFile}
                invalid={Boolean(errors.identityFile)}
                placeholder="~/.ssh/id_ed25519"
                onChange={(event) => set({ identityFile: event.target.value })}
              />
            </Field>
          )}
        </>
      )}
      <div className="dc-ssh-footer">
        <Button type="button" variant="ghost" onClick={onBack}>
          {t('settings.ssh.back')}
        </Button>
        <span className="dc-ssh-footer__spacer" />
        <Button type="submit" variant="primary" loading={saving}>
          {submitLabel}
        </Button>
      </div>
    </form>
  )
}
