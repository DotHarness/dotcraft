import type { ReactNode } from 'react'
import type { BackId, FaceId, HandId, HeadId, SkinId } from './items.js'
import type { BodyPaint, Faceplate } from './MascotRig.js'
import { Detail, FaceplateLayer, FaceplateSurface, Glow, Silhouette as S } from './DecorationShapes.js'
import { paintMaterials } from './paintMaterials.js'
import { materialBounds } from './RigMaterial.js'

const star4 = (cx: number, cy: number, r: number) =>
  `M${cx} ${cy - r}Q${cx + r * .22} ${cy - r * .22} ${cx + r} ${cy}Q${cx + r * .22} ${cy + r * .22} ${cx} ${cy + r}Q${cx - r * .22} ${cy + r * .22} ${cx - r} ${cy}Q${cx - r * .22} ${cy - r * .22} ${cx} ${cy - r}Z`

const musicNote = (x: number, y: number) =>
  `M${x} ${y}v-54l38-10v48a16 16 0 1 1-12-15v-21l-14 4v48a16 16 0 1 1-12-15Z`

const mirrored = (art: ReactNode) => <>{art}<g transform="matrix(-1 0 0 1 1024 0)">{art}</g></>

export function hasCostumeHeadBack(id: string): boolean {
  return id === 'twin-tails' || id === 'vocal-twintails' || id === 'plush-bunny-hood' || id === 'dino-hood'
}

export function CostumeHeadBackDecoration({ id }: { id: HeadId | string }) {
  switch (id) {
    case 'twin-tails': return mirrored(
      <g className="dca-fx-sway" style={{ transformOrigin: '256px 388px' }}>
        <S d="M276 376C174 382 96 462 84 590c-10 104 32 184 96 218 16-54 14-106 38-152 14 36 32 66 62 84-6-92 8-198 28-296Z" fill="#f6a5c0" stroke={20} />
        <path d="M254 416c-68 24-118 92-124 186-4 64 16 122 50 158 12-46 14-94 38-138Z" fill="#e36d97" />
        <Detail><path d="M198 446c-42 36-66 92-68 156" stroke="#ffd6e5" strokeWidth="14" strokeLinecap="round" fill="none" /></Detail>
      </g>,
    )
    case 'vocal-twintails': return <>
      {mirrored(
        <g className="dca-fx-sway" style={{ transformOrigin: '264px 356px' }}>
          <Glow blur={16} className="dca-fx-pulse">
            <ellipse cx="118" cy="790" rx="52" ry="74" fill="#64f5eb" opacity=".55" />
          </Glow>
          <S d="M278 352C146 336 52 444 42 628c-8 128 24 218 76 262 12-58 18-112 40-156 10 48 28 90 58 118 4-126 24-276 62-414Z" fill="#39c5bb" stroke={20} />
          <path d="M258 394C162 406 94 500 88 646c-4 88 14 162 42 212 10-52 18-104 38-146Z" fill="#1d9b94" />
          <path d="M56 728c6 72 28 126 62 162 12-56 18-110 40-154 10 48 28 90 58 118 2-46 6-94 12-142-58 26-118 28-172 16Z" fill="#8cf7ef" opacity=".88" />
          <Detail><path d="M196 418c-52 46-84 126-88 228" stroke="#c8fffb" strokeWidth="14" strokeLinecap="round" fill="none" /></Detail>
          <g className="dca-fx dca-fx-sparkle" fill="#c8fffb" stroke="#fff" strokeWidth="8" paintOrder="stroke fill">
            <path d={star4(78, 766, 20)} />
          </g>
        </g>,
      )}
      <path className="dca-fx dca-fx-shard" d={musicNote(108, 520)} fill="#ff2e88" stroke="#fff" strokeWidth="12" strokeLinejoin="round" paintOrder="stroke fill" style={{ animationDelay: '.15s' }} />
      <path className="dca-fx dca-fx-shard" d={musicNote(888, 500)} fill="#4de3ff" stroke="#fff" strokeWidth="12" strokeLinejoin="round" paintOrder="stroke fill" style={{ animationDelay: '.65s' }} />
    </>
    case 'plush-bunny-hood': return <S d="M214 396h596a54 54 0 0 1 54 54v48a46 46 0 0 1-46 46H206a46 46 0 0 1-46-46v-48a54 54 0 0 1 54-54Z" fill="#ead8c7" stroke={20} />
    case 'dino-hood': return <>
      <S d="M220 392h584a50 50 0 0 1 50 50v46a42 42 0 0 1-42 42H212a42 42 0 0 1-42-42v-46a50 50 0 0 1 50-50Z" fill="#4eb37d" stroke={20} />
      {mirrored(<S d="M212 372 156 346l24 58Z" fill="#ff944d" stroke={16} />)}
    </>
    default: return null
  }
}

export function CostumeHeadDecoration({ id }: { id: HeadId }) {
  switch (id) {
    case 'twin-tails': return <>
      <S d="M312 394c8-98 84-156 200-156s192 58 200 156c-42 18-76 34-102 6-26 30-68 34-98 4-30 30-72 26-98-4-26 28-60 12-102-6Z" fill="#f6a5c0" />
      <path d="M512 246c98 0 174 52 188 138-34 12-62 18-86-2-24 22-60 26-102 2Z" fill="#e36d97" opacity=".55" />
      <Detail><path d="M388 316c34-26 84-36 142-30" stroke="#ffd9e8" strokeWidth="14" strokeLinecap="round" fill="none" /></Detail>
      {mirrored(<g>
        <S d="M312 356 254 322q-16-8-18 10l-6 52q-2 18 16 12l66-22Z" fill="#ff5d8f" stroke={14} />
        <S d="M312 356 356 330q14-8 14 10v36q0 16-14 10l-44-18Z" fill="#e03e70" stroke={14} />
        <circle cx="312" cy="356" r="18" fill="#ffd166" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
      </g>)}
    </>
    case 'vocal-twintails': return <>
      <S d="M286 410c12-108 92-172 226-172s214 64 226 172c-28 24-50 32-68 4-24 26-56 32-82 6l-28 26-24-28-24 28-28-26c-26 26-58 20-82-6-18 28-40 20-68-4Z" fill="#39c5bb" stroke={18} />
      <path d="M512 246c112 0 190 56 210 152-24 16-44 20-60 0-24 22-54 26-80 4l-70-26Z" fill="#1d9b94" opacity=".48" />
      <Detail>
        <path d="M368 320q144-34 288 0" stroke="#c8fffb" strokeWidth="14" strokeLinecap="round" fill="none" opacity=".88" />
      </Detail>
      {mirrored(<g transform="translate(292 342) rotate(-34)">
        <rect x="-40" y="-40" width="80" height="80" rx="14" fill="#1b2030" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
        <rect x="-27" y="-27" width="54" height="54" rx="8" fill="none" stroke="#ff2e88" strokeWidth="10" />
        <Detail>
          <g className="dca-fx dca-fx-pulse" fill="#7ef7ee">
            <rect x="-14" y="-8" width="8" height="20" rx="3" />
            <rect x="-2" y="-15" width="8" height="27" rx="3" />
            <rect x="10" y="-4" width="8" height="16" rx="3" />
          </g>
        </Detail>
        <rect className="dca-fx dca-fx-facet-flash" x="-32" y="-32" width="64" height="64" rx="10" fill="#ffb3d9" />
      </g>)}
      <path className="dca-fx dca-fx-star-pop" d={star4(668, 256, 30)} fill="#c8fffb" stroke="#fff" strokeWidth="10" strokeLinejoin="round" paintOrder="stroke fill" style={{ animationDelay: '.2s' }} />
    </>
    case 'plush-bunny-hood': return <>
      {mirrored(<g className="dca-fx-sway" style={{ transformOrigin: '390px 290px' }}>
        <S d="M408 296C384 202 310 158 250 202c-54 40-66 132-24 176 28 28 72 14 96-24 22-34 46-46 86-58Z" fill="#fff5eb" stroke={18} />
        <path d="M372 292c-22-56-68-82-106-54-32 24-38 82-14 110 18 18 46 8 64-18 16-24 32-32 56-38Z" fill="#f5a9c1" />
      </g>)}
      <S d="M294 406c8-96 86-152 218-152s210 56 218 152c6 18-10 28-30 26-124-12-252-12-376 0-20 2-36-8-30-26Z" fill="#fff5eb" stroke={18} />
      <path d="M322 414c126-12 254-12 380 0" stroke="#ead8c7" strokeWidth="16" strokeLinecap="round" fill="none" />
      <Detail>
        <circle cx="466" cy="346" r="10" fill="#5c4742" /><circle cx="558" cy="346" r="10" fill="#5c4742" />
        <ellipse cx="512" cy="364" rx="16" ry="11" fill="#f5a9c1" />
      </Detail>
    </>
    case 'dino-hood': return <>
      <S d="M482 252 512 174l30 78ZM392 276l12-66 44 52ZM632 276l-12-66-44 52Z" fill="#ff944d" stroke={16} />
      <S d="M300 406c10-96 86-156 212-156s202 60 212 156c4 16-8 24-24 24H324c-16 0-28-8-24-24Z" fill="#6ed39a" stroke={18} />
      <path d="M512 250c112 0 188 54 206 150H570c-12-74-28-118-58-150Z" fill="#4eb37d" />
      <path d="M352 426l22 24 22-24m60 0 22 24 22-24m60 0 22 24 22-24m24 0 22 24 22-24" fill="#fff" stroke="#fff" strokeWidth="8" strokeLinejoin="round" paintOrder="stroke fill" />
      <Detail>
        <circle cx="446" cy="346" r="11" fill="#234233" /><circle cx="578" cy="346" r="11" fill="#234233" />
        <circle cx="492" cy="382" r="6" fill="#327a54" /><circle cx="532" cy="382" r="6" fill="#327a54" />
      </Detail>
    </>
    case 'idol-crown': return <>
      <S d="M516 286c-22-58 8-120 68-128-36 28-36 72-8 106 24-18 54-14 72 6-34 6-56 22-72 44Z" fill="#ff8fab" stroke={16} />
      <g transform="rotate(-7 500 338)">
        <S d="M388 396l16-102 58 46 38-82 38 82 58-46 16 102Z" fill="#ffd166" stroke={18} />
        <rect x="388" y="368" width="224" height="30" rx="12" fill="#f4a261" />
        <path d={star4(500, 326, 28)} fill="#ff5d8f" stroke="#fff" strokeWidth="8" paintOrder="stroke fill" />
        <Detail><circle cx="430" cy="348" r="9" fill="#fff7d6" /><circle cx="570" cy="348" r="9" fill="#fff7d6" /></Detail>
      </g>
      <g className="dca-fx dca-fx-sparkle" fill="#fff3b0">
        <path d={star4(356, 266, 20)} /><path d={star4(652, 254, 16)} style={{ animationDelay: '.7s' }} />
      </g>
    </>
    default: return null
  }
}

export function CostumeFaceDecoration({ id }: { id: FaceId }) {
  switch (id) {
    case 'ribbon-choker': return <>
      <rect x="364" y="790" width="296" height="26" rx="13" fill="#f06a92" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <S d="M484 824 418 892l38-6 18 24 38-66 38 66 18-24 38 6-66-68Z" fill="#fff1f5" stroke={14} />
      <S d="M512 818 606 786q20-6 20 14v56q0 20-20 14L512 842 418 870q-20 6-20-14v-56q0-20 20-14Z" fill="#f06a92" stroke={14} />
      <Detail><path d="M438 814h42M544 814h42" stroke="#ffc2d4" strokeWidth="8" strokeLinecap="round" /></Detail>
      <path d="M512 848c-16-12-30-24-30-38a16 16 0 0 1 30-8 16 16 0 0 1 30 8c0 14-14 26-30 38Z" fill="#ffd166" stroke="#fff" strokeWidth="8" paintOrder="stroke fill" />
    </>
    case 'neko-clip': return <g className="dca-part-temple" transform="translate(-14 14) rotate(12 800 410)">
      <path d="M726 392h94M732 422h92" stroke="#fff" strokeWidth="22" strokeLinecap="round" />
      <path d="M726 392h94M732 422h92" stroke="#ff7aa2" strokeWidth="10" strokeLinecap="round" />
      <S d="M824 358l14 28 30 4-22 22 6 30-28-14-28 14 6-30-22-22 30-4Z" fill="#ffd166" stroke={12} />
      <Detail><circle cx="824" cy="404" r="9" fill="#ff5d8f" /></Detail>
    </g>
    default: return null
  }
}

export function StarryVisorPlate() {
  return <FaceplateSurface id="starry-visor" fill="#23223a">
    <rect x="336" y="658" width="44" height="20" rx="10" fill="#ff8fab" opacity=".52" />
    <rect x="644" y="658" width="44" height="20" rx="10" fill="#ff8fab" opacity=".52" />
  </FaceplateSurface>
}

export function StarryVisorEyes() {
  const cuteEye = (cx: number) => <g key={cx}>
    <path d={`M${cx - 34} 640v-34a34 34 0 0 1 68 0v34q-34-12-68 0Z`} fill="#8be8ff" />
    <circle cx={cx - 14} cy="594" r="12" fill="#fff" />
    <path className="dca-fx dca-fx-pulse" d={star4(cx + 18, 622, 14)} fill="#ffe680" />
  </g>
  return <>
    <FaceplateLayer expression="neutral" extra={<>
      <path d="M494 666q18 16 36 0" stroke="#ffc2d6" strokeWidth="12" strokeLinecap="round" fill="none" />
      <path className="dca-fx dca-fx-star-pop" d={star4(662, 558, 18)} fill="#ffe680" style={{ animationDelay: '.5s' }} />
    </>}>
      {[420, 604].map(cuteEye)}
    </FaceplateLayer>
    <FaceplateLayer expression="happy" extra={<path d="M490 666q22 22 44 0" stroke="#ffc2d6" strokeWidth="13" strokeLinecap="round" fill="none" />}>
      <path d="M384 628q36-46 72 0M568 628q36-46 72 0" stroke="#8be8ff" strokeWidth="24" strokeLinecap="round" fill="none" />
      <path d={star4(658, 578, 16)} fill="#ffe680" />
    </FaceplateLayer>
    <FaceplateLayer expression="operator" extra={<path d="M496 672h32" stroke="#ffc2d6" strokeWidth="11" strokeLinecap="round" />}>
      <rect x="386" y="598" width="68" height="42" rx="21" fill="#8be8ff" />
      <rect x="570" y="598" width="68" height="42" rx="21" fill="#8be8ff" />
      <circle cx="406" cy="613" r="8" fill="#fff" /><circle cx="590" cy="613" r="8" fill="#fff" />
    </FaceplateLayer>
    <FaceplateLayer expression="sleep">
      <path className="dca-fx dca-fx-breathe" d="M386 620q34 26 68 0M570 620q34 26 68 0" stroke="#8be8ff" strokeWidth="20" strokeLinecap="round" fill="none" opacity=".7" />
    </FaceplateLayer>
  </>
}

export function costumeFaceplateOf(id: string): Faceplate | undefined {
  return id === 'starry-visor' ? { plate: <StarryVisorPlate />, eyes: <StarryVisorEyes /> } : undefined
}

export function CostumeHandDecoration({ id }: { id: HandId }) {
  switch (id) {
    case 'stage-mic': return <g transform="translate(226 644) rotate(-22) translate(-226 -644)">
      <S d="M208 566h36l-8 148a10 10 0 0 1-20 0Z" fill="#3c4658" stroke={18} />
      <S d="M226 626 176 604q-12-4-12 10v32q0 14 12 10l50-18 50 18q12 4 12-10v-32q0-14-12-10Z" fill="#f06a92" stroke={14} />
      <circle cx="226" cy="542" r="46" fill="#dce4ee" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
      <path d="M182 542h88" stroke="#8b95a5" strokeWidth="10" />
      <Detail><path d="M202 512v60M226 502v80M250 512v60" stroke="#aeb8c7" strokeWidth="6" /></Detail>
    </g>
    case 'glow-stick': return <g transform="translate(222 648) rotate(-24) translate(-222 -648)">
      <Glow blur={18} className="dca-fx-pulse">
        <rect x="198" y="454" width="48" height="146" rx="24" fill="#ff7aa2" />
        <circle cx="222" cy="456" r="44" fill="#4de3ff" />
      </Glow>
      <S d="M204 596h36v106a18 18 0 0 1-36 0Z" fill="#f4f6fb" stroke={16} />
      <rect x="200" y="584" width="44" height="22" rx="8" fill="#f06a92" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      <rect className="dca-fx dca-fx-orb-color" x="202" y="476" width="40" height="114" rx="20" fill="#ff7aa2" stroke="#fff" strokeWidth="16" paintOrder="stroke fill" />
      <path className="dca-fx dca-fx-orb-color" d="M222 410l14 28 32 5-23 22 6 31-29-15-29 15 6-31-23-22 32-5Z" fill="#fff3b0" stroke="#fff" strokeWidth="14" strokeLinejoin="round" paintOrder="stroke fill" />
    </g>
    default: return null
  }
}

export function CostumeBackDecoration({ id }: { id: BackId }) {
  switch (id) {
    case 'waist-ribbon': return <g className="dca-fx-sway" style={{ transformOrigin: '512px 660px' }}>
      {mirrored(<>
        <S d="M320 690 132 888l76-12 30 48 122-194Z" fill="#c94570" stroke={18} />
        <S d="M310 610C194 504 68 518 58 616c-8 84 96 144 252 78Z" fill="#f06a92" stroke={20} />
        <path d="M286 626C198 552 108 564 102 620c-4 46 68 88 184 44Z" fill="#ffd9e5" />
      </>)}
    </g>
    case 'chibi-devil': return <>
      {mirrored(<g className="dca-fx-flap" style={{ transformOrigin: '272px 560px' }}>
        <S d="M272 536C196 458 104 426 48 476c24 54 16 98-12 126 62 10 86 44 72 88 54-8 98-36 164-92Z" fill="#6d3b7e" stroke={18} />
        <path d="M254 554c-58-56-128-78-168-44 16 38 12 68-6 90 44 8 62 32 54 62 38-8 72-28 120-68Z" fill="#e35d88" />
      </g>)}
      <g className="dca-fx-sway" style={{ transformOrigin: '780px 760px' }}>
        <path d="M776 760c76 18 140-26 148-96" stroke="#fff" strokeWidth="36" strokeLinecap="round" fill="none" />
        <path d="M776 760c76 18 140-26 148-96" stroke="#6d3b7e" strokeWidth="18" strokeLinecap="round" fill="none" />
        <S d="M926 618c-18-16-42-8-42 14 0 22 42 46 42 46s42-24 42-46c0-22-24-30-42-14Z" fill="#e35d88" stroke={14} />
      </g>
    </>
    default: return null
  }
}

export function CostumeSkinOverlay({ id }: { id: SkinId }) {
  if (id !== 'sailor-collar') return null
  return <g data-skin-overlay={id}>
    <path d="M243 408h538v76L512 586 243 484Z" fill="#fff" opacity=".42" />
    <path d="M260 444 512 542 764 444" stroke="var(--dca-part-shadow-color, #163a88)" strokeWidth="14" opacity=".4" fill="none" />
    <rect x="160" y="628" width="140" height="36" fill="#fff" opacity=".42" />
    <rect x="724" y="628" width="140" height="36" fill="#fff" opacity=".42" />
    <rect x="243" y="782" width="538" height="52" fill="#fff" opacity=".36" />
  </g>
}

export function costumeSkinPaint(id: SkinId): BodyPaint | undefined {
  if (id !== 'plush-fleece') return undefined
  return {
    shadow: paintMaterials['plush-fleece'].shadow,
    render: paintId => <linearGradient id={paintId} className="dca-skin-paint dca-skin-plush-fleece" x1="243" y1="408" x2="781" y2="834" gradientUnits="userSpaceOnUse">
      <stop offset="0" stopColor="#f79bb8" /><stop offset=".49" stopColor="#f58aad" />
      <stop offset=".5" stopColor="#fff1e6" /><stop offset="1" stopColor="#fde2cf" />
    </linearGradient>,
  }
}

export function CostumeSkinPaintSurface({ id }: { id: SkinId }) {
  if (id !== 'plush-fleece') return null
  return <g data-skin-overlay={id}>
    <path d="M243 834 781 408" stroke="#e05784" strokeWidth="10" strokeDasharray="22 18" opacity=".55" />
    <path d="M716 792c-12-10-24-18-24-30a13 13 0 0 1 24-6 13 13 0 0 1 24 6c0 12-12 20-24 30Z" fill="#e05784" opacity=".75" />
    <rect className="dca-fx dca-skin-energy" {...materialBounds} fill={paintMaterials['plush-fleece'].accent} opacity="0" />
  </g>
}
