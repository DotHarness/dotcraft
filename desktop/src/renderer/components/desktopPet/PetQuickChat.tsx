import { useLayoutEffect, useRef, useState } from 'react'
import { ComposerShell } from '../conversation/ComposerShell'
import { ComposerSubmitButton } from '../conversation/ComposerSubmitButton'
import { RichInputArea, type RichInputAreaHandle } from '../conversation/RichInputArea'
import { useT } from '../../contexts/LocaleContext'

export function PetQuickChat({ text, onChange, onSubmit }: {
  text: string; onChange: (text: string) => void; onSubmit: () => void
}): JSX.Element {
  const editor = useRef<RichInputAreaHandle>(null)
  const [focused, setFocused] = useState(false)
  const t = useT()
  useLayoutEffect(() => {
    if (editor.current?.getText() !== text) editor.current?.setPlainText(text)
  }, [text])
  const submit = (): void => { if (editor.current?.getText().trim()) onSubmit() }
  return <div className="desktop-pet-quick-chat">
    <ComposerShell dragOver={false} dropLabel="" focused={focused}
      desktopPluginSurfaceContext={{ workspacePath: '', threadId: null, mode: 'agent', busy: false, awaitingApproval: false, variant: 'default', minimalChrome: true }}
      onDragOver={event => event.preventDefault()} onDragLeave={() => {}} onDrop={event => event.preventDefault()}
      editor={<div className="desktop-pet-composer-row">
        <RichInputArea ref={editor} chrome="inline" placeholder={t('composer.placeholder.ask')}
          onContentChange={() => { const value = editor.current?.getText() ?? ''; if (value !== text) onChange(value) }}
          onSubmit={submit} onFocusChange={setFocused} />
        <ComposerSubmitButton mode="send" disabled={!text.trim()} onClick={submit} />
      </div>} footerLeading={null} footerAction={null} />
  </div>
}
