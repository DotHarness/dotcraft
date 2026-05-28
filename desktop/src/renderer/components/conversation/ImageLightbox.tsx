import { useCallback, useEffect } from 'react'

interface ImageLightboxProps {
  src: string
  alt?: string
  onClose: () => void
}

/**
 * Fullscreen image viewer: dark backdrop, centered image, Esc or backdrop to close.
 */
export function ImageLightbox({ src, alt = '', onClose }: ImageLightboxProps): JSX.Element {
  const onKeyDown = useCallback(
    (e: KeyboardEvent): void => {
      if (e.key === 'Escape') onClose()
    },
    [onClose]
  )

  useEffect(() => {
    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [onKeyDown])

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Image preview"
      onClick={onClose}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        background: 'rgba(0,0,0,0.85)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '24px',
        cursor: 'zoom-out'
      }}
    >
      <img
        src={src}
        alt={alt}
        onClick={(e) => {
          e.stopPropagation()
        }}
        style={{
          maxWidth: '100%',
          maxHeight: '100%',
          objectFit: 'contain',
          borderRadius: '8px',
          cursor: 'default'
        }}
      />
    </div>
  )
}
