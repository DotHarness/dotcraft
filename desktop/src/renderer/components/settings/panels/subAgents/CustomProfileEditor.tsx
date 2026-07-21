import {
  useEffect,
  useMemo,
  useState,
  type CSSProperties,
  type JSX,
  type ReactNode
} from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import type { MessageKey } from '../../../../../shared/locales'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsSelect } from '../../ui/SettingsSelect'
import { ToggleSwitch } from '../../../channels/ToggleSwitch'
import { SelectionCard } from '../../../ui/SelectionCard'
import {
  EditableKeyValueList,
  EditableValueList,
  normalizeKeyValueRows,
  normalizeValueRows,
  rowsToRecord,
  rowsToValues,
  type KeyValueRow,
  type ValueRow
} from '../../ui/EditableList'
import { AgentIcon } from './AgentIcon'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import {
  actionBarStyle,
  inputStyle,
  monoTextAreaStyle,
  noticeStyle,
  pageDescriptionStyle,
  pageHeadingStyle,
  pageStyle
} from './styles'
import { Button } from '../../../ui/Button'
import {
  buildCustomWriteWire,
  createCustomEditorState,
  createDefaultWriteWire,
  PERMISSION_MODE_KEYS,
  validateCustomEditorState,
  type CustomEditorState,
  type PermissionModeKey,
  type SubAgentProfileEntryWire,
  type SubAgentProfileWriteWire
} from './wire'

type InputModeOption = {
  value: string
  labelKey: MessageKey
  hintKey: MessageKey
}

const INPUT_MODE_OPTIONS: InputModeOption[] = [
  {
    value: 'arg',
    labelKey: 'settings.subAgents.custom.inputMode.arg',
    hintKey: 'settings.subAgents.custom.inputMode.argHint'
  },
  {
    value: 'arg-template',
    labelKey: 'settings.subAgents.custom.inputMode.argTemplate',
    hintKey: 'settings.subAgents.custom.inputMode.argTemplateHint'
  },
  {
    value: 'stdin',
    labelKey: 'settings.subAgents.custom.inputMode.stdin',
    hintKey: 'settings.subAgents.custom.inputMode.stdinHint'
  },
  {
    value: 'env',
    labelKey: 'settings.subAgents.custom.inputMode.env',
    hintKey: 'settings.subAgents.custom.inputMode.envHint'
  }
]

const OUTPUT_FORMAT_OPTIONS: Array<{ value: string; labelKey: MessageKey }> = [
  {
    value: 'text',
    labelKey: 'settings.subAgents.custom.outputFormat.text'
  },
  {
    value: 'json',
    labelKey: 'settings.subAgents.custom.outputFormat.json'
  }
]

const WORKING_DIR_OPTIONS: Array<{ value: string; labelKey: MessageKey }> = [
  {
    value: 'workspace',
    labelKey: 'settings.subAgents.custom.workingDirWorkspace'
  },
  {
    value: 'specified',
    labelKey: 'settings.subAgents.custom.workingDirSpecified'
  }
]

const TRUST_LEVEL_OPTIONS: Array<{ value: string; labelKey: MessageKey }> = [
  { value: '', labelKey: 'settings.subAgents.custom.trustLevel.inherit' },
  { value: 'trusted', labelKey: 'settings.subAgents.custom.trustLevel.trusted' },
  { value: 'prompt', labelKey: 'settings.subAgents.custom.trustLevel.prompt' },
  { value: 'restricted', labelKey: 'settings.subAgents.custom.trustLevel.restricted' }
]

const PERMISSION_LABEL_KEYS: Record<PermissionModeKey, MessageKey> = {
  interactive: 'settings.subAgents.custom.permissionMappingInteractive',
  'auto-approve': 'settings.subAgents.custom.permissionMappingAutoApprove',
  restricted: 'settings.subAgents.custom.permissionMappingRestricted'
}

interface CustomProfileEditorProps {
  mode: 'create' | 'edit'
  profile: SubAgentProfileEntryWire | null
  externalCliSessionResumeEnabled: boolean
  saving: boolean
  deleting: boolean
  onBack: () => void
  onSave: (name: string, definition: SubAgentProfileWriteWire) => Promise<void> | void
  onDelete?: (profile: SubAgentProfileEntryWire) => Promise<void> | void
}

export function CustomProfileEditor({
  mode,
  profile,
  externalCliSessionResumeEnabled,
  saving,
  deleting,
  onBack,
  onSave,
  onDelete
}: CustomProfileEditorProps): JSX.Element {
  const t = useT()
  const initialDefinition = useMemo<SubAgentProfileWriteWire>(
    () => profile?.definition ?? createDefaultWriteWire(),
    [profile]
  )
  const [state, setState] = useState<CustomEditorState>(() =>
    createCustomEditorState(profile?.name ?? '', initialDefinition)
  )
  const [argRows, setArgRows] = useState<ValueRow[]>(() =>
    normalizeValueRows(initialDefinition.args ?? [])
  )
  const [envRows, setEnvRows] = useState<KeyValueRow[]>(() =>
    normalizeKeyValueRows(initialDefinition.env ?? null)
  )
  const [envPassthroughRows, setEnvPassthroughRows] = useState<ValueRow[]>(() =>
    normalizeValueRows(initialDefinition.envPassthrough ?? [])
  )
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  useEffect(() => {
    const def = profile?.definition ?? createDefaultWriteWire()
    setState(createCustomEditorState(profile?.name ?? '', def))
    setArgRows(normalizeValueRows(def.args ?? []))
    setEnvRows(normalizeKeyValueRows(def.env ?? null))
    setEnvPassthroughRows(normalizeValueRows(def.envPassthrough ?? []))
    setErrorMessage(null)
    setShowAdvanced(false)
  }, [profile])

  const headerTitle =
    mode === 'create'
      ? t('settings.subAgents.custom.newTitle')
      : state.name || t('settings.subAgents.custom.editTitle')

  function updateState<K extends keyof CustomEditorState>(key: K, value: CustomEditorState[K]): void {
    setState((prev) => ({ ...prev, [key]: value }))
  }

  function updatePermissionMapping(key: PermissionModeKey, value: string): void {
    setState((prev) => ({
      ...prev,
      permissionModeMapping: {
        ...prev.permissionModeMapping,
        [key]: value
      }
    }))
  }

  async function handleSubmit(): Promise<void> {
    const validation = validateCustomEditorState(state, t)
    if (validation) {
      setErrorMessage(validation)
      return
    }
    const args = rowsToValues(argRows)
    const env = rowsToRecord(envRows)
    const envPassthrough = rowsToValues(envPassthroughRows)
    const built = buildCustomWriteWire(state, args, env, envPassthrough, t)
    if (!built.ok) {
      setErrorMessage(built.error)
      return
    }
    setErrorMessage(null)
    await onSave(state.name.trim(), built.definition)
  }

  async function handleDelete(): Promise<void> {
    if (!profile || !onDelete) return
    await onDelete(profile)
  }

  return (
    <div style={pageStyle()}>
      <SettingsBreadcrumb
        parentLabel={t('settings.subAgents.title')}
        currentLabel={headerTitle}
        onBack={onBack}
        disabled={saving || deleting}
      />

      <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
        <AgentIcon name={state.name || 'custom'} isBuiltIn={false} size={40} />
        <div style={{ minWidth: 0, flex: 1 }}>
          <div style={pageHeadingStyle()}>{headerTitle}</div>
          <div style={pageDescriptionStyle()}>{t('settings.subAgents.custom.description')}</div>
        </div>
      </div>

      <SettingsGroup
        title={t('settings.subAgents.custom.identityTitle')}
        description={t('settings.subAgents.custom.identityDescription')}
      >
        <SettingsRow
          label={t('settings.subAgents.custom.nameLabel')}
          description={
            mode === 'edit'
              ? t('settings.subAgents.custom.nameLocked')
              : t('settings.subAgents.custom.nameHint')
          }
          orientation="block"
        >
          <input
            type="text"
            value={state.name}
            onChange={(event) => updateState('name', event.target.value)}
            placeholder={t('settings.subAgents.custom.namePlaceholder')}
            disabled={mode === 'edit'}
            style={inputStyle(true)}
            data-testid="subagent-name-input"
          />
        </SettingsRow>
      </SettingsGroup>

      <SettingsGroup
        title={t('settings.subAgents.custom.launcherTitle')}
        description={t('settings.subAgents.custom.launcherDescription')}
      >
        <SettingsRow
          label={t('settings.subAgents.custom.runtimeLabel')}
          description={t('settings.subAgents.custom.runtimeHint')}
          control={
            <code style={inlineCodeStyle()}>
              {t('settings.subAgents.custom.runtimeValue')}
            </code>
          }
        />
        <SettingsRow
          label={t('settings.subAgents.custom.binLabel')}
          description={t('settings.subAgents.custom.binHint')}
          orientation="block"
        >
          <input
            type="text"
            value={state.bin}
            onChange={(event) => updateState('bin', event.target.value)}
            placeholder={t('settings.subAgents.custom.binPlaceholder')}
            style={inputStyle(true)}
            data-testid="subagent-bin-input"
          />
        </SettingsRow>
        <SettingsRow
          label={t('settings.subAgents.custom.argsLabel')}
          description={t('settings.subAgents.custom.argsHint')}
          orientation="block"
        >
          <EditableValueList
            rows={argRows}
            setRows={setArgRows}
            placeholder={t('settings.subAgents.custom.argsPlaceholder')}
          />
        </SettingsRow>
        <SettingsRow
          label={t('settings.subAgents.custom.workingDirLabel')}
          description={t('settings.subAgents.custom.workingDirHint')}
          orientation="block"
        >
          <RadioGroup
            name="subagent-working-dir"
            value={state.workingDirectoryMode}
            onChange={(value) => updateState('workingDirectoryMode', value)}
            options={WORKING_DIR_OPTIONS.map((option) => ({
              value: option.value,
              label: t(option.labelKey)
            }))}
          />
        </SettingsRow>
      </SettingsGroup>

      <SettingsGroup
        title={t('settings.subAgents.custom.inputTitle')}
        description={t('settings.subAgents.custom.inputDescription')}
      >
        <SettingsRow orientation="block">
          <RadioGroup
            name="subagent-input-mode"
            value={state.inputMode}
            onChange={(value) => updateState('inputMode', value)}
            options={INPUT_MODE_OPTIONS.map((option) => ({
              value: option.value,
              label: t(option.labelKey),
              description: t(option.hintKey)
            }))}
          />
        </SettingsRow>
        {state.inputMode === 'arg-template' && (
          <SettingsRow
            label={t('settings.subAgents.custom.inputArgTemplateLabel')}
            description={t('settings.subAgents.custom.inputArgTemplateHint')}
            orientation="block"
          >
            <input
              type="text"
              value={state.inputArgTemplate}
              onChange={(event) => updateState('inputArgTemplate', event.target.value)}
              placeholder={t('settings.subAgents.custom.inputArgTemplatePlaceholder')}
              style={inputStyle(true)}
            />
          </SettingsRow>
        )}
        {state.inputMode === 'env' && (
          <SettingsRow
            label={t('settings.subAgents.custom.inputEnvKeyLabel')}
            description={t('settings.subAgents.custom.inputEnvKeyHint')}
            orientation="block"
          >
            <input
              type="text"
              value={state.inputEnvKey}
              onChange={(event) => updateState('inputEnvKey', event.target.value)}
              placeholder={t('settings.subAgents.custom.inputEnvKeyPlaceholder')}
              style={inputStyle(true)}
            />
          </SettingsRow>
        )}
      </SettingsGroup>

      <SettingsGroup
        title={t('settings.subAgents.custom.outputTitle')}
        description={t('settings.subAgents.custom.outputDescription')}
      >
        <SettingsRow
          label={t('settings.subAgents.custom.outputFormatLabel')}
          orientation="block"
        >
          <RadioGroup
            name="subagent-output-format"
            value={state.outputFormat}
            onChange={(value) => updateState('outputFormat', value)}
            options={OUTPUT_FORMAT_OPTIONS.map((option) => ({
              value: option.value,
              label: t(option.labelKey)
            }))}
          />
        </SettingsRow>
        {state.outputFormat === 'json' && (
          <>
            <SettingsRow
              label={t('settings.subAgents.custom.outputJsonPathLabel')}
              description={t('settings.subAgents.custom.outputJsonPathHint')}
              orientation="block"
            >
              <input
                type="text"
                value={state.outputJsonPath}
                onChange={(event) => updateState('outputJsonPath', event.target.value)}
                placeholder={t('settings.subAgents.custom.outputJsonPathPlaceholder')}
                style={inputStyle(true)}
              />
            </SettingsRow>
            <SettingsRow
              label={t('settings.subAgents.custom.tokenPathsTitle')}
              description={t('settings.subAgents.custom.tokenPathsHint')}
              orientation="block"
            >
              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                <LabeledInline
                  label={t('settings.subAgents.custom.outputInputTokensLabel')}
                  value={state.outputInputTokensJsonPath}
                  onChange={(value) => updateState('outputInputTokensJsonPath', value)}
                  placeholder={t('settings.subAgents.custom.outputInputTokensPlaceholder')}
                />
                <LabeledInline
                  label={t('settings.subAgents.custom.outputOutputTokensLabel')}
                  value={state.outputOutputTokensJsonPath}
                  onChange={(value) => updateState('outputOutputTokensJsonPath', value)}
                  placeholder={t('settings.subAgents.custom.outputOutputTokensPlaceholder')}
                />
                <LabeledInline
                  label={t('settings.subAgents.custom.outputTotalTokensLabel')}
                  value={state.outputTotalTokensJsonPath}
                  onChange={(value) => updateState('outputTotalTokensJsonPath', value)}
                  placeholder={t('settings.subAgents.custom.outputTotalTokensPlaceholder')}
                />
              </div>
            </SettingsRow>
          </>
        )}
        <SettingsRow
          label={t('settings.subAgents.custom.outputFileArgTemplateLabel')}
          description={t('settings.subAgents.custom.outputFileArgTemplateHint')}
          orientation="block"
        >
          <input
            type="text"
            value={state.outputFileArgTemplate}
            onChange={(event) => updateState('outputFileArgTemplate', event.target.value)}
            placeholder={t('settings.subAgents.custom.outputFileArgTemplatePlaceholder')}
            style={inputStyle(true)}
          />
        </SettingsRow>
        <SettingsRow
          control={
            <ToggleSwitch
              label={t('settings.subAgents.custom.readOutputFileLabel')}
              description={t('settings.subAgents.custom.readOutputFileHint')}
              checked={state.readOutputFile}
              onChange={(value) => updateState('readOutputFile', value)}
            />
          }
        />
        <SettingsRow
          control={
            <ToggleSwitch
              label={t('settings.subAgents.custom.deleteOutputFileLabel')}
              description={t('settings.subAgents.custom.deleteOutputFileHint')}
              checked={state.deleteOutputFileAfterRead}
              onChange={(value) => updateState('deleteOutputFileAfterRead', value)}
            />
          }
        />
      </SettingsGroup>

      <SettingsGroup
        title={t('settings.subAgents.custom.envTitle')}
        description={t('settings.subAgents.custom.envDescription')}
      >
        <SettingsRow
          label={t('settings.subAgents.custom.envLabel')}
          description={t('settings.subAgents.custom.envHint')}
          orientation="block"
        >
          <EditableKeyValueList
            rows={envRows}
            setRows={setEnvRows}
            keyPlaceholder={t('settings.subAgents.custom.envKeyPlaceholder')}
            valuePlaceholder={t('settings.subAgents.custom.envValuePlaceholder')}
          />
        </SettingsRow>
        <SettingsRow
          label={t('settings.subAgents.custom.envPassthroughLabel')}
          description={t('settings.subAgents.custom.envPassthroughHint')}
          orientation="block"
        >
          <EditableValueList
            rows={envPassthroughRows}
            setRows={setEnvPassthroughRows}
            placeholder={t('settings.subAgents.custom.envPassthroughPlaceholder')}
          />
        </SettingsRow>
      </SettingsGroup>

      <SettingsGroup
        title={t('settings.subAgents.custom.limitsTitle')}
        description={t('settings.subAgents.custom.limitsDescription')}
      >
        <SettingsRow
          label={t('settings.subAgents.custom.timeoutLabel')}
          description={t('settings.subAgents.custom.timeoutHint')}
          orientation="block"
        >
          <input
            type="number"
            min={1}
            value={state.timeout}
            onChange={(event) => updateState('timeout', event.target.value)}
            style={{ ...inputStyle(), width: '160px' }}
          />
        </SettingsRow>
        <SettingsRow
          label={t('settings.subAgents.custom.maxOutputBytesLabel')}
          description={t('settings.subAgents.custom.maxOutputBytesHint')}
          orientation="block"
        >
          <input
            type="number"
            min={1}
            value={state.maxOutputBytes}
            onChange={(event) => updateState('maxOutputBytes', event.target.value)}
            style={{ ...inputStyle(), width: '200px' }}
          />
        </SettingsRow>
      </SettingsGroup>

      <SettingsGroup
        title={t('settings.subAgents.custom.advancedTitle')}
        description={t('settings.subAgents.custom.advancedDescription')}
        headerAction={
          <Button onClick={() => setShowAdvanced((prev) => !prev)}>
            {showAdvanced
              ? t('settings.subAgents.custom.advancedToggleHide')
              : t('settings.subAgents.custom.advancedToggleShow')}
          </Button>
        }
      >
        {showAdvanced && (
          <>
            <SettingsRow
              label={t('settings.subAgents.custom.trustLevelLabel')}
              description={t('settings.subAgents.custom.trustLevelHint')}
              orientation="block"
            >
              <SettingsSelect
                value={state.trustLevel}
                onValueChange={(trustLevel) => updateState('trustLevel', trustLevel)}
                style={{ width: '260px' }}
                options={TRUST_LEVEL_OPTIONS.map((option) => ({
                  value: option.value,
                  label: t(option.labelKey)
                }))}
              />
            </SettingsRow>
            <SettingsRow
              label={t('settings.subAgents.custom.permissionMappingTitle')}
              description={t('settings.subAgents.custom.permissionMappingHint')}
              orientation="block"
            >
              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                {PERMISSION_MODE_KEYS.map((key) => (
                  <div
                    key={key}
                    style={{
                      display: 'grid',
                      gridTemplateColumns: '160px 1fr',
                      gap: '10px',
                      alignItems: 'center'
                    }}
                  >
                    <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
                      {t(PERMISSION_LABEL_KEYS[key])}
                    </span>
                    <input
                      type="text"
                      value={state.permissionModeMapping[key]}
                      onChange={(event) => updatePermissionMapping(key, event.target.value)}
                      placeholder={t('settings.subAgents.custom.permissionMappingPlaceholder')}
                      style={inputStyle(true)}
                    />
                  </div>
                ))}
              </div>
            </SettingsRow>
            <SettingsRow
              control={
                <ToggleSwitch
                  label={t('settings.subAgents.custom.supportsResumeLabel')}
                  description={t('settings.subAgents.custom.supportsResumeHint')}
                  checked={state.supportsResume}
                  onChange={(value) => updateState('supportsResume', value)}
                />
              }
            />
            {state.supportsResume && (
              <>
                <SettingsRow
                  label={t('settings.subAgents.custom.resumeArgTemplateLabel')}
                  description={t('settings.subAgents.custom.resumeArgTemplateHint')}
                  orientation="block"
                >
                  <input
                    type="text"
                    value={state.resumeArgTemplate}
                    onChange={(event) => updateState('resumeArgTemplate', event.target.value)}
                    placeholder={t('settings.subAgents.custom.resumeArgTemplatePlaceholder')}
                    style={inputStyle(true)}
                  />
                </SettingsRow>
                <SettingsRow
                  label={t('settings.subAgents.custom.resumeSessionIdJsonPathLabel')}
                  description={t('settings.subAgents.custom.resumeSessionIdJsonPathHint')}
                  orientation="block"
                >
                  <input
                    type="text"
                    value={state.resumeSessionIdJsonPath}
                    onChange={(event) => updateState('resumeSessionIdJsonPath', event.target.value)}
                    placeholder={t('settings.subAgents.custom.resumeSessionIdJsonPathPlaceholder')}
                    style={inputStyle(true)}
                  />
                </SettingsRow>
                <SettingsRow
                  label={t('settings.subAgents.custom.resumeSessionIdRegexLabel')}
                  description={t('settings.subAgents.custom.resumeSessionIdRegexHint')}
                  orientation="block"
                >
                  <input
                    type="text"
                    value={state.resumeSessionIdRegex}
                    onChange={(event) => updateState('resumeSessionIdRegex', event.target.value)}
                    placeholder={t('settings.subAgents.custom.resumeSessionIdRegexPlaceholder')}
                    style={inputStyle(true)}
                  />
                </SettingsRow>
                {!externalCliSessionResumeEnabled && (
                  <div style={noticeStyle('warning')}>
                    {t('settings.subAgents.custom.resumeWorkspaceToggleOff')}
                  </div>
                )}
              </>
            )}
            <SettingsRow
              label={t('settings.subAgents.custom.sanitizationRulesLabel')}
              description={t('settings.subAgents.custom.sanitizationRulesHint')}
              orientation="block"
            >
              <textarea
                value={state.sanitizationRulesText}
                onChange={(event) => updateState('sanitizationRulesText', event.target.value)}
                placeholder={t('settings.subAgents.custom.sanitizationRulesPlaceholder')}
                style={monoTextAreaStyle()}
                rows={4}
              />
            </SettingsRow>
          </>
        )}
      </SettingsGroup>

      {errorMessage && <div style={noticeStyle('error')}>{errorMessage}</div>}

      <div style={actionBarStyle()}>
        {mode === 'edit' && profile && onDelete && (
          <Button
            variant="danger"
            onClick={handleDelete}
            disabled={deleting || saving}
          >
            {deleting ? t('settings.subAgents.deleting') : t('settings.subAgents.delete')}
          </Button>
        )}
        <Button
          variant="primary"
          onClick={handleSubmit}
          disabled={saving || deleting}
        >
          {saving ? t('settings.subAgents.saving') : t('settings.subAgents.save')}
        </Button>
      </div>
    </div>
  )
}

interface RadioOption {
  value: string
  label: string
  description?: string
}

interface RadioGroupProps {
  name: string
  value: string
  onChange: (value: string) => void
  options: RadioOption[]
}

function RadioGroup({ name, value, onChange, options }: RadioGroupProps): JSX.Element {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
      {options.map((option) => (
        <SelectionCard
          key={option.value}
          name={name}
          value={option.value}
          active={option.value === value}
          onSelect={() => onChange(option.value)}
          title={option.label}
          description={option.description}
        />
      ))}
    </div>
  )
}

interface LabeledInlineProps {
  label: ReactNode
  value: string
  onChange: (value: string) => void
  placeholder?: string
}

function LabeledInline({ label, value, onChange, placeholder }: LabeledInlineProps): JSX.Element {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: '180px 1fr', gap: '10px', alignItems: 'center' }}>
      <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>{label}</span>
      <input
        type="text"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        style={inputStyle(true)}
      />
    </div>
  )
}

function inlineCodeStyle(): CSSProperties {
  return {
    padding: '2px 6px',
    borderRadius: '6px',
    background: 'var(--bg-tertiary)',
    fontFamily: 'var(--font-mono)',
    fontSize: '12px',
    color: 'var(--text-primary)'
  }
}

