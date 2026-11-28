import { SummaryRow, type DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { BookOpen } from 'lucide-react'
import { useEffect, useState, type JSX } from 'react'
import { openMemoryDialog } from '../state/memoryDialog'
import { collectThreadMemories, readThreadMemoryItems, type ThreadMemory } from './threadMemories'

export const MEMORY_VIEW_ID = 'memory'

export function MemorySources({ host, context }: DesktopPluginSurfaceProps<'summary.sources'>): JSX.Element {
  const { threadId, workspacePath, busy } = context
  const [memories, setMemories] = useState<ThreadMemory[]>([])

  // Re-read when a turn ends, since its memory operations are recorded by then.
  useEffect(() => {
    if (busy) return
    let cancelled = false
    readThreadMemoryItems(host.appServer, threadId).then(
      (items) => { if (!cancelled) setMemories(collectThreadMemories(items, workspacePath)) },
      () => { if (!cancelled) setMemories([]) }
    )
    return () => { cancelled = true }
  }, [host, threadId, workspacePath, busy])

  return (
    <>
      {memories.map((memory) => (
        <SummaryRow
          key={`${memory.store?.id ?? ''}|${memory.path}`}
          leading={<BookOpen size={16} strokeWidth={1.75} />}
          label={memory.fileName}
          meta={memory.store?.name}
          title={memory.path}
          onClick={() => openMemoryDialog({
            mode: 'existing',
            fileName: memory.fileName,
            path: memory.path,
            storeId: memory.store?.id ?? null,
            workspacePath
          })}
        />
      ))}
    </>
  )
}
