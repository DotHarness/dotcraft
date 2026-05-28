export async function setupBrowserRuntime(options = {}) {
  const globals = options.globals ?? globalThis
  const setup =
    globals.__dotcraftSetupBrowserRuntime ??
    globalThis.__dotcraftSetupBrowserRuntime
  if (typeof setup !== 'function') {
    throw new Error('DotCraft IAB runtime is not available in this Node REPL context.')
  }
  return setup({ ...options, globals })
}
