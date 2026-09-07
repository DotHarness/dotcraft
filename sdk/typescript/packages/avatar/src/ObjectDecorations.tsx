import type { PrimaryId } from './appearanceModel.js'
import { Detail, Silhouette as S } from './DecorationShapes.js'

export function ObjectDecoration({ id }: { id: PrimaryId }) {
  switch (id) {
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
    default: return null
  }
}
