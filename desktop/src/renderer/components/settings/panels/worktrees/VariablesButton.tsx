import { useCallback, useEffect, useId, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { LayerBoundary } from '../../../../contexts/LayerContext'
import { useT } from '../../../../contexts/LocaleContext'
import { Button } from '../../../ui/Button'
import { CopyButton } from '../../../ui/CopyButton'
import { useDismissOnViewportChange } from '../../../ui/anchoredPopup'
import { ENVIRONMENT_VARIABLES } from './environmentPresentation'
import styles from './environments.module.css'

const POPOVER_WIDTH = 320

export function VariablesButton(): JSX.Element {
  const t = useT()
  const popoverId = useId()
  const triggerRef = useRef<HTMLButtonElement>(null)
  const popoverRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<{ left: number; top: number } | null>(null)
  const close = useCallback(() => setPosition(null), [])
  useDismissOnViewportChange(popoverRef, position ? close : null)

  useEffect(() => {
    if (!position) return
    const onPointerDown = (event: PointerEvent): void => {
      const target = event.target as Node
      if (popoverRef.current?.contains(target) || triggerRef.current?.contains(target)) return
      close()
    }
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      close()
      triggerRef.current?.focus()
    }
    document.addEventListener('pointerdown', onPointerDown, true)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown, true)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [close, position])

  function toggle(): void {
    if (position) {
      close()
      return
    }
    const rect = triggerRef.current?.getBoundingClientRect()
    if (!rect) return
    const left = Math.max(8, Math.min(rect.right - POPOVER_WIDTH, window.innerWidth - POPOVER_WIDTH - 8))
    setPosition({ left, top: rect.bottom + 6 })
  }

  return (
    <>
      <Button
        ref={triggerRef}
        size="sm"
        variant="secondary"
        aria-expanded={position != null}
        aria-controls={position ? popoverId : undefined}
        onClick={toggle}
      >
        {t('settings.environments.variables')}
      </Button>
      {position && createPortal(
        <LayerBoundary>
          <div
            id={popoverId}
            ref={popoverRef}
            role="dialog"
            aria-label={t('settings.environments.variables')}
            className={styles.popover}
            style={{ left: position.left, top: position.top }}
          >
            {ENVIRONMENT_VARIABLES.map((variable) => (
              <div key={variable.name} className={styles.variable}>
                <span className={styles.variableText}>
                  <span className={styles.variableName}>{variable.name}</span>
                  <span className={styles.variableDescription}>{t(variable.descriptionKey)}</span>
                </span>
                <CopyButton
                  getText={() => variable.name}
                  label={t('settings.environments.variables.copy')}
                  copiedLabel={t('settings.environments.variables.copied')}
                />
              </div>
            ))}
          </div>
        </LayerBoundary>,
        document.body
      )}
    </>
  )
}
