import { useT } from '../../contexts/LocaleContext'
import { ChannelFormPage } from './ChannelFormPage'
import {
  ChannelModuleDetailPage,
  type ChannelModuleDetailMode
} from './ChannelModuleDetailPage'
import {
  ExternalChannelConfigForm,
  type ExternalChannelConfigWire
} from './ExternalChannelConfigForm'

interface ExternalChannelDetailPageProps {
  value: ExternalChannelConfigWire
  isNew: boolean
  saving: boolean
  deleting: boolean
  /** Some servers do not offer external channel management. */
  available: boolean
  initialMode?: ChannelModuleDetailMode
  onChange: (next: ExternalChannelConfigWire) => void
  /** Resolves false when the upsert failed, so manage mode stays open. */
  onSave: () => Promise<boolean>
  onToggleEnabled: () => void
  onDelete: () => void
  onBack: () => void
}

function transportLabel(transport: ExternalChannelConfigWire['transport']): string {
  if (transport === 'websocket') return 'WebSocket'
  if (transport === 'managedWebsocket') return 'Managed WebSocket'
  return 'Subprocess'
}

/**
 * Creation is the form page alone: there is nothing to preview before the
 * channel exists. Once saved, it earns the preview module channels get.
 */
export function ExternalChannelDetailPage({
  value,
  isNew,
  saving,
  deleting,
  available,
  initialMode,
  onChange,
  onSave,
  onToggleEnabled,
  onDelete,
  onBack
}: ExternalChannelDetailPageProps): JSX.Element {
  const t = useT()

  if (!available) {
    return (
      <ChannelFormPage
        breadcrumbLabel={t('channels.external.title')}
        title={t('channels.external.title')}
        description={t('channels.external.unavailable')}
        onBack={onBack}
      />
    )
  }

  if (isNew) {
    return (
      <ChannelFormPage
        breadcrumbLabel={t('channels.external.new')}
        title={t('channels.external.new')}
        description={t('channels.external.createIntro')}
        onBack={onBack}
      >
        <ExternalChannelConfigForm
          value={value}
          saving={saving}
          deleting={deleting}
          isNew
          onChange={onChange}
          onSave={() => {
            void onSave()
          }}
          onCancel={onBack}
        />
      </ChannelFormPage>
    )
  }

  const title = value.name || t('channels.external.title')

  return (
    <ChannelModuleDetailPage
      title={title}
      subtitle={t('channels.external.detailShort')}
      previewPrompt={t('channels.external.previewPrompt')}
      description={t('channels.external.detailLong')}
      infoItems={[
        { label: t('channels.detail.source'), value: t('channels.external.title') },
        { label: t('channels.detail.transports'), value: transportLabel(value.transport) },
        ...(value.transport === 'websocket'
          ? []
          : [{ label: t('channels.external.command'), value: value.command?.trim() || '-' }])
      ]}
      active={value.enabled}
      busy={saving}
      controlsAvailable
      initialMode={initialMode}
      onBack={onBack}
      onToggleConnection={onToggleEnabled}
      renderManage={({ onCancel, onSaved }) => (
        <ExternalChannelConfigForm
          value={value}
          saving={saving}
          deleting={deleting}
          isNew={false}
          onChange={onChange}
          onSave={() => {
            void onSave().then((saved) => {
              if (saved) onSaved()
            })
          }}
          onCancel={onCancel}
          onDelete={onDelete}
        />
      )}
    />
  )
}
