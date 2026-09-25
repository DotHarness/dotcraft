<script setup lang="ts">
import type { InstallOs } from './home'

const props = defineProps<{ id: string; label: string; controls: (os: InstallOs) => string }>()
const os = defineModel<InstallOs>({ required: true })
const tabs: { os: InstallOs; text: string }[] = [
  { os: 'windows', text: 'PowerShell' },
  { os: 'unix', text: 'macOS / Linux' }
]

function onKey(event: KeyboardEvent): void {
  if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return
  event.preventDefault()
  os.value = os.value === 'windows' ? 'unix' : 'windows'
  const tablist = (event.currentTarget as HTMLElement).parentElement
  tablist?.querySelector<HTMLElement>(`[data-os="${os.value}"]`)?.focus()
}
</script>

<template>
  <div class="dc-seg" role="tablist" :aria-label="label">
    <button
      v-for="tab in tabs"
      :id="`${props.id}-${tab.os}`"
      :key="tab.os"
      type="button"
      role="tab"
      :data-os="tab.os"
      :aria-selected="os === tab.os"
      :aria-controls="controls(tab.os)"
      :tabindex="os === tab.os ? 0 : -1"
      @click="os = tab.os"
      @keydown="onKey"
    >
      {{ tab.text }}
    </button>
  </div>
</template>
