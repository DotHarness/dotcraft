import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { AppLocale } from '../../shared/locales'

export interface CustomCommandInfo {
  name: string
  aliases: string[]
  description: string
  category: string
  requiresAdmin: boolean
}

interface UseCustomCommandCatalogArgs {
  enabled: boolean
  locale: AppLocale
}

type LoadStatus = 'idle' | 'loading' | 'ready' | 'error'

function toCommandLanguage(locale: AppLocale): 'en' | 'zh' {
  return locale === 'zh-Hans' ? 'zh' : 'en'
}

function parseCustomCommands(payload: unknown): CustomCommandInfo[] {
  const typed = payload as { commands?: unknown[] }
  const rawList = Array.isArray(typed.commands) ? typed.commands : []
  const mapped = rawList
    .map((entry) => {
      const item = entry as {
        name?: unknown
        aliases?: unknown
        description?: unknown
        category?: unknown
        requiresAdmin?: unknown
      }
      const name = typeof item.name === 'string' ? item.name.trim() : ''
      if (!name.startsWith('/')) return null
      const category = typeof item.category === 'string' ? item.category.trim() : ''
      if (category.toLowerCase() !== 'custom') return null
      const aliases = Array.isArray(item.aliases)
        ? item.aliases
            .map((alias) => (typeof alias === 'string' ? alias.trim() : ''))
            .filter(Boolean)
        : []
      return {
        name,
        aliases,
        description: typeof item.description === 'string' ? item.description : '',
        category,
        requiresAdmin: Boolean(item.requiresAdmin)
      } satisfies CustomCommandInfo
    })
    .filter((item): item is CustomCommandInfo => item !== null)
  return mapped.sort((a, b) => a.name.localeCompare(b.name))
}

export function useCustomCommandCatalog({
  enabled,
  locale
}: UseCustomCommandCatalogArgs): {
  commands: CustomCommandInfo[]
  status: LoadStatus
  reload: () => Promise<void>
} {
  const [commands, setCommands] = useState<CustomCommandInfo[]>([])
  const [status, setStatus] = useState<LoadStatus>('idle')
  const reqRef = useRef(0)

  const fetchCommands = useCallback(async () => {
    if (!enabled) {
      setCommands([])
      setStatus('idle')
      return
    }
    const reqId = ++reqRef.current
    setStatus('loading')
    try {
      const payload = await window.api.appServer.sendRequest('command/list', {
        language: toCommandLanguage(locale)
      })
      if (reqId !== reqRef.current) return
      setCommands(parseCustomCommands(payload))
      setStatus('ready')
    } catch {
      if (reqId !== reqRef.current) return
      setCommands([])
      setStatus('error')
    }
  }, [enabled, locale])

  useEffect(() => {
    void fetchCommands()
  }, [fetchCommands])

  return useMemo(
    () => ({
      commands,
      status,
      reload: fetchCommands
    }),
    [commands, fetchCommands, status]
  )
}
