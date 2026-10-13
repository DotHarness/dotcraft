import { embedInDocs } from '../demo/docsEmbed'
import { createDemoRuntime } from '../demo/runtime'
import type { AppRuntime } from './runtime'

export function createRuntime(): AppRuntime {
  const runtime = createDemoRuntime()
  embedInDocs(runtime.session)
  return runtime
}
