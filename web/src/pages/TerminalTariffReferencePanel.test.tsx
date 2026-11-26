import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { TerminalTariffReferencePanel } from './TerminalTariffReferencePanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const response = { rateIndex: 7, setRevision: 3, profile: 'Fixture verified profile', review: { supported: true,
  reason: null, groups: [{ recordNumber: 92, kind: 'Screening rules', use: 'configured', count: 1, entries: ['Screening rule 1: 3335550120'] },
    { recordNumber: 101, kind: 'Local exchanges in 205', use: 'configured', count: 3, entries: ['200 to 202'] },
    { recordNumber: 93, kind: 'Replacement tariffs', use: 'conditional', count: 1, entries: ['Discount 1: replacement tariff'] }],
  reviewedRecords: [92, 93, 101], missingRecords: [55], unreviewedRecords: [102], warnings: ['An entry needs manual review.'] } }

it('shows selected tariff references and distinguishes missing, unreviewed and conditional records', async () => {
  vi.mocked(apiFetch).mockResolvedValue(Response.json(response))
  render(<TerminalTariffReferencePanel setId="fixture" rateIndex={7} />)
  expect(apiFetch).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Review references' }))
  const review = await screen.findByRole('region', { name: 'Saved tariff reference review' })
  expect(within(review).getByText(/Saved configuration revision 3/)).toBeVisible()
  for (const name of [/MT92 · Screening rules/, /MT101 · Local exchanges in 205/, /MT93 · Replacement tariffs/]) {
    const details = within(review).getByText(name).closest('details')!
    details.open = true
  }
  expect(within(review).getByText('Screening rule 1: 3335550120')).toBeVisible()
  expect(within(review).getByText('200 to 202')).toBeVisible()
  expect(within(review).getByText(/MT93 · Replacement tariffs.*conditional/)).toBeVisible()
  expect(within(review).getByText(/Not present here: MT55/)).toBeVisible()
  expect(within(review).getByRole('alert')).toHaveTextContent('MT102')
  expect(within(review).getByText('An entry needs manual review.')).toBeVisible()
  expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/fixture/tariffs/7/references', expect.objectContaining({ signal: expect.any(AbortSignal) }))
})

it('reports request errors, unknown layouts and an empty reviewed result without claiming a rate is unused', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response(null, { status: 500 }))
    .mockResolvedValueOnce(Response.json({ ...response, review: { ...response.review, supported: false, reason: 'Choose a verified profile.' } }))
    .mockResolvedValueOnce(Response.json({ ...response, review: { ...response.review, groups: [], unreviewedRecords: [] } }))
  render(<TerminalTariffReferencePanel setId="fixture" rateIndex={7} />)
  fireEvent.click(screen.getByRole('button', { name: 'Review references' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('HTTP 500')
  fireEvent.click(screen.getByRole('button', { name: 'Review references' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Choose a verified profile.')
  fireEvent.click(screen.getByRole('button', { name: 'Refresh references' }))
  expect(await screen.findByText(/This does not establish that the tariff is unused/)).toBeVisible()
})
