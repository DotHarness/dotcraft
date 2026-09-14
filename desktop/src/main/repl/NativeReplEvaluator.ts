import { Session, type Runtime } from 'node:inspector'
import { constants, createContext } from 'node:vm'
import { createRequire } from 'node:module'
import { create as createDomain } from 'node:domain'
import { join } from 'node:path'

export class NativeReplEvaluator {
  private readonly session = new Session()
  private readonly globals: Record<string, unknown>
  private contextId = 0

  constructor() {
    this.session.connect()
    this.session.on('Runtime.executionContextCreated', ({ params }) => {
      if (params.context.name === 'DotCraft REPL') this.contextId = params.context.id
    })
    this.session.post('Runtime.enable')
    const descriptors: Record<string, PropertyDescriptor> = Object.getOwnPropertyDescriptors(globalThis)
    delete descriptors.globalThis
    delete descriptors.global
    this.globals = createContext(Object.defineProperties({}, descriptors), {
      name: 'DotCraft REPL',
      importModuleDynamically: constants.USE_MAIN_CONTEXT_DEFAULT_LOADER
    })
    this.globals.global = this.globals
  }

  async evaluate(code: string, workspacePath: string, evaluationId: string): Promise<{ resultText?: string; error?: string }> {
    this.globals.require = createRequire(join(workspacePath, 'NodeReplJs.cjs'))
    const domain = createDomain()
    try {
      return await new Promise((resolve, reject) => {
        domain.on('error', reject)
        domain.run(() => {
          const params: Runtime.EvaluateParameterType & { replMode: boolean } = {
            expression: code, contextId: this.contextId, replMode: true,
            awaitPromise: true, objectGroup: evaluationId
          }
          this.session.post('Runtime.evaluate', params, (error, response) => {
            if (error) { reject(error); return }
            if (response.exceptionDetails) {
              resolve({ error: response.exceptionDetails.exception?.description ?? response.exceptionDetails.text })
              return
            }
            void this.describe(response.result).then(resultText => resolve({ resultText }), reject)
          })
        })
      })
    } finally {
      this.session.post('Runtime.releaseObjectGroup', { objectGroup: evaluationId })
    }
  }

  private async describe(result: Runtime.RemoteObject): Promise<string> {
    if (result.type === 'undefined' || result.subtype === 'null') return ''
    if (result.unserializableValue) return result.unserializableValue
    if (!result.objectId) return typeof result.value === 'string' ? result.value : String(result.value ?? result.description ?? '')
    return await new Promise((resolve, reject) => {
      this.session.post('Runtime.callFunctionOn', {
        objectId: result.objectId,
        functionDeclaration: 'function() { try { return JSON.stringify(this, null, 2) ?? String(this); } catch { return String(this); } }',
        returnByValue: true
      }, (error, response) => error ? reject(error) : resolve(String(response.result.value ?? response.result.description ?? '')))
    })
  }
}
