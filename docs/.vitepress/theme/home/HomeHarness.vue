<script setup lang="ts">
import { ref } from 'vue'
import CopyButton from './CopyButton.vue'
import Icon from './Icon.vue'
import { data } from './home.data'
import { useHome } from './home'

const TERMINAL_COMMAND = 'dotnet add package DotCraft.Harness'

const { t, href } = useHome()
const tab = ref<'code' | 'terminal'>('code')
const code = ref<HTMLElement>()
const tabs = [
  { key: 'code', icon: 'file-code', text: 'Program.cs' },
  { key: 'terminal', icon: 'terminal', text: 'Terminal' }
] as const

function copyText(): string {
  return tab.value === 'code' ? (code.value?.innerText ?? '') : TERMINAL_COMMAND
}

function onKey(event: KeyboardEvent): void {
  if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return
  event.preventDefault()
  tab.value = tab.value === 'code' ? 'terminal' : 'code'
  const bar = (event.currentTarget as HTMLElement).parentElement
  bar?.querySelector<HTMLElement>(`#viewer-tab-${tab.value}`)?.focus()
}
</script>

<template>
  <section class="dc-section" aria-labelledby="harness-title">
    <div class="dc-wrap dc-harness">
      <div>
        <p class="dc-kicker">{{ t.harness.kicker }}</p>
        <h2 id="harness-title" class="dc-harness__title">{{ t.harness.title }}</h2>
        <div class="dc-points">
          <div v-for="point in t.harness.points" :key="point.title" class="dc-point">
            <span class="dc-row__mark"><Icon :name="point.icon" :size="18" /></span>
            <div>
              <h3>{{ point.title }}</h3>
              <p>{{ point.text }}</p>
            </div>
          </div>
        </div>
        <div class="dc-harness__links">
          <a class="dc-button" data-variant="secondary" :href="href(t.harness.overview.href)">{{ t.harness.overview.text }}</a>
          <a class="dc-button" data-variant="ghost" :href="href(t.harness.nuget.href)">
            <Icon name="nuget" :size="15" />
            {{ t.harness.nuget.text }}
          </a>
        </div>
      </div>
      <div class="dc-viewer">
        <div class="dc-viewer__bar" role="tablist" :aria-label="t.harness.viewer">
          <button
            v-for="item in tabs"
            :id="`viewer-tab-${item.key}`"
            :key="item.key"
            class="dc-viewer__tab"
            type="button"
            role="tab"
            :aria-controls="`viewer-${item.key}`"
            :aria-selected="tab === item.key"
            :tabindex="tab === item.key ? 0 : -1"
            @click="tab = item.key"
            @keydown="onKey"
          >
            <Icon :name="item.icon" :size="14" :stroke="2" />
            {{ item.text }}
          </button>
          <CopyButton tip-below :text="copyText" :label="t.install.copy" :copy="t.install.copy" :copied="t.install.copied" />
        </div>
        <div class="dc-viewer__pane">
          <div
            id="viewer-code"
            ref="code"
            class="dc-viewer__code"
            role="tabpanel"
            aria-labelledby="viewer-tab-code"
            :hidden="tab !== 'code'"
            v-html="data.program"
          />
          <pre
            id="viewer-terminal"
            class="dc-viewer__term"
            role="tabpanel"
            aria-labelledby="viewer-tab-terminal"
            :hidden="tab !== 'terminal'"
          ><code><span class="dc-viewer__prompt">$ </span>{{ TERMINAL_COMMAND }}</code></pre>
        </div>
      </div>
    </div>
  </section>
</template>
