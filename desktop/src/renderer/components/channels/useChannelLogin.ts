import { useEffect, useRef, useState } from 'react'
import type { DiscoveredModule, ModuleStatusEntry, QrUpdatePayload } from '../../../preload/api.d'
import type { ChannelLoginPhase } from './ChannelLoginDialog'

export interface ChannelLogin {
  open: boolean
  phase: ChannelLoginPhase
  qrDataUrl: string | null
  show: () => void
  hide: () => void
}

export function useChannelLogin(
  module: DiscoveredModule | null,
  status: ModuleStatusEntry | undefined
): ChannelLogin {
  const moduleId = module?.requiresInteractiveSetup ? module.moduleId : null
  const [qrByModule, setQrByModule] = useState<Record<string, string | null>>({})
  const [openModuleId, setOpenModuleId] = useState<string | null>(null)
  const dismissedModuleIdRef = useRef<string | null>(null)

  useEffect(() => {
    return window.api.modules.onQrUpdate((payload: QrUpdatePayload) => {
      setQrByModule((prev) => ({ ...prev, [payload.moduleId]: payload.qrDataUrl }))
    })
  }, [])

  useEffect(() => {
    if (!moduleId) return
    let cancelled = false
    window.api.modules
      .qrStatus(moduleId)
      .then((state) => {
        if (!cancelled) setQrByModule((prev) => ({ ...prev, [moduleId]: state.qrDataUrl }))
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [moduleId])

  const qrDataUrl = moduleId ? qrByModule[moduleId] ?? null : null
  const running = status?.processState === 'starting' || status?.processState === 'running'
  const connected = status?.connected === true

  useEffect(() => {
    if (!running) dismissedModuleIdRef.current = null
  }, [running])

  useEffect(() => {
    if (!moduleId || !running || connected || !qrDataUrl) return
    if (dismissedModuleIdRef.current === moduleId) return
    setOpenModuleId(moduleId)
  }, [moduleId, running, connected, qrDataUrl])

  const open = moduleId !== null && openModuleId === moduleId
  const phase: ChannelLoginPhase = connected
    ? 'connected'
    : status?.processState === 'crashed'
      ? 'failed'
      : 'qr'

  return {
    open,
    phase,
    qrDataUrl,
    show: () => {
      if (!moduleId) return
      dismissedModuleIdRef.current = null
      setQrByModule((prev) => ({ ...prev, [moduleId]: null }))
      setOpenModuleId(moduleId)
    },
    hide: () => {
      dismissedModuleIdRef.current = moduleId
      setOpenModuleId(null)
    }
  }
}
