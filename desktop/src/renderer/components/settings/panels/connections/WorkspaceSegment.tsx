import type { CSSProperties, JSX, RefObject } from 'react'

import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsSelect } from '../../ui/SettingsSelect'
import {
  settingsErrorTextStyle,
  settingsHintStyle,
  settingsSectionLabelStyle
} from '../../settingsTypography'
import { FolderIcon } from '../../../ui/AppIcons'
import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { InputWithAction } from '../../../ui/InputWithAction'
import { SelectionCard } from '../../../ui/SelectionCard'
import { SecretInput } from '../../../channels/FormShared'
import { useT } from '../../../../contexts/LocaleContext'
import type { MessageKey } from '../../../../../shared/locales'
import type { BinarySource, ConnectionMode } from '../../../../../shared/desktopSettings'

export interface WorkspaceSegmentProps {
  connectionMode: ConnectionMode
  onConnectionModeChange: (mode: ConnectionMode) => void
  activeRemoteStackConnection: boolean
  manualRemoteConnection: boolean
  remoteUrl: string
  onRemoteUrlChange: (value: string) => void
  remoteUrlErrorKey: MessageKey | null
  remoteToken: string
  onRemoteTokenChange: (value: string) => void
  binarySource: BinarySource
  onBinarySourceChange: (source: BinarySource) => void
  binaryPath: string
  onBinaryPathChange: (value: string) => void
  binaryPathInputRef: RefObject<HTMLInputElement | null>
  onPickBinary: () => void
  resolvingBinary: boolean
  resolvedBinaryPath: string | null
  connectionDirty: boolean
  onRevert: () => void
  revertDisabled: boolean
}

const BINARY_SOURCES: BinarySource[] = ['bundled', 'path', 'custom']

const BINARY_SOURCE_KEYS: Record<BinarySource, { title: MessageKey; description: MessageKey; notFound: MessageKey }> = {
  bundled: {
    title: 'settings.binarySource.bundled',
    description: 'settings.binarySource.bundledDesc',
    notFound: 'settings.binaryNotFound.bundled'
  },
  path: {
    title: 'settings.binarySource.path',
    description: 'settings.binarySource.pathDesc',
    notFound: 'settings.binaryNotFound.path'
  },
  custom: {
    title: 'settings.binarySource.custom',
    description: 'settings.binarySource.customDesc',
    notFound: 'settings.binaryNotFound.custom'
  }
}

export function WorkspaceSegment({
  connectionMode,
  onConnectionModeChange,
  activeRemoteStackConnection,
  manualRemoteConnection,
  remoteUrl,
  onRemoteUrlChange,
  remoteUrlErrorKey,
  remoteToken,
  onRemoteTokenChange,
  binarySource,
  onBinarySourceChange,
  binaryPath,
  onBinaryPathChange,
  binaryPathInputRef,
  onPickBinary,
  resolvingBinary,
  resolvedBinaryPath,
  connectionDirty,
  onRevert,
  revertDisabled
}: WorkspaceSegmentProps): JSX.Element {
  const t = useT()
  const remote = connectionMode === 'remote'
  // Only local-only controls dim; the heading and the footnote explaining why stay lit.
  const inertInRemoteMode: CSSProperties | undefined = remote
    ? { opacity: 0.55, pointerEvents: 'none' }
    : undefined

  return (
    <>
      <SettingsGroup title={t('settings.group.connection')}>
        <SettingsRow
          label={t('settings.connection.mode')}
          description={t('settings.connectionModeHint')}
          htmlFor="settings-connection-mode"
          control={
            <SettingsSelect
              id="settings-connection-mode"
              value={connectionMode}
              onValueChange={(mode) => {
                onConnectionModeChange(mode as ConnectionMode)
              }}
              options={[
                { value: 'local', label: t('settings.connectionMode.local') },
                { value: 'remote', label: t('settings.connectionMode.remote') }
              ]}
            />
          }
        />

        {activeRemoteStackConnection && (
          <SettingsRow orientation="block" label={t('settings.remoteStackManaged.title')}>
            <div
              style={{
                border: '1px solid var(--border-default)',
                borderLeft: '3px solid var(--accent)',
                borderRadius: '8px',
                background: 'var(--bg-secondary)',
                color: 'var(--text-secondary)',
                fontSize: '12px',
                lineHeight: 1.5,
                padding: '10px 12px'
              }}
            >
              {t('settings.remoteStackManaged.description')}
            </div>
          </SettingsRow>
        )}

        {manualRemoteConnection && (
          <SettingsRow orientation="block" label={t('settings.remoteUrl')} htmlFor="settings-remote-url">
            <Input
              id="settings-remote-url"
              value={remoteUrl}
              onChange={(e) => onRemoteUrlChange(e.target.value)}
              placeholder="ws://127.0.0.1:9100/ws"
              mono
            />
            {remoteUrlErrorKey && (
              <div style={{ ...settingsErrorTextStyle(false), marginTop: '6px' }}>{t(remoteUrlErrorKey)}</div>
            )}
            <label style={{ ...settingsSectionLabelStyle(), marginTop: '10px' }}>{t('settings.remoteToken')}</label>
            <SecretInput
              value={remoteToken}
              onChange={onRemoteTokenChange}
              placeholder={t('settings.remoteTokenPlaceholder')}
              mono
            />
          </SettingsRow>
        )}
      </SettingsGroup>

      <SettingsGroup title={t('settings.group.localAppServer')}>
        <SettingsRow orientation="block" style={inertInRemoteMode}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', width: '100%' }}>
            {BINARY_SOURCES.map((source) => {
              const keys = BINARY_SOURCE_KEYS[source]
              const showError = !resolvingBinary && !resolvedBinaryPath
              return (
                <SelectionCard
                  key={source}
                  name="settings-binary-source"
                  value={source}
                  active={binarySource === source}
                  onSelect={() => onBinarySourceChange(source)}
                  title={t(keys.title)}
                  description={t(keys.description)}
                  errorHint={showError ? t(keys.notFound) : undefined}
                  extra={
                    source === 'custom' ? (
                      <InputWithAction
                        id="settings-binary-path"
                        inputRef={binaryPathInputRef}
                        mono
                        value={binaryPath}
                        onChange={(e) => onBinaryPathChange(e.target.value)}
                        placeholder={t('settings.binaryPlaceholder')}
                        onInputClick={(e) => e.stopPropagation()}
                        actionIcon={<FolderIcon size={16} />}
                        actionLabel={t('settings.binaryBrowse')}
                        onAction={(e) => {
                          e.stopPropagation()
                          onPickBinary()
                        }}
                      />
                    ) : undefined
                  }
                />
              )
            })}
            {resolvingBinary && <div style={settingsHintStyle(false)}>{t('settings.binaryResolving')}</div>}
          </div>
        </SettingsRow>

        {connectionDirty && (
          <SettingsRow
            style={inertInRemoteMode}
            description={t(remote ? 'settings.pendingChanges.connectionRemote' : 'settings.pendingChanges.connection')}
            control={
              <Button onClick={onRevert} disabled={revertDisabled}>
                {t('settings.llm.revert')}
              </Button>
            }
          />
        )}

        {remote && (
          <SettingsRow>
            <div style={settingsHintStyle(false)}>{t('settings.localAppServerRemoteHint')}</div>
          </SettingsRow>
        )}
      </SettingsGroup>
    </>
  )
}
