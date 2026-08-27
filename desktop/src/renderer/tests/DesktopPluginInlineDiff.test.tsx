import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { DesktopPluginInlineDiff } from '../components/desktopPlugins/DesktopPluginInlineDiff'

describe('Desktop Plugin InlineDiff', () => {
  it('adapts public text props to the Host-owned body-only diff view', () => {
    render(
      <DesktopPluginInlineDiff
        filePath="src/Widget.cs"
        line={27}
        before="old line"
        after="new line"
      />
    )

    expect(screen.getByTestId('inline-diff-view')).toBeInTheDocument()
    expect(screen.queryByTestId('file-result-header')).toBeNull()
    expect(screen.getByText('old line')).toBeInTheDocument()
    expect(screen.getByText('new line')).toBeInTheDocument()
  })
})
