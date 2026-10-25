import { memo, useCallback, useState } from 'react'
import { useComputerLink, useComputerSelector } from '../../app-state/SessionContext'
import { usedShare } from '../../core/contextUsage'
import type { ComputerState } from '../../core/state'
import { ContextRing } from './ContextRing'
import { StatusPopover } from './StatusPopover'

export const ChatStatus = memo(function ChatStatus({
  chatKey,
  threadId,
  projectId,
  providerId,
  folder,
}: {
  chatKey: string
  threadId: string
  projectId: string
  providerId: string | null
  folder: string | null
}) {
  const session = useComputerLink()
  const [open, setOpen] = useState(false)
  const selectContext = useCallback((state: ComputerState) => state.details[chatKey]?.context ?? null, [chatKey])
  const selectUsage = useCallback(
    (state: ComputerState) => (open && providerId ? (state.models[projectId]?.usage[providerId] ?? null) : null),
    [open, providerId, projectId],
  )
  const context = useComputerSelector(selectContext)
  const usage = useComputerSelector(selectUsage)
  return (
    <>
      <ContextRing
        used={context ? usedShare(context) : null}
        onPress={() => {
          setOpen(true)
          void session.loadUsage(projectId, providerId).catch(() => undefined)
        }}
      />
      <StatusPopover visible={open} chatId={threadId} folder={folder} context={context} usage={usage} onClose={() => setOpen(false)} />
    </>
  )
})
