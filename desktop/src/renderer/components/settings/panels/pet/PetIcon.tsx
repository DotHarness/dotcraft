import { createLucideIcon } from 'lucide-react'

export const PetIcon = createLucideIcon('pet', [
  ['path', { d: 'M12 3h.01', key: 'light' }],
  ['path', { d: 'M12 4v3', key: 'antenna' }],
  ['rect', { x: '3', y: '7', width: '18', height: '13', rx: '5', key: 'body' }],
  ['path', { d: 'm8 11.5 3 2.5-3 2.5', key: 'prompt' }],
  ['path', { d: 'M13.5 16.5H17', key: 'cursor' }]
])
