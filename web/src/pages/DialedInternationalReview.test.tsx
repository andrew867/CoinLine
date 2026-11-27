import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { DialedNumberReview } from './DialedNumberReview'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('reviews explicit international destination digits and separates direct, fallback and host settings', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ setRevision: 7, profile: 'Later packed', review: {
    supported: true, reason: null, findings: [
      { tableNumber: 26, entry: 'International set rating', interpretation: 'Enabled', rateIndex: null },
      { tableNumber: 151, entry: 'Destination 1: prefix 44', interpretation: 'Code 2: MT73 index 30', rateIndex: 30 },
      { tableNumber: 151, entry: 'Destination 2: prefix 441', interpretation: 'Code 100: use fallback; Fallback default code 1: MT73 index 29 for a destination using fallback', rateIndex: 29 },
      { tableNumber: 151, entry: 'Unmatched-destination fallback (separate setting)', interpretation: 'Code 1: reject', rateIndex: null },
    ], tariffs: [{ index: 29, classification: 'International toll', initialSeconds: 60, initialCents: 50, additionalSeconds: 60, additionalCents: 25 },
      { index: 30, classification: 'International toll', initialSeconds: 30, initialCents: 10, additionalSeconds: 60, additionalCents: 5 }],
    reviewedRecords: [26, 73, 151], missingRecords: [], unreviewedRecords: [], warnings: ['Multiple international destination prefixes match.'],
  } }))
  render(<MemoryRouter><DialedNumberReview setId="fixture-set" recordRevision="151:1" /></MemoryRouter>)
  fireEvent.click(screen.getByText('Review a dialed number across saved tables'))
  fireEvent.change(screen.getByLabelText('Test dialed digits'), { target: { value: '011441234567' } })
  const destination = screen.getByLabelText('International destination after access prefix (optional)')
  fireEvent.change(destination, { target: { value: '+441234567' } })
  expect(screen.getByRole('button', { name: 'Review saved configuration' })).toBeDisabled()
  expect(apiFetch).not.toHaveBeenCalled()
  fireEvent.change(destination, { target: { value: '441234567' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review saved configuration' }))
  const candidates = await screen.findByRole('table', { name: 'Configured candidates and payment settings' })
  expect(within(candidates).getByText('Code 2: MT73 index 30')).toBeVisible()
  expect(within(candidates).getByText(/Fallback default code 1: MT73 index 29/)).toBeVisible()
  expect(within(candidates).getByText('Code 1: reject')).toBeVisible()
  expect(screen.getByText('Multiple international destination prefixes match.')).toBeVisible()
  expect(screen.getByText('50 cents / 60 seconds')).toBeVisible()
  expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/fixture-set/dialed-number-review', expect.objectContaining({
    method: 'POST', body: JSON.stringify({ digits: '011441234567', internationalDigits: '441234567' }) }))
  fireEvent.change(destination, { target: { value: '49' } })
  expect(screen.queryByRole('table', { name: 'Configured candidates and payment settings' })).not.toBeInTheDocument()
})
