/**
 * Renderer-side utility for building `dotcraft-viewer://` URLs.
 *
 * The main process mirrors the same logic in viewerFileProtocol.ts.
 * URL format: `dotcraft-viewer://workspace/absolute/path/with/encoded/segments`
 */
export const VIEWER_SCHEME = 'dotcraft-viewer'
const VIEWER_HOST = 'workspace'

/**
 * Builds a `dotcraft-viewer://` URL from an absolute file path.
 * Uses forward slashes for cross-platform consistency (Windows paths are
 * normalized before encoding).
 */
export function buildViewerUrlRenderer(absolutePath: string): string {
  const normalized = absolutePath.replace(/\\/g, '/')
  const withLeadingSlash = normalized.startsWith('/') ? normalized : `/${normalized}`
  const encodedPath = withLeadingSlash
    .split('/')
    .map((segment) => encodeURIComponent(segment))
    .join('/')
  return `${VIEWER_SCHEME}://${VIEWER_HOST}${encodedPath}`
}
