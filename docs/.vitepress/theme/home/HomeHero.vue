<script setup lang="ts">
import { ref } from 'vue'
import CopyButton from './CopyButton.vue'
import DemoWindow from './DemoWindow.vue'
import DownloadSplit from './DownloadSplit.vue'
import Icon from './Icon.vue'
import InstallTabs from './InstallTabs.vue'
import PerchedMascot from './PerchedMascot.vue'
import { installCommands } from './copy'
import { data } from './home.data'
import { useHome } from './home'

const { t, os, href } = useHome()
const mascot = ref<InstanceType<typeof PerchedMascot>>()
</script>

<template>
  <section class="dc-hero" aria-labelledby="hero-title">
    <div class="dc-wrap">
      <a class="dc-hero__eyebrow" href="https://github.com/DotHarness/dotcraft">
        <Icon name="github" :size="14" />
        <span>{{ t.hero.eyebrow }}</span>
        <Icon name="chevron-right" :size="14" :stroke="2" />
      </a>
      <h1 id="hero-title" class="dc-hero__title">
        <span class="dc-line">{{ t.hero.lines[0] }}</span>
        {{ ' ' }}
        <span class="dc-line">{{ t.hero.lines[1] }}<span class="dc-dot">{{ t.hero.stop }}</span></span>
      </h1>
      <p class="dc-hero__lead">{{ t.hero.lead }}</p>
      <div class="dc-hero__actions">
        <div class="dc-actions">
          <a class="dc-button" data-variant="primary" data-size="prominent" :href="href('/getting-started')">{{ t.start }}</a>
          <DownloadSplit />
        </div>
        <div class="dc-install">
          <InstallTabs id="hero-tab" v-model="os" :label="t.install.tablist" :controls="(value) => `hero-cmd-${value}`" />
          <code
            v-for="(command, key) in installCommands"
            :id="`hero-cmd-${key}`"
            :key="key"
            class="dc-install__cmd"
            role="tabpanel"
            :aria-labelledby="`hero-tab-${key}`"
            :hidden="os !== key"
          >{{ command }}</code>
          <CopyButton
            round
            :text="() => installCommands[os]"
            :label="t.install.copyLabel"
            :copy="t.install.copy"
            :copied="t.install.copied"
            @copied="mascot?.celebrate()"
          />
        </div>
      </div>
    </div>
    <figure class="dc-stage">
      <div class="dc-stage__glow" aria-hidden="true" />
      <PerchedMascot ref="mascot" perch="hero" :poses="data.mascots.hero" :size="84" />
      <DemoWindow />
    </figure>
  </section>
</template>
