import { useEffect, useState } from 'react'

/** Bytes read before the viewer shows the truncation notice. */
export const MAX_READ_BYTES = 5 * 1024 * 1024

export interface FileTextState {
  status: 'loading' | 'ok' | 'error'
  text: string
  truncated: boolean
  /** The path this state describes; a stale result for a previous path is discarded. */
  absolutePath: string
  error?: string
}

export function useFileText(absolutePath: string): FileTextState {
  const [state, setState] = useState<FileTextState>(() => ({
    status: 'loading',
    text: '',
    truncated: false,
    absolutePath
  }))

  useEffect(() => {
    let cancelled = false
    setState({ status: 'loading', text: '', truncated: false, absolutePath })

    window.api.workspace.viewer.readText({ absolutePath, limitBytes: MAX_READ_BYTES })
      .then((result) => {
        if (cancelled) return
        setState({ status: 'ok', text: result.text, truncated: result.truncated, absolutePath })
      })
      .catch((error: unknown) => {
        if (cancelled) return
        setState({
          status: 'error',
          text: '',
          truncated: false,
          absolutePath,
          error: error instanceof Error ? error.message : String(error)
        })
      })

    return () => { cancelled = true }
  }, [absolutePath])

  return state
}
