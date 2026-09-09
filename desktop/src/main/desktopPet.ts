import { BrowserWindow, ipcMain, screen } from 'electron'
import { join } from 'path'
import { clampPet, petActivity, type PetCommand, type PetEvent, type PetPoint, type PetRect, type PetSnapshot } from '../shared/desktopPet'
import { PET_RETURN_DURATION, samplePetReturn, type PetReturnFrame } from '../shared/desktopPetMotion'
import { SUPPORTED_LOCALE_VALUES } from '../shared/locales/types'

let active: DesktopPet | null = null

export function restoreDesktopPet(win: BrowserWindow): boolean {
  if (!active || active.owner !== win || !active.detached) return false
  active.returnHome()
  return true
}

export function attachDesktopPet(owner: BrowserWindow): void {
  active?.dispose()
  active = new DesktopPet(owner)
}

class DesktopPet {
  private overlay: BrowserWindow | null = null
  private snapshot: PetSnapshot | null = null
  private point: PetPoint = { x: 0, y: 0 }
  private seat: PetRect | null = null
  private dragging: { cursor: PetPoint; point: PetPoint } | null = null
  private generation = 0
  private watchdog: ReturnType<typeof setTimeout> | undefined
  private returning = false
  private revealing = false
  private started = false
  private settled = false
  private disposed = false
  private sourceDragging = false
  private chatOpen = false
  private sourceTimer: ReturnType<typeof setInterval> | undefined
  private readonly ownerContents: Electron.WebContents
  detached = false

  constructor(readonly owner: BrowserWindow) {
    this.ownerContents = owner.webContents
    ipcMain.removeHandler('desktop-pet:command')
    ipcMain.handle('desktop-pet:command', (event, command: PetCommand) => {
      const fromOwner = event.sender === this.ownerContents
      const fromPet = !!this.overlay && !this.overlay.isDestroyed() && event.sender === this.overlay.webContents
      if (!fromOwner && !fromPet) throw new Error('Unknown desktop pet sender')
      if (!command || typeof command.type !== 'string') throw new Error('Invalid desktop pet command')
      switch (command.type) {
        case 'detach':
          if (fromOwner && !this.detached && validRect(command.seat) && validPoint(command.point)
            && validSnapshot(command.snapshot)) this.detach(command)
          break
        case 'ready': if (fromPet) this.ready(); break
        case 'source-drag': if (fromOwner && this.sourceDragging) this.updateSourceDrag(command.stage === 'end'); break
        case 'chat':
          if (fromPet && typeof command.open === 'boolean' && !this.returning) {
            this.chatOpen = command.open
            this.place(this.point)
          }
          break
        case 'hidden': if (fromOwner && this.detached && !this.returning && !this.started) this.leave(); break
        case 'return': this.returnHome(); break
        case 'seat':
          if (fromOwner && this.returning) this.land(validRect(command.seat) ? this.toScreen(command.seat) : this.seat)
          break
        case 'snapshot':
          if (fromOwner && validSnapshot(command.snapshot)) {
            this.snapshot = command.snapshot
            this.send(this.overlay, { type: 'snapshot', snapshot: command.snapshot })
          }
          break
        case 'edit':
          if (fromPet && this.detached && !this.returning && this.snapshot?.canChat
            && typeof command.text === 'string' && command.text.length <= 100000 && typeof command.submit === 'boolean'
            && Number.isSafeInteger(command.revision) && command.revision >= 0) {
            this.send(owner, command)
          }
          break
        case 'drag': if (fromPet && this.settled && !this.returning) this.drag(command.stage); break
        case 'interactive':
          if (fromPet && typeof command.value === 'boolean') this.overlay?.setIgnoreMouseEvents(this.sourceDragging || !command.value, { forward: true })
          break
      }
    })
    owner.on('show', this.onShow)
    owner.on('closed', this.onClosed)
    owner.webContents.on('render-process-gone', this.onFailure)
    owner.webContents.on('did-start-loading', this.onFailure)
    screen.on('display-removed', this.onDisplayChange)
    screen.on('display-metrics-changed', this.onDisplayChange)
  }

  private send(win: BrowserWindow | null, event: PetEvent): void {
    if (win && !win.isDestroyed() && !win.webContents.isDestroyed()) win.webContents.send('desktop-pet:event', event)
  }

  private toScreen(rect: PetRect): PetRect {
    const bounds = this.owner.getContentBounds()
    const zoom = this.owner.webContents.getZoomFactor()
    return { x: bounds.x + rect.x * zoom, y: bounds.y + rect.y * zoom, width: rect.width * zoom, height: rect.height * zoom }
  }

  private detach(command: Extract<PetCommand, { type: 'detach' }>): void {
    this.detached = true
    this.owner.webContents.setBackgroundThrottling(false)
    this.snapshot = command.snapshot
    this.seat = this.toScreen(command.seat)
    const release = this.toScreen({ ...command.point, width: 0, height: 0 })
    this.point = { x: release.x - this.seat.width / 2, y: release.y - this.seat.height / 2 }
    this.sourceDragging = command.pointerHeld === true
    if (this.sourceDragging) {
      this.dragging = { cursor: screen.getCursorScreenPoint(), point: { ...this.point } }
      this.sourceTimer = setInterval(() => this.updateSourceDrag(false), 16)
    }
    const area = screen.getDisplayNearestPoint({ x: Math.round(this.point.x), y: Math.round(this.point.y) }).workArea
    const overlay = new BrowserWindow({
      ...area, show: false, frame: false, transparent: true, backgroundColor: '#00000000',
      resizable: false, hasShadow: false, skipTaskbar: true, alwaysOnTop: true,
      webPreferences: { preload: join(__dirname, '../preload/pet.js'), contextIsolation: true,
        nodeIntegration: false, sandbox: true, backgroundThrottling: false }
    })
    this.overlay = overlay
    overlay.setIgnoreMouseEvents(true, { forward: true })
    overlay.webContents.setWindowOpenHandler(() => ({ action: 'deny' }))
    overlay.webContents.on('will-navigate', event => event.preventDefault())
    overlay.webContents.once('render-process-gone', this.onFailure)
    overlay.webContents.once('did-fail-load', this.onFailure)
    overlay.once('closed', () => { if (this.overlay === overlay) this.recover() })
    const url = this.owner.webContents.getURL()
    const load = url.startsWith('http')
      ? overlay.loadURL(new URL('/pet.html', url).href)
      : overlay.loadFile(join(__dirname, '../renderer/pet.html'))
    void load.catch(this.onFailure)
    this.watchdog = setTimeout(this.onFailure, 5000)
  }

  private ready(): void {
    if (!this.snapshot || !this.overlay || this.returning || this.started) return
    clearTimeout(this.watchdog)
    this.send(this.overlay, { type: 'snapshot', snapshot: this.snapshot })
    this.place(this.point, this.seat?.width ?? 44)
    this.send(this.owner, { type: 'ownership', detached: true })
    this.watchdog = setTimeout(this.onFailure, 3000)
  }

  private leave(): void {
    clearTimeout(this.watchdog)
    this.started = true
    this.overlay?.showInactive()
    void this.animate(320, progress => {
      this.owner.setOpacity(1 - progress)
      this.place(this.point, (this.seat?.width ?? 44) + (59 - (this.seat?.width ?? 44)) * progress)
    }).then(done => {
      if (!done || this.disposed || this.owner.isDestroyed()) return
      this.settled = true
      if (!this.sourceDragging) { this.owner.hide(); this.owner.setOpacity(1) }
      this.place(this.point, 59, !this.sourceDragging)
    })
  }

  private place(point: PetPoint, size = 59, snap = false, pose?: PetReturnFrame): void {
    if (!this.overlay || this.overlay.isDestroyed()) return
    const area = screen.getDisplayNearestPoint({ x: Math.round(point.x), y: Math.round(point.y) }).workArea
    this.point = clampPet(point, area, size, snap)
    this.point.y = Math.min(this.point.y, area.y + area.height - size - (this.chatOpen && !this.returning ? 80 : 40))
    this.overlay.setBounds(area)
    this.send(this.overlay, { type: 'position', point: { x: this.point.x - area.x, y: this.point.y - area.y }, size,
      phase: this.returning ? 'returning' : this.settled ? 'pet' : 'leaving',
      held: this.sourceDragging,
      ...(pose ? { pose: { scaleX: pose.scaleX, scaleY: pose.scaleY, rotation: pose.rotation } } : {}),
      ...(this.returning && this.seat ? { landing: { x: this.seat.x - area.x + this.seat.width / 2, y: this.seat.y - area.y + this.seat.height } } : {}) })
  }

  private drag(stage: 'start' | 'move' | 'end'): void {
    const cursor = screen.getCursorScreenPoint()
    if (stage === 'start') this.dragging = { cursor, point: this.point }
    else if (this.dragging) {
      this.place({ x: this.dragging.point.x + cursor.x - this.dragging.cursor.x,
        y: this.dragging.point.y + cursor.y - this.dragging.cursor.y }, 59, stage === 'end')
      if (stage === 'end') this.dragging = null
    }
  }

  private updateSourceDrag(end: boolean): void {
    if (!this.sourceDragging || this.disposed || this.returning) return
    this.drag(end ? 'end' : 'move')
    if (!end) return
    this.stopSourceDrag()
    this.place(this.point, 59, true)
    if (this.settled && !this.owner.isDestroyed()) { this.owner.hide(); this.owner.setOpacity(1) }
  }

  private stopSourceDrag(): void {
    clearInterval(this.sourceTimer)
    this.sourceTimer = undefined
    this.sourceDragging = false
    this.dragging = null
  }

  returnHome(): void {
    if (!this.detached || this.returning) return
    if (!this.started) { this.recover(); return }
    this.generation++
    this.returning = true
    this.stopSourceDrag()
    this.owner.hide()
    this.owner.setOpacity(1)
    clearTimeout(this.watchdog)
    this.send(this.owner, { type: 'return-seat' })
    this.watchdog = setTimeout(() => this.land(this.seat), 500)
  }

  private land(seat: PetRect | null): void {
    if (!this.returning || this.revealing) return
    this.revealing = true
    this.seat = seat
    clearTimeout(this.watchdog)
    const start = { ...this.point }
    void this.animate(PET_RETURN_DURATION, progress => {
      if (seat) {
        const frame = samplePetReturn(start, seat, progress)
        this.place(frame, 59 + ((seat.width || 44) - 59) * frame.travel, false, frame)
      }
    }, true).then(async done => {
      if (!done || this.owner.isDestroyed()) return
      this.owner.setOpacity(0)
      if (this.owner.isMinimized()) this.owner.restore()
      this.owner.show()
      if (await this.animate(260, progress => this.owner.setOpacity(progress))) this.recover()
    })
  }

  private async animate(duration: number, frame: (progress: number) => void, linear = false): Promise<boolean> {
    const generation = this.generation
    const start = Date.now()
    do {
      if (generation !== this.generation || this.owner.isDestroyed()) return false
      const p = this.snapshot?.reducedMotion ? 1 : Math.min(1, (Date.now() - start) / duration)
      frame(linear ? p : 1 - Math.pow(1 - p, 3))
      if (p === 1) return true
      await new Promise(resolve => setTimeout(resolve, 16))
    } while (true)
  }

  private closeOverlay(): void {
    const overlay = this.overlay
    this.overlay = null
    if (overlay && !overlay.isDestroyed()) overlay.destroy()
  }
  private recover(): void {
    if (this.disposed) return
    this.generation++
    clearTimeout(this.watchdog)
    this.stopSourceDrag()
    this.detached = false
    this.returning = false
    this.revealing = false
    this.started = false
    this.settled = false
    this.chatOpen = false
    this.closeOverlay()
    if (!this.owner.isDestroyed()) {
      this.owner.setOpacity(1)
      this.owner.webContents.setBackgroundThrottling(true)
      this.send(this.owner, { type: 'ownership', detached: false })
      this.owner.show()
      this.owner.focus()
    }
  }
  private onFailure = (): void => { if (this.detached) this.recover() }
  private onShow = (): void => { if (this.detached && !this.returning) { this.owner.hide(); this.returnHome() } }
  private onClosed = (): void => this.dispose()
  private onDisplayChange = (): void => { if (this.detached) this.place(this.point, 59, true) }
  dispose(): void {
    if (this.disposed) return
    this.disposed = true
    this.detached = false
    this.generation++
    clearTimeout(this.watchdog)
    this.stopSourceDrag()
    this.closeOverlay()
    this.owner.removeListener('show', this.onShow)
    this.owner.removeListener('closed', this.onClosed)
    // `closed` runs after BrowserWindow's native handle has gone away.
    this.ownerContents.removeListener('render-process-gone', this.onFailure)
    this.ownerContents.removeListener('did-start-loading', this.onFailure)
    screen.removeListener('display-removed', this.onDisplayChange)
    screen.removeListener('display-metrics-changed', this.onDisplayChange)
    ipcMain.removeHandler('desktop-pet:command')
    if (active === this) active = null
  }
}

function validPoint(value: unknown): value is PetPoint {
  const point = value as PetPoint | null
  return !!point && Number.isFinite(point.x) && Number.isFinite(point.y)
}
function validRect(value: unknown): value is PetRect {
  const rect = value as PetRect | null
  return validPoint(rect) && Number.isFinite(rect.width) && Number.isFinite(rect.height) && rect.width > 0 && rect.height > 0
}
function validSnapshot(value: unknown): value is PetSnapshot {
  const snapshot = value as PetSnapshot | null
  return !!snapshot && typeof snapshot.name === 'string' && snapshot.name.length <= 1000
    && typeof snapshot.text === 'string' && snapshot.text.length <= 100000
    && (snapshot.theme === 'dark' || snapshot.theme === 'light')
    && SUPPORTED_LOCALE_VALUES.includes(snapshot.locale)
    && typeof snapshot.reducedMotion === 'boolean' && typeof snapshot.canChat === 'boolean'
    && (snapshot.activity === undefined || petActivity(snapshot.activity) === snapshot.activity)
}
