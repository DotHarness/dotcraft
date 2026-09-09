import { useEffect, useRef, useState } from 'react'

export function usePetReaction(reduced: boolean) {
  const [state, setState] = useState<'idle' | 'greeting' | 'acknowledge'>('idle')
  const [sequence, setSequence] = useState(0)
  const [tilt, setTilt] = useState(0)
  const [dragging, setDragging] = useState(false)
  const [direction, setDirection] = useState<'look-left' | 'look-right'>('look-right')
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  useEffect(() => () => clearTimeout(timer.current), [])
  const react = (next: 'greeting' | 'acknowledge', duration: number): void => {
    clearTimeout(timer.current)
    setState(next)
    setSequence(value => value + 1)
    timer.current = setTimeout(() => setState('idle'), duration)
  }
  return {
    state, sequence, tilt: reduced ? 0 : tilt, dragging, direction,
    greet: () => react('greeting', 1400),
    move: (delta: number) => {
      clearTimeout(timer.current)
      setState('idle')
      setDragging(true)
      setTilt(Math.max(-9, Math.min(9, delta * 0.7)))
      if (Math.abs(delta) > 1) setDirection(delta < 0 ? 'look-left' : 'look-right')
    },
    land: () => { setDragging(false); setTilt(0); react('acknowledge', 350) }
  }
}
