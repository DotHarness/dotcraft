import type { JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { InputComposer, type InputComposerSubmitPayload } from '../conversation/InputComposer'
import { useComposerModelControls } from '../conversation/useComposerModelControls'
import type { ThreadConfigurationWire } from '../../types/thread'
import { AgentBuilderChatEmptyState } from './AgentBuilderChatEmptyState'

export function DetachedAgentBuilderChat({
  creating,
  workspacePath,
  mascotName,
  prefillRequest,
  error,
  onPrefill,
  onSubmit
}: {
  creating: boolean
  workspacePath: string
  mascotName: string
  prefillRequest: { id: number; text: string } | null
  error: string | null
  onPrefill: (prompt: string) => void
  onSubmit: (payload: InputComposerSubmitPayload, config: ThreadConfigurationWire) => Promise<void> | void
}): JSX.Element {
  const t = useT()
  const modelControls = useComposerModelControls({
    workspacePath,
    mode: 'detached'
  })

  return (
    <div className="agent-builder-detached-chat">
      {error && (
        <div className="agent-builder-chat-error" role="alert">
          Couldn’t start the builder chat: {error}
        </div>
      )}
      <AgentBuilderChatEmptyState creating={creating} onPick={onPrefill} />
      <InputComposer
        threadId="agent-builder-detached"
        transientVoiceOrigin
        workspacePath={workspacePath}
        minimalChrome
        mascotName={mascotName}
        variant="agentBuilder"
        placeholder={creating ? t('agentBuilder.chat.createTitle') : undefined}
        prefillRequest={prefillRequest}
        submitOverride={(payload) => onSubmit(payload, modelControls.threadStartConfig)}
        modelName={modelControls.modelName}
        modelOptions={modelControls.modelOptions}
        modelCatalog={modelControls.modelCatalog}
        reasoningValue={modelControls.reasoningValue}
        modelLoading={modelControls.modelLoading}
        modelDisabled={modelControls.modelDisabled}
        modelListUnsupportedEndpoint={modelControls.modelListUnsupportedEndpoint}
        modelCatalogError={modelControls.modelCatalogError}
        modelCatalogErrorMessage={modelControls.modelCatalogErrorMessage}
        onModelChange={modelControls.onModelChange}
        onReasoningChange={modelControls.onReasoningChange}
        onModelCatalogRetry={modelControls.onModelCatalogRetry}
        contextMode={modelControls.contextMode}
        contextSupportsMax={modelControls.contextSupportsMax}
        contextDegraded={modelControls.contextDegraded}
        contextConfiguredWindow={modelControls.contextConfiguredWindow}
        onContextModeChange={modelControls.onContextModeChange}
      />
    </div>
  )
}
