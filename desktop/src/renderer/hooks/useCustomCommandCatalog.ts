import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { translate, type AppLocale } from '../../shared/locales'

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

function serverFallback(locale: AppLocale, key: string, fallback: string): string {
  const trimmedKey = key.trim()
  if (!trimmedKey) return fallback
  const localized = translate(locale, trimmedKey)
  return localized === trimmedKey ? fallback : localized
}

function parseCustomCommands(payload: unknown, locale: AppLocale): CustomCommandInfo[] {
  const typed = payload as { commands?: unknown[] }
  const rawList = Array.isArray(typed.commands) ? typed.commands : []
  const mapped = rawList
    .map((entry) => {
      const item = entry as {
        name?: unknown
        aliases?: unknown
        descriptionKey?: unknown
        fallbackDescription?: unknown
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
        description: serverFallback(
          locale,
          typeof item.descriptionKey === 'string' ? item.descriptionKey : '',
          typeof item.fallbackDescription === 'string'
            ? item.fallbackDescription
            : typeof item.description === 'string'
              ? item.description
              : ''
        ),
        category,
        requiresAdmin: Boolean(item.requiresAdmin)
      } satisfies CustomCommandInfo
    })
    .filter((item): item is CustomCommandInfo => item !== null)
  return mapped.sort((a, b) => a.name.localeCompare(b.name))
}

function includesInitCommand(payload: unknown): boolean {
  const typed = payload as { commands?: unknown[] }
  return Array.isArray(typed.commands) && typed.commands.some((entry) => {
    const name = typeof (entry as { name?: unknown }).name === 'string'
      ? (entry as { name: string }).name.trim()
      : ''
    return name.toLowerCase() === '/init'
  })
}

export function useCustomCommandCatalog({
  enabled,
  locale
}: UseCustomCommandCatalogArgs): {
  commands: CustomCommandInfo[]
  initAvailable: boolean
  status: LoadStatus
  reload: () => Promise<void>
} {
  const [commands, setCommands] = useState<CustomCommandInfo[]>([])
  const [initAvailable, setInitAvailable] = useState(false)
  const [status, setStatus] = useState<LoadStatus>('idle')
  const reqRef = useRef(0)

  const fetchCommands = useCallback(async () => {
    if (!enabled) {
      setCommands([])
      setInitAvailable(false)
      setStatus('idle')
      return
    }
    const reqId = ++reqRef.current
    setStatus('loading')
    try {
      const payload = await window.api.appServer.sendRequest('command/list', {})
      if (reqId !== reqRef.current) return
      setCommands(parseCustomCommands(payload, locale))
      setInitAvailable(includesInitCommand(payload))
      setStatus('ready')
    } catch {
      if (reqId !== reqRef.current) return
      setCommands([])
      setInitAvailable(false)
      setStatus('error')
    }
  }, [enabled, locale])

  useEffect(() => {
    void fetchCommands()
  }, [fetchCommands])

  return useMemo(
    () => ({
      commands,
      initAvailable,
      status,
      reload: fetchCommands
    }),
    [commands, fetchCommands, initAvailable, status]
  )
}
