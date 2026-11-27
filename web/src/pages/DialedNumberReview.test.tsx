import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { apiFetch, apiGet, apiPost } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { DialedNumberReview } from './DialedNumberReview'
import { RatingQuoteTool } from './RatingQuoteTool'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

it('shows independent screening, tariffs and missing records, and discards stale results', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response(JSON.stringify({ setRevision: 2, profile: 'Later packed', review: {
    supported: true, reason: null, findings: [{ tableNumber: 92, entry: 'Screening rule 1', interpretation: 'Free flag clear', rateIndex: 1 },
      { tableNumber: 150, entry: 'Area code', interpretation: 'Code 1: reject', rateIndex: null }],
    tariffs: [{ index: 1, classification: 'Fixed local charge', initialSeconds: -1, initialCents: 25, additionalSeconds: 0, additionalCents: 0 }],
    reviewedRecords: [92, 150], missingRecords: [62], unreviewedRecords: [26], warnings: ['Multiple screening candidates match.'],
  } })))
  const view = render(<MemoryRouter><DialedNumberReview setId="test-set" recordRevision="92:1" /></MemoryRouter>)
  fireEvent.click(screen.getByText('Review a dialed number across saved tables'))
  const input = screen.getByLabelText('Test dialed digits')
  fireEvent.change(input, { target: { value: '3335550123' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review saved configuration' }))
  expect(await screen.findByText('Fixed local charge')).toBeVisible()
  expect(screen.getByText('25 cents / unlimited')).toBeVisible()
  expect(screen.getByText('Code 1: reject')).toBeVisible()
  expect(screen.getByText(/Missing: MT62/)).toBeVisible()
  expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/test-set/dialed-number-review', expect.objectContaining({ method: 'POST', body: '{"digits":"3335550123"}' }))
  view.rerender(<MemoryRouter><DialedNumberReview setId="test-set" recordRevision="92:2" /></MemoryRouter>)
  await waitFor(() => expect(screen.queryByText('Fixed local charge')).not.toBeInTheDocument())
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response('{}', { status: 500 }))
  fireEvent.click(screen.getByRole('button', { name: 'Review saved configuration' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('HTTP 500')
  fireEvent.change(input, { target: { value: '2374' } })
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('carries exact digits into a separate explicitly selected published host quote without placing a call', async () => {
  consoleSession.save('fixture-token', '', undefined, 'Admin')
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ setRevision: 1, profile: 'Later packed', review: {
    supported: true, reason: null, findings: [], tariffs: [], reviewedRecords: [], missingRecords: [], unreviewedRecords: [], warnings: [],
  } }))
  vi.mocked(apiGet).mockResolvedValue([
    { id: 'plan-a', name: 'Local default', mode: 1, customerId: null, publishedVersionId: 'a-v1' },
    { id: 'plan-b', name: 'Regional plan', mode: 2, customerId: 'customer-b', publishedVersionId: 'b-v1' },
    { id: 'draft', name: 'Unpublished plan', mode: 1, customerId: null, publishedVersionId: null },
  ])
  vi.mocked(apiPost).mockResolvedValue({ amountUsd: .25, allowed: true, blocked: false, freeCall: false, emergency: false, pricing: null })
  render(<MemoryRouter initialEntries={['/configuration']}><Routes>
    <Route path="/configuration" element={<DialedNumberReview setId="test-set" recordRevision="92:1" />} />
    <Route path="/rating-quote" element={<RatingQuoteTool />} />
  </Routes></MemoryRouter>)
  fireEvent.click(screen.getByText('Review a dialed number across saved tables'))
  fireEvent.change(screen.getByLabelText('Test dialed digits'), { target: { value: '13335550123' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review saved configuration' }))
  const link = await screen.findByRole('link', { name: 'Compare a published host quote for these digits' })
  expect(link).toHaveAttribute('href', '/rating-quote')
  fireEvent.click(link)
  expect(await screen.findByLabelText('Dialed digits')).toHaveValue('13335550123')
  expect(screen.getByRole('button', { name: 'Quote' })).toBeDisabled()
  expect(screen.queryByRole('option', { name: 'Unpublished plan' })).not.toBeInTheDocument()
  expect(apiPost).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('Rate plan'), { target: { value: 'plan-b' } })
  expect(screen.getByLabelText('Rating mode')).toHaveValue('2')
  expect(screen.getByTestId('rating-quote-customer-id')).toHaveValue('customer-b')
  fireEvent.click(screen.getByRole('button', { name: 'Quote' }))
  expect(await screen.findByRole('heading', { name: 'Estimated charge: $0.25' })).toBeVisible()
  expect(apiPost).toHaveBeenCalledWith('/api/rating/quote', { dialedDigits: '13335550123', mode: 2, ratePlanId: 'plan-b', customerId: 'customer-b', assumedDurationMinutes: 1 })
  fireEvent.change(screen.getByLabelText('Assumed minutes'), { target: { value: '2' } })
  expect(screen.queryByRole('heading', { name: 'Estimated charge: $0.25' })).not.toBeInTheDocument()
})

it('keeps the host quote link unavailable without the necessary console permissions', async () => {
  consoleSession.save('fixture-token', '', undefined, 'Operator', { 'table-records': 1 })
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ setRevision: 1, profile: 'Later packed', review: {
    supported: true, reason: null, findings: [], tariffs: [], reviewedRecords: [], missingRecords: [], unreviewedRecords: [], warnings: [],
  } }))
  render(<MemoryRouter><DialedNumberReview setId="test-set" recordRevision="92:1" /></MemoryRouter>)
  fireEvent.click(screen.getByText('Review a dialed number across saved tables'))
  fireEvent.change(screen.getByLabelText('Test dialed digits'), { target: { value: '2374' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review saved configuration' }))
  expect(await screen.findByText('Saved revision 1 · Later packed')).toBeVisible()
  expect(screen.queryByRole('link', { name: /Compare a published host quote/ })).not.toBeInTheDocument()
  expect(apiGet).not.toHaveBeenCalled()
})
