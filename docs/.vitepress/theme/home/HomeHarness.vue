<script setup lang="ts">
import Icon from './Icon.vue'
import { data } from './home.data'
import { useHome } from './home'

const { t, href } = useHome()
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
      <div class="dc-harness__stage dc-field">
        <figure class="dc-viewer" :aria-label="t.harness.viewer">
          <figcaption class="dc-viewer__bar">
            <Icon name="file-code" :size="14" :stroke="2" />
            Program.cs
          </figcaption>
          <div class="dc-viewer__code" v-html="data.program" />
        </figure>
      </div>
    </div>
  </section>
</template>
