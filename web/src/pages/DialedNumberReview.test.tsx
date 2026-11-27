import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { DialedNumberReview } from './DialedNumberReview'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('shows independent screening, tariffs and missing records, and discards stale results', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response(JSON.stringify({ setRevision: 2, profile: 'Later packed', review: {
    supported: true, reason: null, findings: [{ tableNumber: 92, entry: 'Screening rule 1', interpretation: 'Free flag clear', rateIndex: 1 },
      { tableNumber: 150, entry: 'Area code', interpretation: 'Code 1: reject', rateIndex: null }],
    tariffs: [{ index: 1, classification: 'Fixed local charge', initialSeconds: -1, initialCents: 25, additionalSeconds: 0, additionalCents: 0 }],
    reviewedRecords: [92, 150], missingRecords: [62], unreviewedRecords: [26], warnings: ['Multiple screening candidates match.'],
  } })))
  const view = render(<DialedNumberReview setId="test-set" recordRevision="92:1" />)
  fireEvent.click(screen.getByText('Review a dialed number across saved tables'))
  const input = screen.getByLabelText('Test dialed digits')
  fireEvent.change(input, { target: { value: '3335550123' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review saved configuration' }))
  expect(await screen.findByText('Fixed local charge')).toBeVisible()
  expect(screen.getByText('25 cents / unlimited')).toBeVisible()
  expect(screen.getByText('Code 1: reject')).toBeVisible()
  expect(screen.getByText(/Missing: MT62/)).toBeVisible()
  expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/test-set/dialed-number-review', expect.objectContaining({ method: 'POST', body: '{"digits":"3335550123"}' }))
  view.rerender(<DialedNumberReview setId="test-set" recordRevision="92:2" />)
  await waitFor(() => expect(screen.queryByText('Fixed local charge')).not.toBeInTheDocument())
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response('{}', { status: 500 }))
  fireEvent.click(screen.getByRole('button', { name: 'Review saved configuration' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('HTTP 500')
  fireEvent.change(input, { target: { value: '2374' } })
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})
