<script setup lang="ts">
import { useHome } from './home'

const { t, href } = useHome()
</script>

<template>
  <section class="dc-section" aria-labelledby="products-title">
    <div class="dc-wrap">
      <header class="dc-head">
        <h2 id="products-title">{{ t.products.title }}</h2>
      </header>
      <div class="dc-products">
        <a v-for="row in t.products.rows" :key="row.label" class="dc-product" :href="href(row.href)">
          <img class="dc-product__art" :src="row.art" alt="" width="540" height="430" loading="lazy" />
          <span class="dc-product__head">
            <span class="dc-product__name">{{ row.label }}</span>
            <span class="dc-product__get">{{ row.get }}</span>
          </span>
          <span class="dc-product__hint">{{ row.hint }}</span>
        </a>
      </div>
    </div>
  </section>
</template>
