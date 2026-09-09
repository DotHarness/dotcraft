import { useEffect, useRef, type RefObject } from 'react'
import type { PetRect, PetSnapshot } from '../../../shared/desktopPet'
import { inPetDetachZone, petActivity } from '../../../shared/desktopPet'
import { findPetEditor } from './editorBridge'
import { useDesktopPluginRegistry } from '../../plugins/desktopPluginRegistry'
import { normalizeLocale } from '../../../shared/locales'

function seatOf(root: HTMLElement): PetRect | null {
  const mascot = root.querySelector<HTMLElement>('.composer-mascot-jelly')
  if (!mascot) return null
  const rect = mascot.getBoundingClientRect()
  return { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
}

export function useDesktopPet(root: RefObject<HTMLDivElement | null>, enabled: boolean, canChat: boolean): void {
  const latest = useRef(canChat)
  latest.current = canChat
  useEffect(() => {
    const element = root.current
    const api = window.api?.desktopPet
    if (!enabled || !element || !api) return
    let owns = false
    let detached = false
    let sourcePointer: number | null = null
    let drag: { id: number; x: number; y: number; seat: PetRect; mascot: HTMLElement; distance: number } | null = null
    let savedFocus: HTMLElement | null = null
    let timer: ReturnType<typeof setInterval> | undefined
    let editRevision = 0
    let lastSnapshot = ''
    let suppressClick = false
    let tether: SVGSVGElement | null = null
    const releaseSourcePointer = (): void => {
      const pointer = sourcePointer
      sourcePointer = null
      if (pointer !== null && element.hasPointerCapture(pointer)) element.releasePointerCapture(pointer)
    }
    const snapshot = (): PetSnapshot => ({
      activity: petActivity(element.querySelector<HTMLElement>('[data-composer-avatar-pose]')?.dataset.composerAvatarPose),
      name: element.querySelector<HTMLElement>('[data-mascot-name]')?.dataset.mascotName ?? '',
      text: findPetEditor(element)?.getText() ?? '',
      editRevision,
      theme: document.documentElement.dataset.theme === 'light' ? 'light' : 'dark',
      locale: normalizeLocale(document.documentElement.lang),
      reducedMotion: document.documentElement.dataset.reduceMotion === 'on' ||
        (document.documentElement.dataset.reduceMotion !== 'off' && matchMedia('(prefers-reduced-motion: reduce)').matches),
      canChat: latest.current && !!findPetEditor(element)?.enabled
    })
    const command = (value: Parameters<typeof api.command>[0]): void => {
      void api.command(value).catch(() => {
        owns = false
        element.removeAttribute('data-pet-owner')
        detached = false
        releaseSourcePointer()
        clearInterval(timer)
        document.documentElement.removeAttribute('data-desktop-pet-detached')
        reset()
      })
    }
    const reset = (): void => {
      if (drag) { drag.mascot.style.removeProperty('translate'); drag.mascot.style.removeProperty('transition') }
      drag = null
      tether?.remove()
      tether = null
      element.removeAttribute('data-pet-drag')
    }
    const down = (event: PointerEvent): void => {
      if (event.button !== 0 || detached || owns) return
      if (useDesktopPluginRegistry.getState().surfaces.some(surface => surface.surface === 'composer.mascot' && surface.kind === 'replace')) return
      const target = (event.target as Element).closest<HTMLElement>('.composer-mascot-jelly')
      const seat = seatOf(element)
      if (!target || !seat) return
      savedFocus = document.activeElement as HTMLElement
      drag = { id: event.pointerId, x: event.clientX, y: event.clientY, seat, mascot: target, distance: 0 }
      element.setPointerCapture(event.pointerId)
    }
    const detach = (event: PointerEvent, pointerHeld: boolean): void => {
      if (!drag || owns) return
      owns = true
      sourcePointer = pointerHeld ? event.pointerId : null
      element.setAttribute('data-pet-owner', '')
      command({ type: 'detach', seat: drag.seat, pointerHeld,
        point: { x: drag.seat.x + drag.seat.width / 2 + event.clientX - drag.x,
          y: drag.seat.y + drag.seat.height / 2 + event.clientY - drag.y }, snapshot: snapshot() })
      timer = setInterval(() => {
        const current = snapshot()
        const serialized = JSON.stringify(current)
        if (owns && serialized !== lastSnapshot) { lastSnapshot = serialized; command({ type: 'snapshot', snapshot: current }) }
      }, 250)
    }
    const move = (event: PointerEvent): void => {
      if (owns) {
        if (sourcePointer === event.pointerId) command({ type: 'source-drag', stage: 'move' })
        return
      }
      if (!drag || drag.id !== event.pointerId) return
      const x = event.clientX - drag.x
      const y = event.clientY - drag.y
      drag.distance = Math.hypot(x, y)
      if (drag.distance < 5) return
      suppressClick = true
      event.preventDefault()
      element.dataset.petDrag = drag.distance >= 112 ? 'armed' : 'dragging'
      if (!tether) {
        tether = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
        tether.classList.add('desktop-pet-tether')
        tether.setAttribute('aria-hidden', 'true')
        tether.appendChild(document.createElementNS('http://www.w3.org/2000/svg', 'line'))
        document.body.appendChild(tether)
      }
      const line = tether.firstElementChild!
      const center = { x: drag.seat.x + drag.seat.width / 2, y: drag.seat.y + drag.seat.height / 2 }
      line.setAttribute('x1', String(center.x)); line.setAttribute('y1', String(center.y))
      line.setAttribute('x2', String(center.x + x)); line.setAttribute('y2', String(center.y + y))
      tether.style.visibility = drag.distance >= 112 ? 'hidden' : 'visible'
      // The composer character's parent is scaled to 0.75.
      drag.mascot.style.translate = `${x / 0.75}px ${y / 0.75}px`
      if (drag.distance >= 24 && inPetDetachZone({ x: center.x + x, y: center.y + y },
        { width: window.innerWidth, height: window.innerHeight })) detach(event, true)
    }
    const end = (event: PointerEvent): void => {
      if (sourcePointer === event.pointerId) {
        sourcePointer = null
        command({ type: 'source-drag', stage: 'end' })
        reset()
        if (element.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId)
        return
      }
      if (owns) return
      if (!drag || drag.id !== event.pointerId) return
      if (drag.distance >= 112 && event.type === 'pointerup') {
        detach(event, false)
      } else {
        const mascot = drag.mascot
        if (!snapshot().reducedMotion) {
          mascot.style.transition = 'translate 220ms cubic-bezier(0.2, 0.8, 0.2, 1)'
          mascot.style.translate = '0px 0px'
          setTimeout(() => { mascot.style.removeProperty('translate'); mascot.style.removeProperty('transition') }, 240)
          drag = null
          tether?.remove()
          tether = null
          element.removeAttribute('data-pet-drag')
        } else reset()
      }
      if (element.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId)
    }
    const key = (event: KeyboardEvent): void => {
      if (event.key === 'Escape' && (drag || sourcePointer !== null)) { event.preventDefault(); if (owns) command({ type: 'return' }); else reset() }
    }
    const click = (event: MouseEvent): void => {
      if (owns || suppressClick) { event.preventDefault(); event.stopPropagation() }
      suppressClick = false
    }
    const unsubscribe = api.onEvent(event => {
      if (!owns) return
      if (event.type === 'ownership') {
        detached = event.detached
        document.documentElement.toggleAttribute('data-desktop-pet-detached', detached)
        if (detached) command({ type: 'hidden' })
        reset()
        if (!detached) { owns = false; releaseSourcePointer(); element.removeAttribute('data-pet-owner'); clearInterval(timer); savedFocus?.focus() }
      } else if (event.type === 'return-seat') {
        reset()
        command({ type: 'seat', seat: seatOf(element) })
      } else if (event.type === 'edit') {
        editRevision = event.revision
        const editor = findPetEditor(element)
        if (!latest.current || !editor?.enabled) { command({ type: 'return' }); return }
        if (editor.getText() !== event.text) editor.setText(event.text)
        if (event.submit) {
          // Let the source composer commit its draft-dependent state before submission.
          setTimeout(() => { if (owns) findPetEditor(element)?.submit() }, 0)
        }
      }
    })
    element.addEventListener('pointerdown', down, true)
    element.addEventListener('pointermove', move)
    element.addEventListener('pointerup', end)
    element.addEventListener('pointercancel', end)
    element.addEventListener('lostpointercapture', end)
    element.addEventListener('click', click, true)
    window.addEventListener('keydown', key, true)
    return () => {
      if (owns) command({ type: 'return' })
      element.removeAttribute('data-pet-owner')
      clearInterval(timer)
      unsubscribe()
      reset()
      if (!owns) document.documentElement.removeAttribute('data-desktop-pet-detached')
      element.removeEventListener('pointerdown', down, true)
      element.removeEventListener('pointermove', move)
      element.removeEventListener('pointerup', end)
      element.removeEventListener('pointercancel', end)
      element.removeEventListener('lostpointercapture', end)
      element.removeEventListener('click', click, true)
      window.removeEventListener('keydown', key, true)
    }
  }, [root, enabled])
}
