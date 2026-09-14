import { useEffect, useId, useRef } from 'react'
import { registerComposerVoiceTarget } from '../../voice/composerDraftBridge'
import { useVoiceStore } from '../../voice/voiceStore'
import { useComposerDraftStore } from '../../stores/composerDraftStore'

export function useCommentVoice(
  comment: string,
  onChange: (text: string) => void,
) {
  const id = useId()
  const origin = `comment-${id}`
  const current = useRef({ comment, onChange })
  current.current = { comment, onChange }
  const busy = useVoiceStore(
    (state) =>
      state.recording?.threadId === origin ||
      state.finalizing?.threadId === origin,
  )
  useEffect(() => {
    const unregister = registerComposerVoiceTarget(origin, {
      capture: () => ({
        text: current.current.comment,
        segments: [{ type: 'text', value: current.current.comment }],
        images: [],
        files: [],
      }),
      apply: (draft) => current.current.onChange(draft.text),
      submit: async (draft) => {
        current.current.onChange(draft.text)
      },
    })
    return () => {
      unregister()
      void useVoiceStore.getState().discardOrigin(origin)
      useComposerDraftStore.getState().clearDraft(origin)
    }
  }, [origin])
  return { origin, busy }
}
