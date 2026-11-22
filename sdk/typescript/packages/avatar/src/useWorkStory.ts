import { useEffect, useRef, type RefObject } from 'react'
import { CONDUCT_STORY_CHANNELS, CONDUCT_STORY_DURATION, conductStoryKeyframes, type ConductStoryChannel } from './conductStory.js'
import { WORK_STORY_CHANNELS, WORK_STORY_DURATION, workStoryKeyframes, type WorkStoryChannel } from './workStory.js'

const EXIT_MS = 160

export type WorkStoryKind = 'laptop' | 'conduct'

interface Story {
  duration: number
  delay: number
  tracks: { selector: string; property: string; frames: Keyframe[] }[]
}

const STORIES: Record<WorkStoryKind, Story> = {
  laptop: {
    duration: WORK_STORY_DURATION,
    // The laptop reveals after 1200 ms and its arm spring settles 320 ms later; the CSS loop plays until then.
    delay: 1520,
    tracks: (Object.keys(WORK_STORY_CHANNELS) as WorkStoryChannel[])
      .map(channel => ({ ...WORK_STORY_CHANNELS[channel], frames: workStoryKeyframes(channel) })),
  },
  conduct: {
    duration: CONDUCT_STORY_DURATION,
    delay: 0,
    tracks: (Object.keys(CONDUCT_STORY_CHANNELS) as ConductStoryChannel[])
      .map(channel => ({ ...CONDUCT_STORY_CHANNELS[channel], frames: conductStoryKeyframes(channel) })),
  },
}

export function useWorkStory(ref: RefObject<Element | null>, active: boolean, paused: boolean, kind: WorkStoryKind = 'laptop') {
  const running = useRef<Animation[]>([])
  useEffect(() => {
    const root = ref.current
    if (!active || !root || typeof root.animate !== 'function') return
    const story = STORIES[kind]
    const created: { animation: Animation; element: Element; property: string }[] = []
    for (const { selector, property, frames } of story.tracks) {
      root.querySelectorAll(selector).forEach(element => {
        const animation = element.animate(frames, { id: 'dca-work-story', duration: story.duration, delay: story.delay, iterations: Infinity })
        created.push({ animation, element, property })
      })
    }
    running.current = created.map(({ animation }) => animation)
    return () => {
      running.current = []
      for (const { animation, element, property } of created) {
        const started = Number(animation.currentTime ?? 0) > story.delay
        const from = started && element.isConnected ? getComputedStyle(element).getPropertyValue(property) : ''
        animation.cancel()
        if (from) element.animate([{ [property]: from, offset: 0 }], { duration: EXIT_MS, easing: 'ease-out' })
      }
    }
  }, [ref, active, kind])
  useEffect(() => {
    for (const animation of running.current) {
      if (paused && animation.playState === 'running') animation.pause()
      if (!paused && animation.playState === 'paused') animation.play()
    }
  }, [paused, active, kind])
}
