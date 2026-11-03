import { useEffect, useRef, type RefObject } from 'react'
import { WORK_STORY_CHANNELS, WORK_STORY_DURATION, workStoryKeyframes, type WorkStoryChannel } from './workStory.js'

// The laptop reveals after 1200 ms and its arm spring settles 320 ms later; the CSS loop plays until then.
const START_DELAY_MS = 1520
const EXIT_MS = 160
const FRAMES = Object.fromEntries((Object.keys(WORK_STORY_CHANNELS) as WorkStoryChannel[]).map(channel => [channel, workStoryKeyframes(channel)])) as Record<WorkStoryChannel, Keyframe[]>

export function useWorkStory(ref: RefObject<Element | null>, active: boolean, paused: boolean) {
  const running = useRef<Animation[]>([])
  useEffect(() => {
    const root = ref.current
    if (!active || !root || typeof root.animate !== 'function') return
    const created: { animation: Animation; element: Element; property: string }[] = []
    for (const channel of Object.keys(WORK_STORY_CHANNELS) as WorkStoryChannel[]) {
      const { selector, property } = WORK_STORY_CHANNELS[channel]
      root.querySelectorAll(selector).forEach(element => {
        const animation = element.animate(FRAMES[channel], { id: 'dca-work-story', duration: WORK_STORY_DURATION, delay: START_DELAY_MS, iterations: Infinity })
        created.push({ animation, element, property })
      })
    }
    running.current = created.map(({ animation }) => animation)
    return () => {
      running.current = []
      for (const { animation, element, property } of created) {
        const started = Number(animation.currentTime ?? 0) > START_DELAY_MS
        const from = started && element.isConnected ? getComputedStyle(element).getPropertyValue(property) : ''
        animation.cancel()
        if (from) element.animate([{ [property]: from }], { duration: EXIT_MS, easing: 'ease-out' })
      }
    }
  }, [ref, active])
  useEffect(() => {
    for (const animation of running.current) {
      if (paused && animation.playState === 'running') animation.pause()
      if (!paused && animation.playState === 'paused') animation.play()
    }
  }, [paused, active])
}
