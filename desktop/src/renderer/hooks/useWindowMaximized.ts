import { useEffect, useState } from 'react'

export function useWindowMaximized(): boolean {
  const [maximized, setMaximized] = useState(false)

  useEffect(() => {
    const windowApi = window.api?.window
    if (!windowApi) return

    let disposed = false
    void windowApi.isMaximized()
      .then((value) => {
        if (!disposed) setMaximized(value)
      })
      .catch(() => {
        // Keep the default windowed state if the native query is unavailable during startup.
      })

    const unsubscribe = windowApi.onMaximizedChange((value) => {
      setMaximized(value)
    })

    return () => {
      disposed = true
      unsubscribe()
    }
  }, [])

  return maximized
}
