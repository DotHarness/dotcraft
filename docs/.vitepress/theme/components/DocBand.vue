<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { withBase } from 'vitepress'
import { useBreadcrumb, useDocStrings, useEditUrl } from './docPage'

const crumbs = useBreadcrumb()
const editUrl = useEditUrl()
const strings = useDocStrings()
const copied = ref(false)
let resetTimer = 0

async function copyLink(): Promise<void> {
  try {
    await navigator.clipboard.writeText(location.origin + location.pathname)
  } catch {
    return
  }
  copied.value = true
  window.clearTimeout(resetTimer)
  resetTimer = window.setTimeout(() => {
    copied.value = false
  }, 1600)
}

onBeforeUnmount(() => window.clearTimeout(resetTimer))
</script>

<template>
  <div class="dc-band">
    <nav class="dc-crumbs" :aria-label="strings.breadcrumb">
      <template v-for="(crumb, index) in crumbs" :key="index">
        <svg v-if="index" class="dc-crumbs__sep" viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6" /></svg>
        <span v-if="index === crumbs.length - 1" class="dc-crumbs__item" aria-current="page">{{ crumb.text }}</span>
        <a v-else-if="crumb.link" class="dc-crumbs__item" :href="withBase(crumb.link)">{{ crumb.text }}</a>
        <span v-else class="dc-crumbs__item">{{ crumb.text }}</span>
      </template>
    </nav>
    <div class="dc-band__actions">
      <button type="button" class="dc-icon-button" :data-copied="copied || undefined" :aria-label="strings.copyLink" @click="copyLink">
        <svg v-if="copied" class="dc-icon-button__done" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5" /></svg>
        <svg v-else viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" /><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" /></svg>
        <span class="dc-tip" aria-live="polite">{{ copied ? strings.linkCopied : strings.copyLink }}</span>
      </button>
      <a v-if="editUrl" class="dc-icon-button" :href="editUrl" target="_blank" rel="noopener" :aria-label="strings.edit">
        <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" /><path d="M18.375 2.625a1 1 0 0 1 3 3l-9.013 9.014a2 2 0 0 1-.853.505l-2.873.84a.5.5 0 0 1-.62-.62l.84-2.873a2 2 0 0 1 .506-.852z" /></svg>
        <span class="dc-tip">{{ strings.edit }}</span>
      </a>
    </div>
  </div>
</template>
