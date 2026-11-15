import { net, type ClientRequest } from 'electron'
import { lookup as dnsLookup } from 'node:dns/promises'
import { BlockList, isIP } from 'node:net'
import { bareHostname } from '../shared/linkPreviews'

export class LinkPreviewBlockedError extends Error {}

const nonPublic = new BlockList()
for (const [address, prefix] of [
  ['0.0.0.0', 8],
  ['10.0.0.0', 8],
  ['100.64.0.0', 10],
  ['127.0.0.0', 8],
  ['169.254.0.0', 16],
  ['172.16.0.0', 12],
  ['192.0.0.0', 24],
  ['192.0.2.0', 24],
  ['192.168.0.0', 16],
  ['198.51.100.0', 24],
  ['203.0.113.0', 24],
  ['224.0.0.0', 4],
  ['240.0.0.0', 4]
] as const) {
  nonPublic.addSubnet(address, prefix, 'ipv4')
}
for (const [address, prefix] of [
  ['::', 127],
  ['64:ff9b::', 96],
  ['100::', 64],
  ['2001:db8::', 32],
  ['2002::', 16],
  ['fc00::', 7],
  ['fe80::', 10],
  ['ff00::', 8]
] as const) {
  nonPublic.addSubnet(address, prefix, 'ipv6')
}

function mappedIpv4(address: string): string | null {
  const dotted = /^::ffff:(\d{1,3}(?:\.\d{1,3}){3})$/i.exec(address)
  if (dotted) return dotted[1]
  const hex = /^::ffff:([0-9a-f]{1,4}):([0-9a-f]{1,4})$/i.exec(address)
  if (!hex) return null
  const high = parseInt(hex[1], 16)
  const low = parseInt(hex[2], 16)
  return [high >> 8, high & 255, low >> 8, low & 255].join('.')
}

export function isPublicAddress(address: string): boolean {
  const family = isIP(address)
  if (family === 0) return false
  if (family === 6) {
    const v4 = mappedIpv4(address)
    if (v4) return isPublicAddress(v4)
  }
  return !nonPublic.check(address, family === 4 ? 'ipv4' : 'ipv6')
}

export interface LinkPreviewResponse {
  url: string
  contentType: string
  body: Buffer
}

export interface LinkPreviewRequest {
  accept: string
  maxBytes: number
  stopAt?: string
  timeoutMs: number
}

const USER_AGENT = 'Mozilla/5.0 (compatible; DotCraftLinkPreview/1.0)'
const MAX_REDIRECTS = 3

export function readLinkPreviewBody(
  source: Pick<NodeJS.EventEmitter, 'on'>,
  options: Pick<LinkPreviewRequest, 'maxBytes' | 'stopAt'>
): Promise<Buffer> {
  return new Promise<Buffer>((resolve, reject) => {
    const chunks: Buffer[] = []
    let size = 0
    let scanned = 0
    let settled = false
    const settle = (error: Error | null): void => {
      if (settled) return
      settled = true
      if (error) reject(error)
      else resolve(Buffer.concat(chunks, size))
    }
    source.on('data', (chunk: Buffer) => {
      if (settled) return
      const room = options.maxBytes - size
      chunks.push(chunk.length > room ? chunk.subarray(0, room) : chunk)
      size += Math.min(chunk.length, room)
      if (options.stopAt) {
        const from = Math.max(0, scanned - options.stopAt.length)
        const tail = Buffer.concat(chunks, size).subarray(from).toString('latin1').toLowerCase()
        scanned = size
        if (tail.includes(options.stopAt)) return settle(null)
      }
      if (size >= options.maxBytes) settle(options.stopAt ? null : new Error('Response too large'))
    })
    source.on('end', () => settle(null))
    source.on('error', (error) => settle(error))
    source.on('aborted', () => settle(new Error('Connection closed')))
    source.on('close', () => settle(new Error('Connection closed')))
  })
}

type Hop = { redirect: string } | LinkPreviewResponse

function requestOnce(url: URL, options: LinkPreviewRequest, deadline: number): Promise<Hop> {
  const host = bareHostname(url.hostname)
  if (isIP(host) && !isPublicAddress(host)) {
    return Promise.reject(new LinkPreviewBlockedError(`${host} is not a public address`))
  }
  let req: ClientRequest | undefined
  let timer: ReturnType<typeof setTimeout> | undefined
  return new Promise<Hop>((resolve, reject) => {
    timer = setTimeout(() => reject(new Error('Timed out')), Math.max(1, deadline - Date.now()))
    dnsLookup(host, { all: true }).then((addresses) => {
      if (Date.now() >= deadline) throw new Error('Timed out')
      if (addresses.length === 0 || addresses.some((entry) => !isPublicAddress(entry.address))) {
        throw new LinkPreviewBlockedError(`${host} does not resolve to a public address`)
      }
      req = net.request({
        url: url.href,
        method: 'GET',
        credentials: 'omit',
        referrerPolicy: 'no-referrer',
        redirect: 'manual'
      })
      req.setHeader('User-Agent', USER_AGENT)
      req.setHeader('Accept', options.accept)
      req.on('redirect', (_status, _method, redirectUrl) => resolve({ redirect: redirectUrl }))
      req.on('response', (response) => {
        const status = response.statusCode
        if (status < 200 || status >= 300) {
          reject(new Error(`HTTP ${status}`))
          return
        }
        readLinkPreviewBody(response, options).then(
          (body) => resolve({ url: url.href, contentType: String(response.headers['content-type'] ?? ''), body }),
          reject
        )
      })
      req.on('error', reject)
      req.end()
    }).catch(reject)
  }).finally(() => {
    clearTimeout(timer)
    req?.abort()
  })
}

export async function fetchLinkPreviewResource(rawUrl: string, options: LinkPreviewRequest): Promise<LinkPreviewResponse> {
  const deadline = Date.now() + options.timeoutMs
  let url = new URL(rawUrl)
  for (let hop = 0; ; hop++) {
    if (url.protocol !== 'https:' || url.username || url.password) {
      throw new Error(`Refusing ${url.protocol} URL`)
    }
    const result = await requestOnce(url, options, deadline)
    if (!('redirect' in result)) return result
    if (hop >= MAX_REDIRECTS) throw new Error('Too many redirects')
    url = new URL(result.redirect, url)
  }
}
