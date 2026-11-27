import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it } from 'vitest'
import { FamilyOperatingModeNotice } from './FamilyOperatingModeNotice'

afterEach(cleanup)

it('does not turn a length-only profile into a saved operating mode', () => {
  render(<FamilyOperatingModeNotice mode={{ expectedCode: 2, expectedLabel: 'Multi-pay', configuredCode: null,
    configuredLabel: null, status: 'unverified', message: 'A matching length alone is not an operating-mode qualification.' }} />)
  expect(screen.getByText('Multi-pay (code 2)')).toBeVisible()
  expect(screen.getByText('Not established')).toBeVisible()
  expect(screen.getByText(/matching length alone/)).toBeVisible()
})

it('shows the original unrecognized value without assigning a known payment mode', () => {
  render(<FamilyOperatingModeNotice mode={{ expectedCode: 1, expectedLabel: 'Card only', configuredCode: 10,
    configuredLabel: null, status: 'invalid', message: 'Stored MT26 operating-mode code 10 is not a recognized choice for this layout.' }} />)
  expect(screen.getByText('Card only (code 1)')).toBeVisible()
  expect(screen.getByText('Unrecognized code 10')).toBeVisible()
  expect(screen.getByText(/not a recognized choice/)).toBeVisible()
})
