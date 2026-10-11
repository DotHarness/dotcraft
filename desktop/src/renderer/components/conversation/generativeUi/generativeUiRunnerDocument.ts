import frameSource from './runner/generativeUiRunnerFrame.ts?bundled'
import workerSource from './runner/generativeUiWorker.ts?bundled'
import { GENERATIVE_UI_WORKER_SOURCE_ELEMENT_ID } from './generativeUiProtocol'

const RUNNER_CSP = [
  "default-src 'none'",
  "script-src 'unsafe-inline' 'unsafe-eval' blob:",
  'worker-src blob:',
  "connect-src 'none'",
  "img-src 'none'",
  "style-src 'none'",
  "base-uri 'none'",
  "form-action 'none'"
].join('; ')

let cachedDocument: string | null = null

export function generativeUiRunnerDocument(): string {
  cachedDocument ??= [
    '<!doctype html><html><head><meta charset="utf-8">',
    `<meta http-equiv="Content-Security-Policy" content="${RUNNER_CSP}">`,
    '</head><body>',
    `<script type="application/json" id="${GENERATIVE_UI_WORKER_SOURCE_ELEMENT_ID}">${JSON.stringify(workerSource).replace(/</g, '\\u003c')}</script>`,
    `<script>${frameSource.replace(/<\/script/gi, '<\\/script')}</script>`,
    '</body></html>'
  ].join('')
  return cachedDocument
}
