import type { JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { ComposerSendButton, SendIcon, SendProcessingIcon, StopIcon } from './ComposerShell'

export type ComposerSubmitMode = 'send' | 'steer' | 'queue' | 'stop' | 'stopping'
export type ComposerSubmitGlyph = 'send' | 'stop' | 'stopping'

const modes: Record<ComposerSubmitMode, { title: string; aria: string; glyph: ComposerSubmitGlyph }> = {
  send: { title: 'composer.sendAriaAlt', aria: 'composer.sendAriaAlt', glyph: 'send' },
  steer: { title: 'composer.steerSendTitle', aria: 'composer.steerSendAria', glyph: 'send' },
  queue: { title: 'composer.queueSendTitle', aria: 'composer.queueSendAria', glyph: 'send' },
  stop: { title: 'composer.stopTitle', aria: 'composer.stopAria', glyph: 'stop' },
  stopping: { title: 'composer.stoppingTitle', aria: 'composer.stoppingAria', glyph: 'stopping' }
}

interface ComposerSubmitButtonProps {
  mode: ComposerSubmitMode
  onClick: () => void | Promise<void>
  disabled?: boolean
  tone?: 'enabled' | 'disabled'
}

export function ComposerSubmitButton({
  mode,
  onClick,
  disabled = false,
  tone = disabled ? 'disabled' : 'enabled'
}: ComposerSubmitButtonProps): JSX.Element {
  const t = useT()
  const { title, aria, glyph } = modes[mode]
  const shortcut = mode === 'send' && !disabled
    ? ACTION_SHORTCUTS.send
    : mode === 'stop'
      ? ACTION_SHORTCUTS.cancel
      : undefined

  return (
    <ActionTooltip label={t(title)} shortcut={shortcut} placement="top">
      <ComposerSendButton
        tone={tone}
        onClick={() => {
          void onClick()
        }}
        disabled={disabled}
        aria-label={t(aria)}
        aria-busy={mode === 'stopping' ? true : undefined}
      >
        <ComposerSubmitGlyphs glyph={glyph} />
      </ComposerSendButton>
    </ActionTooltip>
  )
}

export function ComposerSubmitGlyphs({ glyph }: { glyph: ComposerSubmitGlyph }): JSX.Element {
  return (
    <span className="composer-submit-glyphs" data-glyph={glyph}>
      <span className="composer-submit-glyph composer-submit-glyph--send"><SendIcon /></span>
      <span className="composer-submit-glyph composer-submit-glyph--stop"><StopIcon /></span>
      <span className="composer-submit-glyph composer-submit-glyph--stopping"><SendProcessingIcon /></span>
    </span>
  )
}
