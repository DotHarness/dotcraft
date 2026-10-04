import { encodeUtf8 } from '../core/utf8'

const CODE = [
  'export async function persistSnapshot(turnId: string, diff: string): Promise<void> {',
  '  const previous = pending.get(turnId) ?? Promise.resolve()',
  '  const next = previous.then(() => writer.write(turnId, diff))',
  '  pending.set(turnId, next)',
  '  await next',
  '}',
  '',
  'export function readSnapshot(turnId: string): string | undefined {',
  '  return snapshots.get(turnId)',
  '}',
  '',
  'export async function settled(turnId: string): Promise<void> {',
  '  await pending.get(turnId)',
  '  pending.delete(turnId)',
  '}',
]

const CONTEXT = ['import { writer } from "./snapshotWriter"', '', 'const pending = new Map<string, Promise<void>>()']

function line(index: number): string {
  return CODE[index % CODE.length]
}

export function sampleDiff(path: string, added: number, removed: number, created = false): string {
  const header = created
    ? [`diff --git a/${path} b/${path}`, 'new file mode 100644', '--- /dev/null', `+++ b/${path}`]
    : [`diff --git a/${path} b/${path}`, `--- a/${path}`, `+++ b/${path}`]
  const context = created ? [] : CONTEXT
  const start = created ? 0 : 12
  const body = [
    `@@ -${start},${context.length + removed} +${created ? 1 : start},${context.length + added} @@`,
    ...context.map((text) => ` ${text}`),
    ...Array.from({ length: removed }, (_, index) => `-${line(index + 7)}`.replace('readSnapshot', 'peekSnapshot')),
    ...Array.from({ length: added }, (_, index) => `+${line(index)}`),
  ]
  return [...header, ...body, ''].join('\n')
}

export function textBase64(text: string): string {
  return btoa(String.fromCharCode(...encodeUtf8(text)))
}
