import { inject, provide, type InjectionKey, type Ref } from 'vue'
import { withBase } from 'vitepress'
import type { HomeCopy, Locale } from './copy'

export type InstallOs = 'windows' | 'unix'

export interface HomeContext {
  locale: Locale
  t: HomeCopy
  os: Ref<InstallOs>
  href: (path: string) => string
}

const key: InjectionKey<HomeContext> = Symbol('dc-home')

export function provideHome(context: HomeContext): void {
  provide(key, context)
}

export function useHome(): HomeContext {
  const context = inject(key)
  if (!context) throw new Error('Home sections render inside HomePage')
  return context
}

export function localHref(locale: Locale, path: string): string {
  return withBase(`${locale === 'zh' ? '/zh' : ''}${path}`)
}

export function reducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}
