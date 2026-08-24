<script setup lang="ts">
/**
 * Live DotCraft mascot. Markup only — behavior lives in liveMascot.ts, motion
 * in mascot.css. Geometry is ported from the desktop MascotRobot.tsx (same
 * mascot-* sub-part hooks and arm hinges); role faces and accessories come
 * verbatim from docs/public/team-*.svg. The initial `data-state` is SSR'd so
 * the page shows a correct static face without JavaScript. `uid` is an
 * explicit prop (not useId) to keep SVG def ids SSR/hydration-deterministic.
 */

export type MascotRole = 'leader' | 'explorer' | 'builder' | 'reviewer' | 'operator'

interface Props {
  uid: string
  role?: MascotRole
  state?: string
  interactive?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  state: 'idle',
  interactive: false
})

interface Palette {
  body: [string, string, string]
  /** [offset, color] mark-gradient stops. */
  mark: Array<[number, string]>
  markCoords: { x1: number; y1: number; x2: number; y2: number }
  shadow: string
  lift: string
  /** Solid raised-arm fills — a rotated gradient arm would break the torso field. */
  armL: string
  armR: string
}

const PALETTES: Record<'brand' | MascotRole, Palette> = {
  brand: {
    body: ['#2458f7', '#5f82f7', '#8fa5ff'],
    mark: [
      [0, '#2257f5'],
      [0.55, '#577df7'],
      [1, '#8ca2ff']
    ],
    markCoords: { x1: 380, y1: 696, x2: 492, y2: 557 },
    shadow: '#0b3d62',
    lift: '#163a88',
    armL: '#3161f7',
    armR: '#7a96fb'
  },
  leader: {
    body: ['#2563eb', '#4f7cf6', '#8198f5'],
    mark: [
      [0, '#2563eb'],
      [1, '#6f8df5']
    ],
    markCoords: { x1: 366, y1: 644, x2: 658, y2: 572 },
    shadow: '#07307c',
    lift: '#07307c',
    armL: '#2e69ed',
    armR: '#6b8cf5'
  },
  explorer: {
    body: ['#0284c7', '#0ea5e9', '#38bdf8'],
    mark: [
      [0, '#0369a1'],
      [1, '#22d3ee']
    ],
    markCoords: { x1: 366, y1: 694, x2: 658, y2: 560 },
    shadow: '#075985',
    lift: '#075985',
    armL: '#058bce',
    armR: '#26b2f1'
  },
  builder: {
    body: ['#6d28d9', '#8b5cf6', '#a78bfa'],
    mark: [
      [0, '#5b21b6'],
      [1, '#a78bfa']
    ],
    markCoords: { x1: 366, y1: 694, x2: 658, y2: 560 },
    shadow: '#4c1d95',
    lift: '#4c1d95',
    armL: '#7433df',
    armR: '#9b76f8'
  },
  reviewer: {
    body: ['#15803d', '#22c55e', '#4ade80'],
    mark: [
      [0, '#166534'],
      [1, '#4ade80']
    ],
    markCoords: { x1: 366, y1: 694, x2: 658, y2: 560 },
    shadow: '#14532d',
    lift: '#14532d',
    armL: '#188f44',
    armR: '#38d371'
  },
  operator: {
    body: ['#d97706', '#eab308', '#fbbf24'],
    mark: [
      [0, '#92400e'],
      [1, '#f59e0b']
    ],
    markCoords: { x1: 366, y1: 694, x2: 658, y2: 560 },
    shadow: '#78350f',
    lift: '#78350f',
    armL: '#dd8406',
    armR: '#f4ba18'
  }
}

const palette = PALETTES[props.role ?? 'brand']

const bodyId = `dc-m-body-${props.uid}`
const markId = `dc-m-mark-${props.uid}`
const yellowId = `dc-m-yellow-${props.uid}`
const shadowId = `dc-m-shadow-${props.uid}`
const liftId = `dc-m-lift-${props.uid}`
const accessoryId = `dc-m-acc-${props.uid}`

const body = `url(#${bodyId})`
const mark = `url(#${markId})`
const yellow = `url(#${yellowId})`

const armVars = {
  '--mascot-raised-arm-left': palette.armL,
  '--mascot-raised-arm-right': palette.armR
}
</script>

<template>
  <span
    class="dc-mascot"
    data-live
    :data-role="role"
    :data-state="state"
    :data-interactive="interactive ? 'true' : undefined"
  >
    <span class="dc-mascot__jelly">
      <svg
        viewBox="0 0 1024 1024"
        fill="none"
        aria-hidden="true"
        focusable="false"
        :style="armVars"
      >
        <defs>
          <linearGradient :id="bodyId" x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">
            <stop offset="0" :stop-color="palette.body[0]" />
            <stop offset=".5" :stop-color="palette.body[1]" />
            <stop offset="1" :stop-color="palette.body[2]" />
          </linearGradient>
          <linearGradient
            :id="markId"
            :x1="palette.markCoords.x1"
            :y1="palette.markCoords.y1"
            :x2="palette.markCoords.x2"
            :y2="palette.markCoords.y2"
            gradientUnits="userSpaceOnUse"
          >
            <stop v-for="[offset, color] in palette.mark" :key="offset" :offset="offset" :stop-color="color" />
          </linearGradient>
          <linearGradient :id="yellowId" x1="481" y1="174" x2="617" y2="713" gradientUnits="userSpaceOnUse">
            <stop offset="0" stop-color="#ffcf11" />
            <stop offset="1" stop-color="#f6b500" />
          </linearGradient>
          <filter :id="shadowId" x="-12%" y="-12%" width="124%" height="124%">
            <feDropShadow dx="0" dy="18" stdDeviation="24" :flood-color="palette.shadow" flood-opacity=".16" />
          </filter>
          <filter :id="liftId" x="-8%" y="-8%" width="116%" height="116%">
            <feDropShadow dx="0" dy="10" stdDeviation="16" :flood-color="palette.lift" flood-opacity=".1" />
          </filter>
          <filter v-if="role" :id="accessoryId" x="-18%" y="-18%" width="136%" height="136%">
            <feDropShadow dx="0" dy="14" stdDeviation="14" flood-color="#020617" flood-opacity=".18" />
          </filter>
        </defs>
        <g transform="translate(512 528) scale(1.3) translate(-512 -512)">
          <g :filter="`url(#${shadowId})`">
            <rect x="201" y="365" width="622" height="513" rx="151" fill="#fff" />
            <!-- Halo capsules centered on the blue bands (165/723, not the brand
                 145/743) so the outline survives arm rotation. -->
            <rect class="mascot-arm-l-w" x="165" y="472" width="136" height="256" rx="58" fill="#fff" />
            <rect class="mascot-arm-r-w" x="723" y="472" width="136" height="256" rx="58" fill="#fff" />
            <rect x="438" y="270" width="148" height="182" rx="2" fill="#fff" />
            <circle class="mascot-antenna-w" cx="512" cy="229" r="116" fill="#fff" />
          </g>
          <g :filter="`url(#${liftId})`">
            <rect x="243" y="408" width="538" height="426" rx="113" :fill="body" />
            <rect class="mascot-arm-l-b" x="188" y="514" width="90" height="171" rx="19" :fill="body" />
            <rect class="mascot-arm-r-b" x="746" y="514" width="90" height="171" rx="19" :fill="body" />
            <rect x="479" y="337" width="66" height="119" rx="6" :fill="body" />
          </g>
          <rect x="295" y="464" width="434" height="315" rx="78" fill="#fff" />
          <circle class="mascot-glow" cx="512" cy="229" r="96" fill="#f6b500" />
          <circle class="mascot-light" cx="512" cy="229" r="73" :fill="yellow" />
          <!-- All faces stay mounted; the mascot.css matrix picks one. -->
          <g class="mascot-face mascot-face-neutral">
            <g class="mascot-eyes">
              <path d="M387 568 477 634 387 700" :stroke="mark" stroke-width="38" stroke-linecap="round" stroke-linejoin="round" />
            </g>
            <path class="mascot-caret" d="M531 696h116" :stroke="yellow" stroke-width="27" stroke-linecap="round" />
          </g>
          <g class="mascot-face mascot-face-happy">
            <g class="mascot-eyes">
              <path d="M379 585 452 622 379 659" :stroke="mark" stroke-width="30" stroke-linecap="round" stroke-linejoin="round" />
              <path d="M645 585 572 622 645 659" :stroke="mark" stroke-width="30" stroke-linecap="round" stroke-linejoin="round" />
            </g>
            <path d="M470 702c20 18 64 18 84 0" :stroke="yellow" stroke-width="24" stroke-linecap="round" fill="none" />
          </g>
          <g class="mascot-face mascot-face-sleep">
            <path d="M373 618c26 20 56 20 82 0" :stroke="mark" stroke-width="26" stroke-linecap="round" fill="none" />
            <path d="M569 618c26 20 56 20 82 0" :stroke="mark" stroke-width="26" stroke-linecap="round" fill="none" />
            <path d="M488 704h48" :stroke="yellow" stroke-width="20" stroke-linecap="round" />
          </g>
          <g class="mascot-face mascot-face-explorer">
            <g class="mascot-eyes">
              <path d="M389 612h58" :stroke="mark" stroke-width="30" stroke-linecap="round" />
              <path d="M577 612h58" :stroke="mark" stroke-width="30" stroke-linecap="round" />
            </g>
            <path d="M493 700h42" :stroke="mark" stroke-width="20" stroke-linecap="round" />
          </g>
          <g class="mascot-face mascot-face-builder">
            <g class="mascot-eyes">
              <path d="M379 585 452 622 379 659" :stroke="mark" stroke-width="30" stroke-linecap="round" stroke-linejoin="round" />
              <path d="M645 585 572 622 645 659" :stroke="mark" stroke-width="30" stroke-linecap="round" stroke-linejoin="round" />
            </g>
            <path d="M482 704c18 11 42 11 60 0" :stroke="mark" stroke-width="22" stroke-linecap="round" fill="none" />
          </g>
          <g class="mascot-face mascot-face-reviewer">
            <g class="mascot-eyes">
              <path d="M366 586h88v46" :stroke="mark" stroke-width="28" stroke-linecap="round" stroke-linejoin="round" />
              <path d="M570 586h88v46" :stroke="mark" stroke-width="28" stroke-linecap="round" stroke-linejoin="round" />
            </g>
            <path d="M481 700h62" :stroke="mark" stroke-width="22" stroke-linecap="round" />
          </g>
          <g class="mascot-face mascot-face-operator">
            <g class="mascot-eyes">
              <rect x="381" y="581" width="45" height="87" rx="16" :fill="mark" />
              <rect x="598" y="581" width="45" height="87" rx="16" :fill="mark" />
            </g>
            <path d="M487 700h50" :stroke="mark" stroke-width="22" stroke-linecap="round" />
          </g>
        </g>
        <!-- Accessories sit outside the 1.3x wrapper, in the outer 1024 space. -->
        <g v-if="role === 'leader'" class="mascot-accessory" transform="rotate(6 838 665)" :filter="`url(#${accessoryId})`">
          <rect x="718" y="562" width="244" height="194" rx="36" fill="#fff" stroke="#fff" stroke-width="30" />
          <rect x="718" y="562" width="244" height="194" rx="36" fill="#fff" :stroke="mark" stroke-width="11" />
          <path d="M760 620h114" :stroke="mark" stroke-width="18" stroke-linecap="round" />
          <circle cx="775" cy="689" r="14" :fill="mark" />
          <circle cx="842" cy="661" r="14" :fill="yellow" />
          <circle cx="907" cy="708" r="14" :fill="mark" />
          <path d="M789 683 828 667 893 699" stroke="#202124" stroke-width="13" stroke-linecap="round" stroke-linejoin="round" opacity=".72" />
        </g>
        <g v-else-if="role === 'explorer'" class="mascot-accessory" :filter="`url(#${accessoryId})`">
          <g transform="translate(22 -22) rotate(28 862 646)">
            <path d="M862 646v112" stroke="#fff" stroke-width="48" stroke-linecap="round" />
            <path d="M862 646v112" :stroke="mark" stroke-width="28" stroke-linecap="round" />
            <circle cx="862" cy="562" r="82" fill="#fff" stroke="#fff" stroke-width="30" />
            <circle cx="862" cy="562" r="82" fill="#fff" fill-opacity=".72" :stroke="mark" stroke-width="18" />
            <path d="M828 534c21-22 54-30 82-19" stroke="#fff" stroke-width="12" stroke-linecap="round" opacity=".9" />
          </g>
        </g>
        <g v-else-if="role === 'builder'" class="mascot-accessory" transform="translate(122 -140) rotate(-18 826 706)" :filter="`url(#${accessoryId})`">
          <path d="M756 595c30-30 74-39 113-23l-56 56 46 46 56-56c16 39 7 83-23 113-32 32-80 39-119 20L674 850c-18 18-47 18-65 0s-18-47 0-65l99-99c-19-39-12-87 20-119Z" fill="#fff" stroke="#fff" stroke-width="34" stroke-linejoin="round" />
          <path d="M756 595c30-30 74-39 113-23l-56 56 46 46 56-56c16 39 7 83-23 113-32 32-80 39-119 20L674 850c-18 18-47 18-65 0s-18-47 0-65l99-99c-19-39-12-87 20-119Z" fill="#fff" :stroke="mark" stroke-width="13" stroke-linejoin="round" />
          <circle cx="642" cy="817" r="17" :fill="yellow" />
          <path d="M706 752 774 684" :stroke="mark" stroke-width="20" stroke-linecap="round" opacity=".78" />
        </g>
        <g v-else-if="role === 'reviewer'" class="mascot-accessory" transform="translate(58 -52) rotate(8 820 674)" :filter="`url(#${accessoryId})`">
          <path d="M817 553 929 594v76c0 80-46 134-112 162-66-28-112-82-112-162v-76Z" fill="#fff" stroke="#fff" stroke-width="30" stroke-linejoin="round" />
          <path d="M817 553 929 594v76c0 80-46 134-112 162-66-28-112-82-112-162v-76Z" fill="#fff" :stroke="mark" stroke-width="11" stroke-linejoin="round" />
          <path d="M761 683 802 722 881 632" :stroke="mark" stroke-width="28" stroke-linecap="round" stroke-linejoin="round" />
        </g>
        <g v-else-if="role === 'operator'" class="mascot-accessory" transform="rotate(7 839 686)" :filter="`url(#${accessoryId})`">
          <rect x="714" y="592" width="250" height="190" rx="38" fill="#fff" stroke="#fff" stroke-width="30" />
          <rect x="714" y="592" width="250" height="190" rx="38" fill="#fff" :stroke="mark" stroke-width="11" />
          <path d="M765 650h148" stroke="#202124" stroke-width="14" stroke-linecap="round" opacity=".62" />
          <path d="M765 718h148" stroke="#202124" stroke-width="14" stroke-linecap="round" opacity=".62" />
          <circle cx="818" cy="650" r="23" :fill="mark" stroke="#fff" stroke-width="8" />
          <circle cx="872" cy="718" r="23" :fill="yellow" stroke="#fff" stroke-width="8" />
        </g>
      </svg>
    </span>
  </span>
</template>
