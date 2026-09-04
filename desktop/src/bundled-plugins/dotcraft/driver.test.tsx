import '../../renderer/tests/setupPluginRuntime'
import type {
  DesktopPluginHost,
  DesktopPluginSurfaceComponent
} from '@dotcraft/plugin'
import { act, render } from '@testing-library/react'

import { activate, type DotCraftDesktopDriver } from './src/index'

describe('DotCraft Desktop driver', () => {
  afterEach(() => {
    Reflect.deleteProperty(globalThis, 'driver')
  })

  it('installs the driver and resolves readiness after the application surface mounts', async () => {
    const registered = createHost()
    const activation = await activate(registered.host)
    const driver = expectDriver()
    let restored = false
    const readiness = driver.whenWorkbenchRestored().then(() => { restored = true })

    expect(restored).toBe(false)
    render(<registered.Surface host={registered.host} context={{ rootElement: document.body }} />)
    await act(() => readiness)

    expect(restored).toBe(true)
    await activation?.dispose?.()
    expect(globalThis.driver).toBeUndefined()
  })

  it('rejects pending readiness and removes its driver when disposed', async () => {
    const registered = createHost()
    const activation = await activate(registered.host)
    const readiness = expectDriver().whenWorkbenchRestored()

    await activation?.dispose?.()

    await expect(readiness).rejects.toThrow('disposed before the workbench restored')
    expect(globalThis.driver).toBeUndefined()
  })

  it('does not let a stale generation remove a newer driver', async () => {
    const first = createHost()
    const firstActivation = await activate(first.host)
    const replacement: DotCraftDesktopDriver = {
      whenWorkbenchRestored: () => Promise.resolve()
    }
    globalThis.driver = replacement

    await firstActivation?.dispose?.()

    expect(globalThis.driver).toBe(replacement)
  })

  it('installs a new driver after revision replacement disposes the old generation', async () => {
    const firstActivation = await activate(createHost().host)
    const firstDriver = expectDriver()
    await firstActivation?.dispose?.()

    const secondActivation = await activate(createHost().host)
    const secondDriver = expectDriver()

    expect(secondDriver).not.toBe(firstDriver)
    await secondActivation?.dispose?.()
  })

  it('refuses to replace a foreign driver', async () => {
    const existing: DotCraftDesktopDriver = {
      whenWorkbenchRestored: () => Promise.resolve()
    }
    globalThis.driver = existing

    expect(() => activate(createHost().host)).toThrow('already installed')
    expect(globalThis.driver).toBe(existing)
  })
})

function createHost(): {
  host: DesktopPluginHost
  Surface: DesktopPluginSurfaceComponent<'app.background'>
} {
  let Surface: DesktopPluginSurfaceComponent<'app.background'> | undefined
  const host = {
    ui: {
      add(surface: string, component: DesktopPluginSurfaceComponent<'app.background'>) {
        expect(surface).toBe('app.background')
        Surface = component
        return () => undefined
      }
    }
  } as unknown as DesktopPluginHost
  return {
    host,
    get Surface() {
      if (!Surface) throw new Error('Surface has not been registered.')
      return Surface
    }
  }
}

function expectDriver(): DotCraftDesktopDriver {
  const driver = globalThis.driver
  if (!driver) throw new Error('Desktop driver was not installed.')
  return driver
}
