import { contextBridge } from 'electron'
import { desktopPet } from './desktopPet'

contextBridge.exposeInMainWorld('api', { desktopPet })
