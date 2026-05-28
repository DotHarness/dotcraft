import { create } from 'zustand'

interface PendingRestartState {
  signature: string | null
  hiddenSignature: string | null
  applying: boolean
  visible: boolean
  messageKey: string
  applyKey: string
  applyingKey: string
}

interface PendingRestartActions {
  setPending(
    signature: string,
    onApply: () => Promise<void>,
    labels?: { messageKey?: string; applyKey?: string; applyingKey?: string }
  ): void
  clear(): void
  ignore(): void
  apply(): Promise<void>
}

type PendingRestartStore = PendingRestartState & PendingRestartActions

let pendingRestartApply: (() => Promise<void>) | null = null

const initialState: PendingRestartState = {
  signature: null,
  hiddenSignature: null,
  applying: false,
  visible: false,
  messageKey: 'settings.pendingRestart.message',
  applyKey: 'settings.pendingRestart.apply',
  applyingKey: 'settings.action.restarting'
}

export const usePendingRestartStore = create<PendingRestartStore>((set, get) => ({
  ...initialState,

  setPending(signature, onApply, labels) {
    pendingRestartApply = onApply
    set((state) => {
      const visible = state.hiddenSignature !== signature
      const messageKey = labels?.messageKey ?? initialState.messageKey
      const applyKey = labels?.applyKey ?? initialState.applyKey
      const applyingKey = labels?.applyingKey ?? initialState.applyingKey
      if (
        state.signature === signature &&
        state.visible === visible &&
        state.messageKey === messageKey &&
        state.applyKey === applyKey &&
        state.applyingKey === applyingKey
      ) {
        return state
      }
      return {
        signature,
        applying: state.applying,
        visible,
        messageKey,
        applyKey,
        applyingKey
      }
    })
  },

  clear() {
    pendingRestartApply = null
    set(initialState)
  },

  ignore() {
    const signature = get().signature
    if (!signature) return
    set({ hiddenSignature: signature, visible: false })
  },

  async apply() {
    const onApply = pendingRestartApply
    if (!onApply || get().applying) return
    set({ applying: true, visible: true })
    try {
      await onApply()
    } finally {
      set((state) => ({
        applying: false,
        visible: state.signature != null && state.hiddenSignature !== state.signature
      }))
    }
  }
}))
