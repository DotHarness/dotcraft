import { useEffect, useRef, type RefObject } from 'react'
import type { PetRect } from '../../../shared/desktopPet'
import { inPetDetachZone } from '../../../shared/desktopPet'
import { useDesktopPluginRegistry } from '../../plugins/desktopPluginRegistry'
import {
  adoptPetSource, petSourceCommand, petSourceDetached, petSourceOwner, petSourceSeat, prefersReducedMotion,
  releasePetSource, startPetSource, type PetSourceBinding, type PetSourceSurface
} from './desktopPetSource'
import { usePetActivity } from './usePetActivity'

export interface DesktopPetSourceOptions {
  threadId: string | null
  mascotName: string
}

export function useDesktopPet(
  root: RefObject<HTMLDivElement | null>,
  enabled: boolean,
  surface: PetSourceSurface,
  options: DesktopPetSourceOptions
): void {
  const latest = useRef(surface)
  latest.current = surface
  const name = useRef(options.mascotName)
  name.current = options.mascotName
  const threadId = useRef(options.threadId)
  threadId.current = options.threadId
  const activity = usePetActivity(options.threadId)
  useEffect(() => {
    const element = root.current
    if (!enabled || !element || !window.api?.desktopPet) return
    let sourcePointer: number | null = null
    let drag: { id: number; x: number; y: number; seat: PetRect; mascot: HTMLElement; stage: HTMLElement | null; distance: number } | null = null
    let savedFocus: HTMLElement | null = null
    let suppressClick = false
    let tether: SVGSVGElement | null = null
    const releaseSourcePointer = (): void => {
      const pointer = sourcePointer
      sourcePointer = null
      if (pointer !== null && element.hasPointerCapture(pointer)) element.releasePointerCapture(pointer)
    }
    // The columns around the composer clip overflow, so the held character rides on the viewport instead.
    const lift = (stage: HTMLElement): void => {
      const base = stage.offsetParent?.getBoundingClientRect()
      stage.style.width = `${stage.offsetWidth}px`
      stage.style.height = `${stage.offsetHeight}px`
      stage.style.left = `${(base?.left ?? 0) + stage.offsetLeft}px`
      stage.style.top = `${(base?.top ?? 0) + stage.offsetTop}px`
      stage.style.position = 'fixed'
    }
    const settle = (mascot: HTMLElement, stage: HTMLElement | null): void => {
      mascot.style.removeProperty('translate')
      mascot.style.removeProperty('transition')
      for (const property of ['position', 'left', 'top', 'width', 'height']) stage?.style.removeProperty(property)
    }
    const reset = (): void => {
      if (drag) settle(drag.mascot, drag.stage)
      drag = null
      tether?.remove()
      tether = null
      element.removeAttribute('data-pet-drag')
    }
    const binding: PetSourceBinding = {
      root: element,
      surface: () => latest.current,
      name: () => name.current,
      threadId: () => threadId.current,
      activity,
      reset,
      release: restoreFocus => { releaseSourcePointer(); if (restoreFocus) savedFocus?.focus() }
    }
    const owns = (): boolean => petSourceOwner() === binding
    adoptPetSource(binding)
    const down = (event: PointerEvent): void => {
      if (event.button !== 0 || petSourceDetached() || owns()) return
      if (useDesktopPluginRegistry.getState().surfaces.some(surface => surface.surface === 'composer.mascot' && surface.kind === 'replace')) return
      const target = (event.target as Element).closest<HTMLElement>('.composer-mascot-jelly')
      const seat = petSourceSeat(element)
      if (!target || !seat) return
      savedFocus = document.activeElement as HTMLElement
      drag = {
        id: event.pointerId, x: event.clientX, y: event.clientY, seat, mascot: target, distance: 0,
        stage: target.closest<HTMLElement>('.composer-mascot-stage')
      }
      element.setPointerCapture(event.pointerId)
    }
    const detach = (event: PointerEvent, pointerHeld: boolean): void => {
      if (!drag || owns()) return
      sourcePointer = pointerHeld ? event.pointerId : null
      startPetSource(binding, { seat: drag.seat, pointerHeld,
        point: { x: drag.seat.x + drag.seat.width / 2 + event.clientX - drag.x,
          y: drag.seat.y + drag.seat.height / 2 + event.clientY - drag.y } })
    }
    const move = (event: PointerEvent): void => {
      if (owns()) {
        if (sourcePointer === event.pointerId) petSourceCommand({ type: 'source-drag', stage: 'move' })
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
        if (drag.stage) lift(drag.stage)
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
        petSourceCommand({ type: 'source-drag', stage: 'end' })
        reset()
        if (element.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId)
        return
      }
      if (owns()) return
      if (!drag || drag.id !== event.pointerId) return
      if (drag.distance >= 112 && event.type === 'pointerup') {
        detach(event, false)
      } else {
        const { mascot, stage } = drag
        if (!prefersReducedMotion()) {
          mascot.style.transition = 'translate 220ms cubic-bezier(0.2, 0.8, 0.2, 1)'
          mascot.style.translate = '0px 0px'
          setTimeout(() => {
            settle(mascot, stage)
            if (!drag) element.removeAttribute('data-pet-drag')
          }, 240)
          drag = null
          tether?.remove()
          tether = null
        } else reset()
      }
      if (element.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId)
    }
    const key = (event: KeyboardEvent): void => {
      if (event.key === 'Escape' && (drag || sourcePointer !== null)) { event.preventDefault(); if (owns()) petSourceCommand({ type: 'return' }); else reset() }
    }
    const click = (event: MouseEvent): void => {
      if (owns() || suppressClick) { event.preventDefault(); event.stopPropagation() }
      suppressClick = false
    }
    element.addEventListener('pointerdown', down, true)
    element.addEventListener('pointermove', move)
    element.addEventListener('pointerup', end)
    element.addEventListener('pointercancel', end)
    element.addEventListener('lostpointercapture', end)
    element.addEventListener('click', click, true)
    window.addEventListener('keydown', key, true)
    return () => {
      if (sourcePointer !== null && owns()) petSourceCommand({ type: 'source-drag', stage: 'end' })
      releasePetSource(binding)
      reset()
      element.removeEventListener('pointerdown', down, true)
      element.removeEventListener('pointermove', move)
      element.removeEventListener('pointerup', end)
      element.removeEventListener('pointercancel', end)
      element.removeEventListener('lostpointercapture', end)
      element.removeEventListener('click', click, true)
      window.removeEventListener('keydown', key, true)
    }
  }, [root, enabled, activity])
}
