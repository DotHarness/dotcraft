import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { PALETTE, type AvatarSpec } from '../components/agents/agentAvatar'
import { MascotRobot } from '../components/conversation/MascotRobot'

describe('MascotRobot', () => {
  it('derives raised-arm and prop colors from the agent profile palette', () => {
    const orangeAvatar: AvatarSpec = { palette: 6, face: 0, accessory: 0 }
    const palette = PALETTE[orangeAvatar.palette]
    const { container } = render(<MascotRobot avatar={orangeAvatar} />)

    const mascot = screen.getByLabelText('DotCraft mascot') as SVGSVGElement
    expect(mascot.style.getPropertyValue('--mascot-raised-arm-left')).toBe('#ce4c0e')
    expect(mascot.style.getPropertyValue('--mascot-raised-arm-right')).toBe('#fb9b4b')
    expect(mascot.style.getPropertyValue('--mascot-shadow-color')).toBe(palette.shadow)

    const signQuestion = container.querySelector('.mascot-prop-sign path')
    const signDot = container.querySelector('.mascot-prop-sign circle')
    expect(signQuestion).toHaveAttribute('stroke', palette.markD)
    expect(signDot).toHaveAttribute('fill', palette.markD)

    const laptopBlueLines = container.querySelectorAll('.mascot-laptop-lines path:not(.mascot-laptop-caret)')
    expect(laptopBlueLines[0]).toHaveAttribute('stroke', palette.markL)
    expect(laptopBlueLines[2]).toHaveAttribute('stroke', palette.markL)
    expect(laptopBlueLines[4]).toHaveAttribute('stroke', palette.markL)
  })

  it('keeps the default DotCraft blue action colors without an agent profile', () => {
    const { container } = render(<MascotRobot />)

    const mascot = screen.getByLabelText('DotCraft mascot') as SVGSVGElement
    expect(mascot.style.getPropertyValue('--mascot-raised-arm-left')).toBe('#3161f7')
    expect(mascot.style.getPropertyValue('--mascot-raised-arm-right')).toBe('#7a96fb')
    expect(mascot.style.getPropertyValue('--mascot-shadow-color')).toBe('#0b3d62')

    const signQuestion = container.querySelector('.mascot-prop-sign path')
    const signDot = container.querySelector('.mascot-prop-sign circle')
    expect(signQuestion).toHaveAttribute('stroke', '#3161f7')
    expect(signDot).toHaveAttribute('fill', '#3161f7')

    const laptopBlueLines = container.querySelectorAll('.mascot-laptop-lines path:not(.mascot-laptop-caret)')
    expect(laptopBlueLines[0]).toHaveAttribute('stroke', '#8ca2ff')
    expect(laptopBlueLines[2]).toHaveAttribute('stroke', '#8ca2ff')
    expect(laptopBlueLines[4]).toHaveAttribute('stroke', '#8ca2ff')
  })
})
