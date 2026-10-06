import { useEffect, useState, type JSX } from 'react'
import { usePluginStore, type PluginEntry } from '../../stores/pluginStore'
import { PluginInstallDialog } from '../plugins/PluginInstallDialog'

function needsTrust(plugin: PluginEntry): boolean {
  return plugin.dotnet != null && plugin.dotnetRuntime?.trustStatus !== 'trusted'
}

export function ImportedPluginTrust({ pluginIds, onDone }: { pluginIds: string[]; onDone: () => void }): JSX.Element | null {
  const plugins = usePluginStore((state) => state.plugins)
  const [queue, setQueue] = useState(() => pluginIds.map((id) => id.toLowerCase()))
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    void usePluginStore.getState().fetchPlugins().finally(() => setLoaded(true))
  }, [])

  const current = loaded
    ? queue.map((id) => plugins.find((plugin) => plugin.id.toLowerCase() === id)).find((plugin) => plugin != null && needsTrust(plugin))
    : undefined

  useEffect(() => {
    if (loaded && !current) onDone()
  }, [current, loaded, onDone])

  if (!current) return null
  const advance = (): void => setQueue((previous) => previous.filter((id) => id !== current.id.toLowerCase()))

  return (
    <PluginInstallDialog
      key={current.id}
      plugin={current}
      onInstall={() => undefined}
      onClose={advance}
      onTrust={async () => {
        const store = usePluginStore.getState()
        await store.setPluginTrusted(current.id, true)
        await store.fetchPlugins()
        if (!current.enabled) await store.togglePluginEnabled(current.id, true)
        advance()
      }}
    />
  )
}
