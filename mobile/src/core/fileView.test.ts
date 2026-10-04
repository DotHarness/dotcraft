import { JsonRpcError } from '@dotcraft/sdk/wire'
import { describe, expect, it } from 'vitest'
import { fileContent, fileFailure } from './fileView'

const base64 = (bytes: number[]) => btoa(String.fromCharCode(...bytes))

describe('file view', () => {
  it('decodes text as UTF-8 and drops a byte order mark', () => {
    expect(fileContent('D:/p/notes.md', base64([0xef, 0xbb, 0xbf, 0x23, 0x20, 0xe6, 0x96, 0x87, 0x0a]))).toEqual({ kind: 'text', text: '# 文\n' })
  })

  it('shows images as images and anything binary as another type', () => {
    expect(fileContent('/p/Banner.PNG', 'iVBORw0K')).toEqual({ kind: 'image', uri: 'data:image/png;base64,iVBORw0K' })
    expect(fileContent('/p/app.zip', base64([0x50, 0x4b, 0x03, 0x04, 0x00]))).toEqual({ kind: 'other' })
    expect(fileContent('/p/latin1.txt', base64([0x63, 0x61, 0x66, 0xe9]))).toEqual({ kind: 'other' })
  })

  it('tells a file over the read limit apart from other failures', () => {
    expect(fileFailure(new JsonRpcError(-32103, 'FileTooLarge', { code: 'FileTooLarge', params: { path: '/p/big.log' } }))).toBe('tooLarge')
    expect(fileFailure(new JsonRpcError(-32103, 'FileNotFound', { code: 'FileNotFound' }))).toBe('failed')
    expect(fileFailure(new Error('offline'))).toBe('failed')
  })
})
