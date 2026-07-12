interface CommandExecuteResult {
  handled?: boolean
  expandedPrompt?: string | null
}

export async function expandInitCommand(threadId: string): Promise<string> {
  const result = await window.api.appServer.sendRequest('command/execute', {
    threadId,
    command: '/init',
    arguments: []
  }) as CommandExecuteResult
  const prompt = typeof result.expandedPrompt === 'string' ? result.expandedPrompt.trim() : ''
  if (result.handled === false || prompt.length === 0) {
    throw new Error('The server did not return an initialization prompt.')
  }
  return prompt
}
