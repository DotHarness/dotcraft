<script setup lang="ts">
import Icon from './Icon.vue'
import { useHome } from './home'

const { t, href } = useHome()
</script>

<template>
  <section class="dc-section" aria-labelledby="why-title">
    <div class="dc-wrap">
      <header class="dc-head">
        <h2 id="why-title">{{ t.why.title }}</h2>
        <p>{{ t.why.lead }}</p>
      </header>
      <div class="dc-why">
        <section v-for="(group, index) in t.why.groups" :key="group.title" class="dc-group" :aria-labelledby="`why-group-${index}`">
          <div class="dc-group__head">
            <h3 :id="`why-group-${index}`">{{ group.title }}</h3>
            <a class="dc-button" data-variant="ghost" data-size="sm" :href="href(group.link.href)">
              {{ group.link.text }}
              <Icon name="arrow-right" :size="14" :stroke="2" />
            </a>
          </div>
          <div class="dc-card">
            <a v-for="row in group.rows" :key="row.label" class="dc-row" :href="href(row.href)">
              <span class="dc-row__mark"><Icon :name="row.icon" :size="18" /></span>
              <span class="dc-row__copy">
                <span class="dc-row__label">{{ row.label }}</span>
                <span class="dc-row__hint">{{ row.hint }}</span>
              </span>
              <span class="dc-row__go"><Icon name="chevron-right" :stroke="2" /></span>
            </a>
          </div>
        </section>
      </div>
    </div>
  </section>
</template>
