import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { ImageGenerationStatus } from '../components/conversation/ImageGenerationStatus'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'
import type { ConversationItem } from '../types/conversation'

beforeEach(() => { installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } }) })
afterEach(cleanup)

describe('ImageGenerationStatus', () => {
  const item = {
    id: 'image', type: 'imageGeneration', status: 'started', imageGenerationStatus: 'inProgress'
  } as ConversationItem

  it('stops the skeleton on completion and reports storage failure separately', () => {
    const { rerender } = render(<LocaleProvider><ImageGenerationStatus item={item} /></LocaleProvider>)
    expect(screen.getByTestId('image-generation-skeleton')).toBeInTheDocument()
    rerender(<LocaleProvider><ImageGenerationStatus item={{ ...item, status: 'completed', imageGenerationStatus: 'completed', result: 'aW1hZ2U=', saveStatus: 'failed' }} /></LocaleProvider>)
    expect(screen.queryByTestId('image-generation-skeleton')).toBeNull()
    expect(screen.getByText('Generated image')).toBeInTheDocument()
    expect(screen.getByText('Image generated, but the file could not be saved.')).toBeInTheDocument()
  })

  it('shows a terminal generation failure without animation', () => {
    render(<LocaleProvider><ImageGenerationStatus item={{ ...item, status: 'completed', imageGenerationStatus: 'failed', errorMessage: 'No final result' }} /></LocaleProvider>)
    expect(screen.queryByTestId('image-generation-skeleton')).toBeNull()
    expect(screen.getByText('No final result')).toBeInTheDocument()
  })
})
