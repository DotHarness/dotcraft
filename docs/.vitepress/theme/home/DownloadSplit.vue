<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import Icon from './Icon.vue'
import { useHome } from './home'
import {
  detectPlatform,
  loadManifest,
  PLATFORMS,
  RELEASES_PAGE,
  startDownload,
  type Platform,
  type ReleaseManifest
} from './downloadButton'

defineProps<{ drop?: 'up' }>()

const { locale, t } = useHome()
const root = ref<HTMLElement>()
const toggle = ref<HTMLButtonElement>()
const menu = ref<HTMLElement>()
const open = ref(false)
const detected = ref<Platform | null>(null)
const manifest = ref<ReleaseManifest | null>(null)

const mainLabel = computed(() =>
  detected.value ? `${t.download.forPlatform} ${detected.value.label[locale]}` : t.download.fallback
)

function assetHref(platform: Platform | null): string {
  return platform && manifest.value ? manifest.value.assets[platform.assetId].url : RELEASES_PAGE
}

function download(event: MouseEvent, platform: Platform | null): void {
  open.value = false
  if (!platform || manifest.value) return
  event.preventDefault()
  startDownload(platform)
}

function items(): HTMLElement[] {
  return [...(menu.value?.querySelectorAll<HTMLElement>('.dc-menu__item') ?? [])]
}

async function setOpen(next: boolean, restoreFocus = false): Promise<void> {
  open.value = next
  if (next) {
    await nextTick()
    items()[0]?.focus({ preventScroll: true })
  } else if (restoreFocus) {
    toggle.value?.focus()
  }
}

function onMenuKey(event: KeyboardEvent): void {
  const list = items()
  const index = list.indexOf(document.activeElement as HTMLElement)
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    list[(index + 1) % list.length]?.focus()
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    list[(index - 1 + list.length) % list.length]?.focus()
  } else if (event.key === 'Escape') {
    event.preventDefault()
    void setOpen(false, true)
  }
}

function onDocumentClick(event: MouseEvent): void {
  if (!root.value?.contains(event.target as Node)) open.value = false
}

onMounted(() => {
  detected.value = detectPlatform()
  void loadManifest()
    .then((value) => {
      manifest.value = value
    })
    .catch(() => {})
  document.addEventListener('click', onDocumentClick)
})

onBeforeUnmount(() => document.removeEventListener('click', onDocumentClick))
</script>

<template>
  <div ref="root" class="dc-split">
    <div class="dc-split__group">
      <a
        class="dc-button dc-split__main"
        data-variant="secondary"
        data-size="prominent"
        :href="assetHref(detected)"
        @click="download($event, detected)"
      >
        <Icon name="download" :stroke="2" />
        <span>{{ mainLabel }}</span>
      </a>
      <button
        ref="toggle"
        class="dc-button dc-split__toggle"
        data-variant="secondary"
        data-size="prominent"
        type="button"
        aria-haspopup="menu"
        :aria-expanded="open"
        :aria-label="t.download.choose"
        @click="setOpen(!open)"
      >
        <Icon name="chevron-down" :stroke="2" />
      </button>
    </div>
    <div ref="menu" class="dc-menu" role="menu" :data-drop="drop" :data-open="open || undefined" @keydown="onMenuKey">
      <a
        v-for="platform in PLATFORMS"
        :key="platform.id"
        class="dc-menu__item"
        role="menuitem"
        :href="assetHref(platform)"
        @click="download($event, platform)"
      >
        <Icon :name="platform.os" />
        <span>{{ platform.label[locale] }}</span>
        <i v-if="detected?.id === platform.id" class="dc-menu__dot" role="img" :aria-label="t.download.detected" />
      </a>
      <div class="dc-menu__rule" role="separator" />
      <a class="dc-menu__item" role="menuitem" :href="RELEASES_PAGE" @click="open = false">
        <Icon name="external-link" />
        <span>{{ t.download.all }}</span>
        <small v-if="manifest">{{ manifest.tag }}</small>
      </a>
    </div>
  </div>
</template>
