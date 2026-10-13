/** A fixed-seed generator keeps the token heatmap identical across loads. */
import { addLocalDays, localDayKey } from '@renderer/utils/localDay'

const HISTORY_DAYS = 371

function seeded(seed: number): () => number {
  let state = seed
  return () => {
    state = (state * 1664525 + 1013904223) % 4294967296
    return state / 4294967296
  }
}

export function demoUsageHistory(): { unit: string; groupBy: string; days: Array<{ date: string; total: number }> } {
  const random = seeded(20261013)
  const today = new Date()
  const days: Array<{ date: string; total: number }> = []
  for (let offset = HISTORY_DAYS; offset >= 0; offset--) {
    const date = addLocalDays(today, -offset)
    const weekend = date.getDay() === 0 || date.getDay() === 6
    // Usage ramps up over the year; the last two weeks form an unbroken streak.
    const ramp = 0.35 + 0.65 * (1 - offset / HISTORY_DAYS)
    const idle = offset > 14 && random() < (weekend ? 0.6 : 0.18)
    const roll = random()
    if (idle) continue
    const base = weekend ? 60_000 : 260_000
    const burst = roll > 0.93 ? 4 : roll > 0.75 ? 2 : 1
    days.push({ date: localDayKey(date), total: Math.round(base * ramp * burst * (0.4 + random())) })
  }
  return { unit: 'tokens', groupBy: 'day', days }
}

export function demoProfileInsights(model: string): Record<string, unknown> {
  return {
    topModel: { key: model, count: 412, total: 538 },
    topReasoning: { key: 'medium', count: 301, total: 538 },
    skillsExplored: 14,
    totalSkillsUsed: 186,
    totalThreads: 538,
    longestTaskMs: 47 * 60_000 + 12_000,
    skills: [
      { name: 'code-review', count: 64 },
      { name: 'release-notes', count: 41 },
      { name: 'unity-profiler', count: 33 },
      { name: 'db-migration', count: 27 },
      { name: 'changelog-digest', count: 21 }
    ]
  }
}
