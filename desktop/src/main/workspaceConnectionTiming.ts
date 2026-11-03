export function startWorkspaceConnectionTiming(workspacePath: string) {
  const startedAt = performance.now()
  return (stage: 'hub-ready' | 'connection-ready', source: 'ensure-and-connect' | 'existing-connection'): void => {
    console.info('[desktop] workspace connection timing', {
      workspacePath,
      source,
      stage,
      elapsedMs: Math.round(performance.now() - startedAt)
    })
  }
}
