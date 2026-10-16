import { Suspense } from 'react'
import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { lazyWithPrefetch } from '../utils/lazyWithPrefetch'

function Greeting({ name }: { name: string }): JSX.Element {
  return <p>Hello {name}</p>
}

describe('lazyWithPrefetch', () => {
  it('shows the fallback until a view that was not prefetched has loaded', async () => {
    const View = lazyWithPrefetch(async () => Greeting)
    render(<Suspense fallback={<p>Loading</p>}><View name="Ada" /></Suspense>)
    expect(screen.getByText('Loading')).toBeInTheDocument()
    expect(await screen.findByText('Hello Ada')).toBeInTheDocument()
  })

  it('renders a prefetched view on its first mount without a fallback', async () => {
    const View = lazyWithPrefetch(async () => Greeting)
    await View.prefetch()
    render(<Suspense fallback={<p>Loading</p>}><View name="Ada" /></Suspense>)
    expect(screen.getByText('Hello Ada')).toBeInTheDocument()
    expect(screen.queryByText('Loading')).toBeNull()
  })

  it('loads the module once however often it is prefetched', async () => {
    let loads = 0
    const View = lazyWithPrefetch(async () => { loads++; return Greeting })
    await Promise.all([View.prefetch(), View.prefetch()])
    await View.prefetch()
    expect(loads).toBe(1)
  })

  it('retries a module whose load failed', async () => {
    let attempts = 0
    const View = lazyWithPrefetch(async () => {
      attempts++
      if (attempts === 1) throw new Error('chunk failed')
      return Greeting
    })
    await expect(View.prefetch()).rejects.toThrow('chunk failed')
    await View.prefetch()
    render(<View name="Ada" />)
    expect(screen.getByText('Hello Ada')).toBeInTheDocument()
  })
})
