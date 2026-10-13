export type DemoStart = 'review-changes' | 'several-projects' | 'token-activity'

const STARTS: readonly DemoStart[] = ['review-changes', 'several-projects', 'token-activity']
const requested = new URLSearchParams(window.location.search).get('start') as DemoStart | null

export const demoStart: DemoStart | null = requested && STARTS.includes(requested) ? requested : null
