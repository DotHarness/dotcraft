import { useLayoutEffect, useRef, useState } from 'react'
import { ComposerShell } from '../conversation/ComposerShell'
import { RichInputArea, type RichInputAreaHandle } from '../conversation/RichInputArea'
import { useT } from '../../contexts/LocaleContext'
import type { PetFollowUpMode, PetStatusInfo } from '../../../shared/desktopPet'
import { PetSubmitControl } from './PetSubmitControl'

export function PetQuickChat({ text, busy = false, status, followUpMode, onChange, onSubmit, onStop }: {
  text: string
  busy?: boolean
  status: PetStatusInfo | undefined
  followUpMode: PetFollowUpMode
  onChange: (text: string) => void
  onSubmit: () => void
  onStop: () => void
}): JSX.Element {
  const editor = useRef<RichInputAreaHandle>(null)
  const [focused, setFocused] = useState(false)
  const t = useT()
  useLayoutEffect(() => {
    if (editor.current?.getText() !== text) editor.current?.setPlainText(text)
  }, [text])
  const submit = (): void => { if (!busy && editor.current?.getText().trim()) onSubmit() }
  return <div className="desktop-pet-quick-chat">
    <ComposerShell dragOver={false} dropLabel="" focused={focused}
      desktopPluginSurfaceContext={{ workspacePath: '', threadId: null, mode: 'agent', busy, awaitingApproval: false, variant: 'default', minimalChrome: true }}
      onDragOver={event => event.preventDefault()} onDragLeave={() => {}} onDrop={event => event.preventDefault()}
      editor={<div className="desktop-pet-composer-row">
        <RichInputArea ref={editor} chrome="inline" placeholder={t('composer.placeholder.ask')} disabled={busy}
          onContentChange={() => { const value = editor.current?.getText() ?? ''; if (value !== text) onChange(value) }}
          onSubmit={submit} onFocusChange={setFocused} />
        <PetSubmitControl status={status} followUpMode={followUpMode} hasDraft={text.trim().length > 0} busy={busy} onSubmit={submit} onStop={onStop} />
      </div>} footerLeading={null} footerAction={null} />
  </div>
}
