import type { HeadId } from './items.js'
import { Detail, Glow, Silhouette as S, useClipId } from './DecorationShapes.js'

function RingedPlanet() {
  const clip = useClipId()
  return <>
    <defs><clipPath id={clip}><circle cx="512" cy="300" r="92" /></clipPath></defs>
    <Glow blur={22} className="dca-fx-pulse"><circle cx="512" cy="312" r="150" fill="#f1e3c8" opacity=".5" /></Glow>
    <g transform="rotate(-16 512 335)">
      <ellipse cx="512" cy="335" rx="175" ry="40" stroke="#fff" strokeWidth="40" fill="none" />
      <ellipse cx="512" cy="335" rx="175" ry="40" stroke="#f1e3c8" strokeWidth="22" fill="none" />
    </g>
    <S d="M512 208a92 92 0 1 1 0 184a92 92 0 1 1 0-184Z" fill="#d9b07a" />
    <g clipPath={`url(#${clip})`}>
      <path d="M410 272c70 24 140 24 210 0v40c-70 24-140 24-210 0Z" fill="#b8875a" />
    </g>
    <g transform="rotate(-16 512 335)">
      <path d="M337 335A175 40 0 0 0 687 335" stroke="#fff" strokeWidth="40" fill="none" />
      <path d="M337 335A175 40 0 0 0 687 335" stroke="#f1e3c8" strokeWidth="22" fill="none" />
    </g>
  </>
}

function Ufo() {
  return <g className="dca-fx-hover" style={{ transformOrigin: '512px 300px' }}>
    <Glow blur={16} className="dca-fx-pulse"><path d="M452 336 402 404h220l-50-68Z" fill="#c7f6ff" opacity=".6" /></Glow>
    <path d="M452 336 402 404h220l-50-68Z" fill="#c7f6ff" opacity=".4" />
    <S d="M432 296c0-50 36-86 80-86s80 36 80 86Z" fill="#a2c5d1" />
    <S d="M342 304c0-26 76-46 170-46s170 20 170 46-76 46-170 46-170-20-170-46Z" fill="#8b95a5" />
    <g fill="#f6b500"><circle className="dca-fx dca-fx-node" cx="420" cy="312" r="11" /><circle className="dca-fx dca-fx-node" cx="512" cy="322" r="11" style={{ animationDelay: '.6s' }} /><circle className="dca-fx dca-fx-node" cx="604" cy="312" r="11" style={{ animationDelay: '1.2s' }} /></g>
  </g>
}

function MiniVolcano() {
  return <>
    <Glow blur={20} className="dca-fx-pulse"><ellipse cx="512" cy="244" rx="90" ry="48" fill="#ff8a3d" opacity=".75" /></Glow>
    <g fill="#a59c99" stroke="#fff" strokeWidth="10" paintOrder="stroke fill">
      <path className="dca-fx dca-fx-steam" d="M476 228a16 16 0 1 1 32 0a16 16 0 1 1-32 0ZM492 218a22 22 0 1 1 44 0a22 22 0 1 1-44 0ZM521 230a15 15 0 1 1 30 0a15 15 0 1 1-30 0Z" />
      <path className="dca-fx dca-fx-steam" d="M506 178a14 14 0 1 1 28 0a14 14 0 1 1-28 0ZM522 170a15 15 0 1 1 30 0a15 15 0 1 1-30 0Z" style={{ animationDelay: '-.9s' }} />
      <circle className="dca-fx dca-fx-steam" cx="514" cy="134" r="11" style={{ animationDelay: '-1.7s' }} />
    </g>
    <S d="M372 400C416 396 448 340 460 262q2-16 16-16h72q14 0 16 16c12 78 44 134 88 138Z" fill="#564545" />
    <path d="M532 246h16q14 0 16 16c12 78 44 134 88 138h-76c-18-44-32-100-44-154Z" fill="#3f3232" />
    <path d="M460 262q2-16 16-16h72q14 0 16 16l4 28c0 26-16 26-16 0q-10-8-20-2c0 16-14 16-14 0q-12-8-24 0c0 22-16 22-16 0l-20-2Z" fill="#e8451f" />
    <ellipse cx="512" cy="256" rx="42" ry="9" fill="#ffb347" />
  </>
}

const talons = 'M512 360v36M548 358v38M518 399h-28q-8 0-8 8M554 399h-28q-8 0-8 8'
function PhoenixPerch() {
  return <>
    <Glow blur={24} className="dca-fx-pulse"><ellipse cx="596" cy="252" rx="132" ry="104" fill="#ffb347" opacity=".45" /></Glow>
    <g className="dca-fx-flame" style={{ transformOrigin: '596px 330px' }}>
      <S d="M590 356C640 364 700 330 724 282Q706 300 682 290Q722 248 716 196Q696 222 668 236Q684 184 654 142C644 190 596 236 570 300Z" fill="#ffb02e" />
      <path d="M600 340C640 344 684 320 700 290Q684 296 668 292Q694 256 690 222Q672 244 654 252Q664 212 646 180C636 216 608 250 590 300Z" fill="#ffe27a" />
    </g>
    <g className="dca-fx-flame" style={{ transformOrigin: '466px 226px', animationDelay: '-.21s' }}>
      <S d="M444 230C436 206 446 182 466 168Q468 190 480 196Q496 176 522 174Q506 190 504 200Q518 196 532 204C516 222 500 232 488 236Z" fill="#ffb02e" />
    </g>
    <g fill="none" strokeLinecap="round" strokeLinejoin="round">
      <path d={talons} stroke="#fff" strokeWidth="25" /><path d={talons} stroke="#6b4a36" strokeWidth="11" />
    </g>
    <S d="M424 258a38 38 0 1 1 76 0a38 38 0 1 1-76 0ZM460 304a74 48 14 1 1 144 36a74 48 14 1 1-144-36Z" fill="#ee6a2c" />
    <path d="M488 306c30-22 92-22 136 12l-28 6 18 18-30 2 10 16c-40 4-84-4-106-54Z" fill="#c2412a" />
    <path d="M430 248 396 262 432 274Z" fill="#ffcf4a" stroke="#fff" strokeWidth="10" strokeLinejoin="round" paintOrder="stroke fill" />
    <circle cx="452" cy="252" r="7" fill="#3b2418" />
  </>
}

export function ObjectDecoration({ id }: { id: HeadId }) {
  switch (id) {
    case 'ringed-planet': return <RingedPlanet />
    case 'cat-ears': return <>
      <S d="M340 404 384 210 520 380Z" fill="#c9a27e" /><path d="M374 388 396 268 480 372Z" fill="#f2a0b4" />
      <S d="M684 404 640 210 504 380Z" fill="#c9a27e" /><path d="M650 388 628 268 544 372Z" fill="#f2a0b4" />
    </>
    case 'mushroom': return <>
      <rect x="470" y="356" width="84" height="52" rx="16" fill="#fff1dc" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
      <S d="M352 366c0-90 72-160 160-160s160 70 160 160c0 26-20 40-46 40H398c-26 0-46-14-46-40Z" fill="#e8654f" />
      <g fill="#fff4ef"><circle cx="430" cy="300" r="24" /><circle cx="540" cy="266" r="20" /><circle cx="600" cy="334" r="18" /><circle cx="490" cy="350" r="16" /></g>
    </>
    case 'shark-fin': return <>
      <S d="M446 404c10-92 42-166 104-214 22 62 72 134 118 214Z" fill="#8b95a5" />
      <path d="M480 404c8-64 30-118 70-160 12 42 34 96 68 160Z" fill="#a7b1c0" />
    </>
    case 'ice-cream': return <>
      <S d="M440 330 512 130 584 330Z" fill="#e0ad84" />
      <S d="M512 258a82 82 0 1 1 0 164a82 82 0 1 1 0-164Z" fill="#f2a0b4" />
      <circle cx="512" cy="120" r="20" fill="#e8654f" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
    </>
    case 'flower-crown': return <>
      <S d="M330 404c40-40 324-40 364 0v4H330Z" fill="#89b875" />
      <g stroke="#fff" strokeWidth="12" paintOrder="stroke fill">
        <circle cx="372" cy="392" r="26" fill="#f2a0b4" /><circle cx="442" cy="376" r="26" fill="#f6b500" /><circle cx="512" cy="370" r="28" fill="#f2a0b4" /><circle cx="582" cy="376" r="26" fill="#f6b500" /><circle cx="652" cy="392" r="26" fill="#f2a0b4" />
      </g>
      <g fill="#fff"><circle cx="372" cy="392" r="9" /><circle cx="442" cy="376" r="9" /><circle cx="512" cy="370" r="10" /><circle cx="582" cy="376" r="9" /><circle cx="652" cy="392" r="9" /></g>
    </>
    case 'lightning': return <>
      <Glow blur={20} className="dca-fx-pulse"><path d="M548 160 438 320h70l-32 84 116-150h-70Z" fill="#fff3c4" opacity=".8" /></Glow>
      <S d="M548 160 438 320h70l-32 84 116-150h-70Z" fill="#ffcf11" />
    </>
    case 'crystal-cluster': return <>
      <Glow blur={20} className="dca-fx-pulse"><path d="M512 200 560 320 512 404 464 320Z" fill="#c4b5fd" opacity=".8" /></Glow>
      <S d="M440 270 474 340 446 404 412 340Z" fill="#a78bfa" /><S d="M584 270 618 340 590 404 556 340Z" fill="#a78bfa" />
      <S d="M512 200 560 320 512 404 464 320Z" fill="#8b5cf6" /><path d="M512 200 536 320 512 404Z" fill="#c4b5fd" />
    </>
    case 'ufo': return <Ufo />
    case 'mini-volcano': return <MiniVolcano />
    case 'phoenix-perch': return <PhoenixPerch />
    case 'poop': return <>
      <S d="M393 315c-17-37 10-65 57-67-12-33 15-48 39-53 21-5 29-17 27-35 52 20 69 43 55 75 47 0 72 35 52 73 70 9 81 92 8 92H389c-67 0-62-77 4-85Z" fill="#a97b5f" />
      <path d="M397 313c64 19 157 22 227-5-9 32-171 60-227 5Zm53-65c35 16 80 15 121-13-1 31-82 49-121 13Z" fill="#805840" />
      <Detail><path d="M391 365h142M468 290h37" stroke="#c49976" strokeWidth="14" strokeLinecap="round" /></Detail>
    </>
    case 'banana': return <>
      <S d="M350 243c15 80 71 93 143 65 59-24 100-64 131-119l11-23 32 8-5 25c8 89-61 185-164 202-100 17-172-45-174-135l-7-21 24-15Z" fill="#f3cf62" />
      <path d="M350 243c15 80 71 93 143 65 59-24 100-64 131-119-28 112-139 176-204 145-37-18-58-49-70-91Z" fill="#dbaa40" />
      <path d="m624 189 11-23 32 8-5 25Zm-300 77-7-21 24-15 9 13 5 26Z" fill="#89705a" />
      <Detail><path d="M422 375c81 23 170-30 209-107" stroke="#ffe7a0" strokeWidth="12" strokeLinecap="round" fill="none" /></Detail>
    </>
    case 'fried-egg': return <>
      <S d="M343 330c-32-39 8-82 54-76 33-47 99-37 124-12 73-34 130 5 137 37 53 0 71 60 32 80-4 37-58 44-91 39H427c-39 6-101-25-84-68Z" fill="#fff9e8" />
      <path d="M344 351c40 33 121 28 168 26 76 3 129 1 178-18-4 37-58 44-91 39H427c-34 5-87-18-83-47Z" fill="#e6d9bb" />
      <ellipse cx="513" cy="299" rx="72" ry="56" fill="#f3be43" />
      <Detail><path d="M475 283c9-14 24-18 38-18" stroke="#ffe39a" strokeWidth="13" strokeLinecap="round" /></Detail>
    </>
    case 'rubber-duck': return <>
      <S d="M403 277c-13-10-20-29-17-47 8-57 94-71 121-21 16 28 5 52-5 64 42 17 83 19 115 4l39-25c13-8 22 0 16 15l-20 49c16 51-24 87-83 87H460c-58 0-89-47-57-85l-40-8c-24-5-28-21-3-27Z" fill="#f6d05e" />
      <path d="M416 354c47 32 184 26 236-38 16 51-24 87-83 87H460c-38 0-65-20-66-44Z" fill="#e4ae3e" />
      <path d="m407 277-47 6c-25 6-21 22 3 27l44 8c20-2 20-35 0-41Z" fill="#e58b4b" />
      <circle cx="431" cy="246" r="9" fill="#6e563e" />
      <Detail><path d="M500 318c16-8 51-8 63 7-11 22-39 27-63 16" stroke="#e4ae3e" strokeWidth="12" strokeLinecap="round" fill="none" /></Detail>
    </>
    case 'paper-boat': return <>
      <S d="m330 297 74 0 102-115 118 115h70l-84 102H414Z" fill="#d6e7eb" />
      <path d="m404 297 102-115 7 115Z" fill="#f1f7f5" /><path d="m506 182 118 115H513Z" fill="#92b5c1" />
      <path d="m330 297 182 50 182-50-84 102H414Z" fill="#92b5c1" /><path d="m330 297 182 50-98 52Z" fill="#d6e7eb" />
      <Detail><path d="m512 347 98 52" stroke="#7a9eac" strokeWidth="6" /></Detail>
    </>
    case 'traffic-cone': return <>
      <S d="m485 211-80 151h-44c-18 0-21 37 0 37h302c21 0 18-37 0-37h-44l-80-151c-8-15-46-15-54 0Z" fill="#ed985f" />
      <path d="m445 287 134 0 22 41H423Z" fill="#fff0dd" /><path d="m519 204 100 158h-45Z" fill="#c96c43" opacity=".35" />
      <path d="M361 380h302" stroke="#c96c43" strokeWidth="31" strokeLinecap="round" />
      <Detail><path d="m486 239-12 22" stroke="#ffc699" strokeWidth="12" strokeLinecap="round" /></Detail>
    </>
    case 'sprout': return <>
      <S d="M501 399v-91c-93 26-151-28-157-93 76-19 143 12 167 65 13-87 76-117 148-99-8 83-58 130-134 126v92Z" fill="#89b875" />
      <path d="M501 399v-91c-78 21-137-17-154-73 74 49 110 45 164 45 44-8 99-40 145-84-21 78-59 114-131 111v92Z" fill="#537f59" />
      <Detail><path d="m378 236 114 57m49-12 79-69" stroke="#b5d38f" strokeWidth="10" strokeLinecap="round" /></Detail>
    </>
    case 'donut': return <>
      <path d="M512 170a116 116 0 1 1 0 232 116 116 0 0 1 0-232Zm0 74a42 42 0 1 0 0 84 42 42 0 0 0 0-84Z" fill="#dbab70" fillRule="evenodd" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
      <path d="M403 322c49 72 169 73 218-4-14 51-56 84-109 84-52 0-94-31-109-80Z" fill="#bf8456" />
      <path d="M396 284c0-153 231-153 232 0-2 23-26 20-33 0-9-27-13 5-31 4-6 0-10-3-10-14-13-47-75-41-83 2-4 19-18 27-25 6-11-32-20 22-39 18-8-1-11-8-11-16Z" fill="#e5a0b3" />
      <Detail><path d="m449 230 12 9m58-41 1 14m55 21-9 10" stroke="#fff0ce" strokeWidth="10" strokeLinecap="round" /></Detail>
    </>
    // The bottom edge slopes 8° so the rotation lands it flat on the top edge.
    case 'floppy-disk': return <g transform="rotate(-8 512 404)">
      <S d="M388 176h234l30 30v218l-280-40V192c0-9 7-16 16-16Z" fill="#3c4658" />
      <path d="M440 176h156v94c0 6-4 10-10 10H450c-6 0-10-4-10-10Z" fill="#a7b1c0" /><rect x="400" y="302" width="224" height="62" rx="10" fill="#fff" />
      <Detail><rect x="548" y="196" width="30" height="64" rx="6" fill="#3c4658" /></Detail>
    </g>
    case 'rocket': return <>
      <g className="dca-fx dca-fx-flame" style={{ transformOrigin: '512px 330px' }}>
        <path d="M512 398c-24-18-34-40-30-68h60c4 28-6 50-30 68Z" fill="#ffb347" /><path d="M512 380c-12-14-16-30-12-50h24c4 20 0 36-12 50Z" fill="#ffe08a" />
      </g>
      <S d="M446 248c-34 20-52 56-54 100v44c0 8 8 11 14 6l40-40Zm132 0c34 20 52 56 54 100v44c0 8-8 11-14 6l-40-40Z" fill="#e8654f" />
      <S d="M512 128c48 28 68 86 68 154v36c0 7-5 12-12 12H456c-7 0-12-5-12-12v-36c0-68 20-126 68-154Z" fill="#dce8f4" />
      <path d="M512 128c29 17 48 44 58 78H454c10-34 29-61 58-78Z" fill="#e8654f" />
      <Detail><circle cx="512" cy="258" r="26" fill="#3c4658" /></Detail>
    </>
    default: return null
  }
}
