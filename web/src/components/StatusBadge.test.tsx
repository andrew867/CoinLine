import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StatusBadge, terminalStatusLabel } from './StatusBadge'

describe('StatusBadge', () => {
  it('renders label', () => {
    render(<StatusBadge label="Online" variant="ok" />)
    expect(screen.getByTestId('status-badge')).toHaveTextContent('Online')
  })

  it('maps terminal status labels', () => {
    expect(terminalStatusLabel(2)).toBe('Online')
  })
})
