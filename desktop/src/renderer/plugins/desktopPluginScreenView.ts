import type {
  DesktopPluginScreenFrame,
  DesktopPluginScreenStatus,
  DesktopPluginScreenView
} from '@dotcraft/plugin'

interface ScreenViewScope {
  active: boolean
  cleanups: Set<() => void>
}

export function createDesktopPluginScreenView(scope: ScreenViewScope): DesktopPluginScreenView {
  const requireActive = (): void => {
    if (!scope.active) throw new Error('Desktop extension is inactive.')
  }

  return {
    open(request) {
      requireActive()
      const viewId = `plugin-screen-${crypto.randomUUID()}`
      const states = new Set<(status: DesktopPluginScreenStatus) => void>()
      const frames = new Set<(frame: DesktopPluginScreenFrame) => void>()
      let closed = false
      let lastStatus: DesktopPluginScreenStatus | null = null
      const subscriptions: (() => void)[] = []
      const closeTransport = (): void => {
        void window.api.screenView.close({ viewId }).catch(() => {})
      }
      const close = (): void => {
        if (closed) return
        closed = true
        scope.cleanups.delete(close)
        for (const unsubscribe of subscriptions) unsubscribe()
        states.clear()
        frames.clear()
        closeTransport()
      }
      scope.cleanups.add(close)
      const publishState = (status: DesktopPluginScreenStatus): void => {
        if (closed || !scope.active) return
        lastStatus = status
        for (const listener of states) listener(status)
      }
      subscriptions.push(window.api.screenView.onState((payload) => {
        if (payload.viewId === viewId) publishState(payload.status)
      }))
      subscriptions.push(window.api.screenView.onFrame((payload) => {
        if (closed || !scope.active || payload.viewId !== viewId) return
        for (const listener of frames) listener(payload)
      }))
      const failed = (reason: unknown): void => {
        publishState({
          kind: 'unavailable',
          reason: 'hubRejected',
          detail: reason instanceof Error ? reason.message : String(reason)
        })
      }
      void window.api.screenView.open({ viewId, ...request }).then((result) => {
        if (closed || !scope.active) {
          closeTransport()
        } else if (!result.ok) {
          failed('The screen view could not be opened.')
        }
      }, failed)

      return {
        viewId,
        onState(listener) {
          if (closed || !scope.active) return () => {}
          states.add(listener)
          if (lastStatus) listener(lastStatus)
          return () => { states.delete(listener) }
        },
        onFrame(listener) {
          if (closed || !scope.active) return () => {}
          frames.add(listener)
          return () => { frames.delete(listener) }
        },
        async tune(maxWidth) {
          if (closed || !scope.active) return
          await window.api.screenView.tune({ viewId, maxWidth })
        },
        ack(sequence) {
          if (!closed && scope.active) window.api.screenView.ack({ viewId, sequence })
        },
        close
      }
    },
    async getLayout() {
      requireActive()
      const settings = await window.api.settings.get()
      requireActive()
      return { width: settings.screenViewDockWidth, position: settings.screenViewDockPosition }
    },
    async setLayout(layout) {
      requireActive()
      await window.api.settings.set({
        ...(layout.width == null ? {} : { screenViewDockWidth: layout.width }),
        ...(layout.position == null ? {} : { screenViewDockPosition: layout.position })
      })
    }
  }
}
