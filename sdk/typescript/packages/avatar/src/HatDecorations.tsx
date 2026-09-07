import type { PrimaryId } from './appearanceModel.js'
import { Detail, Silhouette as S } from './DecorationShapes.js'

export function HatDecoration({ id }: { id: PrimaryId }) {
  switch (id) {
    case 'baseball-cap': return <>
      <S d="M337 368c0-94 44-163 139-163 91 0 142 65 142 155 36 0 71 8 88 24 9 9-2 23-24 23H363c-24 0-31-15-26-39Z" fill="#e87967" />
      <path d="M513 214c63 18 91 72 91 146h-51c0-82-9-116-40-146Z" fill="#b94f50" />
      <path d="M358 370c113-15 249-20 315 8 23 9 28 20 7 20H363c-19 0-25-15-5-28Z" fill="#b94f50" />
      <Detail><path d="M385 322c8-48 32-75 61-83" stroke="#f5a18c" strokeWidth="14" strokeLinecap="round" /><ellipse cx="480" cy="205" rx="23" ry="10" fill="#b94f50" /></Detail>
    </>
    case 'bucket-hat': return <>
      <S d="m386 233 243 0 25 111 56 47c8 8 1 19-12 19H320c-13 0-20-11-10-20l52-48Z" fill="#94bda6" />
      <path d="m386 233 243 0-10 25H391Z" fill="#bed8c1" /><path d="m362 342 292 2 56 47c8 8 1 19-12 19H320c-13 0-20-11-10-20Z" fill="#5b887a" />
      <Detail><path d="m380 323 261 0" stroke="#bed8c1" strokeWidth="11" strokeLinecap="round" /></Detail>
    </>
    case 'beret': return <>
      <S d="M349 356c-54-6-57-73-4-112 40-30 95-48 151-43l5-26c2-13 23-11 22 3l-2 26c72 6 145 43 164 89 19 43-19 68-55 76l-16 32H370Z" fill="#b17498" />
      <path d="M349 356c69 21 201 14 281-12l-16 57H370Z" fill="#784c71" />
      <Detail><path d="M355 294c20-31 65-54 110-57" stroke="#cf9db5" strokeWidth="17" strokeLinecap="round" /></Detail>
    </>
    case 'beanie': return <>
      <S d="M365 337c0-93 53-153 147-153s147 60 147 153l9 8v56H356v-56Z" fill="#e9a653" />
      <path d="M540 187c82 14 119 76 119 150h-46c0-79-19-128-73-150Z" fill="#bc753d" />
      <rect x="356" y="337" width="312" height="64" rx="19" fill="#f7c676" />
      <Detail><path d="M383 371h53" stroke="#e9a653" strokeWidth="10" strokeLinecap="round" /><rect x="568" y="352" width="44" height="33" rx="7" fill="#bc753d" /></Detail>
    </>
    case 'top-hat': return <>
      <S d="m382 180 260 0-24 187h67c20 0 20 36 0 36H339c-20 0-20-36 0-36h67Z" fill="#465069" />
      <path d="m569 180 73 0-24 187h-66Z" fill="#293247" /><path d="M400 315h224l-6 52H406Z" fill="#c96e85" />
      <path d="M339 384h346" stroke="#293247" strokeWidth="24" strokeLinecap="round" />
      <Detail><path d="m411 207 8 68" stroke="#6a758e" strokeWidth="14" strokeLinecap="round" /></Detail>
    </>
    case 'wizard-hat': return <>
      <S d="M340 367h34c47-70 55-144 113-186 24-18 53-13 79-5-45 18-30 43-9 70l82 121h44c22 0 23 37 0 37H340c-22 0-22-37 0-37Z" fill="#8773c1" />
      <path d="M524 184c-8 29 13 52 34 83l70 100h-70l-42-113c-13-33-15-52 8-70Z" fill="#594b93" />
      <path d="M340 385h343" stroke="#594b93" strokeWidth="26" strokeLinecap="round" />
      <Detail><path d="m474 274 12 23 26 4-19 19 5 26-24-12-24 12 5-26-19-19 26-4Z" fill="#f4cc75" /></Detail>
    </>
    case 'chef-hat': return <>
      <S d="M397 324c-70 4-93-82-39-116 27-18 51-9 67 1 10-72 147-77 170-7 74-32 125 64 77 101-14 11-29 16-45 18v79H397Z" fill="#f5eee3" />
      <path d="M397 332h230v68H397Z" fill="#d7caba" /><path d="M397 332h194v53H397Z" fill="#fffaf1" />
      <Detail><path d="M420 264v39m91-62v62m90-36v36" stroke="#d7caba" strokeWidth="13" strokeLinecap="round" /></Detail>
    </>
    case 'party-hat': return <>
      <S d="m494 192-111 195c-6 12 5 16 17 16h224c14 0 24-4 17-18L531 193c21-18 10-50-17-50-29 0-41 31-20 49Z" fill="#ea9baf" />
      <path d="m450 270 120 0 21 36H430Zm-49 86h223l17 29c7 14-3 18-17 18H400c-12 0-23-4-17-16Z" fill="#8075b7" />
      <circle cx="513" cy="171" r="28" fill="#f5d07b" />
      <Detail><path d="m526 212 75 132" stroke="#f6c4ce" strokeWidth="11" strokeLinecap="round" /></Detail>
    </>
    case 'crown': return <>
      <S d="M363 282c-7-20 9-31 24-18l56 54 51-86c8-15 27-15 36 0l51 86 56-54c15-13 31-2 24 18l-24 117H387Z" fill="#efc65c" />
      <path d="M387 365h250v34H387Z" fill="#c99139" />
      <Detail><path d="m512 305 26 30-26 30-26-30Z" fill="#73b8cc" /><path d="m512 305 0 60-26-30Z" fill="#a3d8df" /><circle cx="410" cy="342" r="8" fill="#fff1af" /><circle cx="614" cy="342" r="8" fill="#fff1af" /></Detail>
    </>
    case 'hard-hat': return <>
      <S d="M358 361c0-94 42-154 133-163v-11c0-15 42-15 42 0v11c91 9 133 69 133 163h22c23 0 23 39 0 39H336c-23 0-23-39 0-39Z" fill="#f5bd48" />
      <path d="M533 198c91 9 133 69 133 163h-48c0-86-24-135-85-163Z" fill="#d68d32" /><path d="M494 192h36v169h-36Z" fill="#ffdc79" />
      <path d="M336 382h352" stroke="#d68d32" strokeWidth="25" strokeLinecap="round" />
      <Detail><path d="M390 326c2-41 21-70 43-87" stroke="#ffdc79" strokeWidth="14" strokeLinecap="round" /></Detail>
    </>
    case 'nightcap': return <>
      <S d="M358 369c-1-99 40-172 141-172 109 0 161 48 157 110 40 5 46 54 12 68-31 13-55-19-40-43-15-17-36-26-51-30l49 67v32H358Z" fill="#789cc9" />
      <path d="M499 197c83 0 161 48 157 110l-28 25c-20-29-53-40-81-43 19-16 39-24 63-20-25-39-57-57-111-72Z" fill="#4c709f" />
      <rect x="355" y="363" width="276" height="39" rx="17" fill="#dce8f4" /><circle cx="653" cy="346" r="31" fill="#dce8f4" />
      <Detail><circle cx="432" cy="296" r="10" fill="#dce8f4" /><circle cx="469" cy="245" r="9" fill="#dce8f4" /></Detail>
    </>
    case 'straw-hat': return <>
      <S d="M319 352c19-7 47-11 78-15l13-83c3-17 200-17 204 0l13 83c31 4 59 8 78 15 32 12 17 45-20 48-115 12-231 12-346 0-37-3-52-36-20-48Z" fill="#e8c78a" />
      <path d="m405 302 214 0 8 35c-72 11-150 11-230 0Z" fill="#c57568" /><ellipse cx="512" cy="258" rx="97" ry="16" fill="#f5dfb2" />
      <Detail><path d="M334 379q177 26 356 0" stroke="#c49b5e" strokeWidth="10" strokeLinecap="round" /></Detail>
    </>
    default: return null
  }
}
