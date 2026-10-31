/**
 * The main process mirrors this logic in viewerFileProtocol.ts. URL format:
 * `dotcraft-viewer://workspace/absolute/path/with/encoded/segments`.
 */
export const VIEWER_SCHEME = 'dotcraft-viewer'
const VIEWER_HOST = 'workspace'

/** Windows paths are normalized to forward slashes before encoding. */
export function buildViewerUrlRenderer(absolutePath: string): string {
  const normalized = absolutePath.replace(/\\/g, '/')
  const withLeadingSlash = normalized.startsWith('/') ? normalized : `/${normalized}`
  const encodedPath = withLeadingSlash
    .split('/')
    .map((segment) => encodeURIComponent(segment))
    .join('/')
  return `${VIEWER_SCHEME}://${VIEWER_HOST}${encodedPath}`
}

/** The query only defeats caching after a refresh; the main process ignores it. */
export function buildVersionedViewerUrl(absolutePath: string, version: number): string {
  const url = buildViewerUrlRenderer(absolutePath)
  return version > 0 ? `${url}?v=${version}` : url
}
