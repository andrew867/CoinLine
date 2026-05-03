import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { formatHexSpaced, HexViewer } from './HexViewer'

describe('HexViewer', () => {
  it('formats hex with spaces', () => {
    expect(formatHexSpaced('aabbcc')).toBe('aa bb cc')
  })

  it('renders spaced payload', () => {
    render(<HexViewer hex="0102AB" />)
    expect(screen.getByTestId('hex-viewer')).toHaveTextContent('01 02 AB')
  })
})
