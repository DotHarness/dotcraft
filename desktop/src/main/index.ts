import { app, powerMonitor } from 'electron'
import { registerViewerScheme } from './viewerFileProtocol'
import { registerPluginFileScheme } from './pluginFileProtocol'
import { registerMcpAppSandboxScheme } from './mcpAppSandboxProtocol'
import { DesktopApplication } from './desktopApplication'
import { configureAppIdentity } from './appIdentity'
import { computerUseManager } from './computerUse/runtime'
import { installStandardIoErrorGuard } from './standardIoGuards'

registerViewerScheme()
registerPluginFileScheme()
registerMcpAppSandboxScheme()
configureAppIdentity()

const singleInstance = app.isPackaged && process.platform !== 'darwin'
if (singleInstance && !app.requestSingleInstanceLock()) app.exit(0)
else {
  let application: DesktopApplication | null = null
  const launches: string[][] = []
  let finishedQuit = false
  let quitting = false
  const routeLaunch = (argv: string[]): void => {
    if (!application) {
      launches.push(argv)
      return
    }
    void application.manager
      .routeLaunch(argv)
      .catch((error) => console.warn('[desktop] launch request failed', error))
  }
  app.on('second-instance', (_event, argv) => routeLaunch(argv))
  app.on('open-url', (event, url) => {
    event.preventDefault()
    routeLaunch([url])
  })
  app.on('activate', () => {
    if (application) void application.openDesktopWindow()
  })
  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin' && application && !application.hasTray()) app.quit()
  })
  app.on('before-quit', (event) => {
    if (finishedQuit || !application) return
    event.preventDefault()
    if (quitting) return
    quitting = true
    let timer: ReturnType<typeof setTimeout> | undefined
    void Promise.race([
      application.dispose(),
      new Promise<void>((resolve) => {
        timer = setTimeout(resolve, 5_000)
      })
    ]).finally(() => {
      if (timer) clearTimeout(timer)
      finishedQuit = true
      app.quit()
    })
  })
  const onBrokenPipe = (): void => {
    if (import.meta.env.DEV) app.quit()
  }
  installStandardIoErrorGuard(process.stdout, onBrokenPipe)
  installStandardIoErrorGuard(process.stderr, onBrokenPipe)
  void app
    .whenReady()
    .then(async () => {
      try {
        app.setAsDefaultProtocolClient('dotcraft')
      } catch (error) {
        console.warn('[desktop] protocol registration failed', error)
      }
      application = new DesktopApplication()
      powerMonitor.on('lock-screen', () => computerUseManager.stop('locked'))
      await application.start(process.argv)
      for (const argv of launches.splice(0)) routeLaunch(argv)
    })
    .catch((error) => {
      console.error('[desktop] startup failed', error)
      app.exit(1)
    })
}
