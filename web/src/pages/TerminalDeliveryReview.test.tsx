import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { TerminalDeliveryReview } from './TerminalDeliveryReview'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('separates changed bytes, an unchanged dependency, and full fallback after an uncertain delivery', async () => {
  const base = { available: true, tableSetName: 'Fixture set', baselineId: 'fixture-baseline' }
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ ...base, reason: 'changed_records', scope: 'partial', records: [
    { number: 26, name: 'Features', revision: 1, comparison: 'same', selectedForRefresh: true },
    { number: 30, name: 'Timers', revision: 2, comparison: 'different', selectedForRefresh: true },
    { number: 50, name: 'Coin', revision: 1, comparison: 'same', selectedForRefresh: false },
  ] })).mockResolvedValueOnce(Response.json({ ...base, reason: 'uncertain_delivery_history', scope: 'full', records: [
    { number: 26, name: 'Features', revision: 1, comparison: 'uncertain', selectedForRefresh: true },
  ] }))
  render(<MemoryRouter><TerminalDeliveryReview terminalId="fixture-phone" assignmentKey="set:version" /></MemoryRouter>)
  expect(apiFetch).not.toHaveBeenCalled()
  const summary = screen.getByText('Review acknowledged configuration and ordinary refresh')
  const details = summary.closest('details')!; details.open = true; fireEvent(details, new Event('toggle'))
  expect(await screen.findByText(/Ordinary refresh preview: selected records/)).toBeVisible()
  const features = screen.getByRole('row', { name: /MT26/ })
  expect(within(features).getByText('Matching acknowledged bytes')).toBeVisible()
  expect(within(features).getByText('Selected')).toBeVisible()
  expect(within(screen.getByRole('row', { name: /MT30/ })).getByText('Different from acknowledged bytes')).toBeVisible()
  expect(within(screen.getByRole('row', { name: /MT50/ })).getByText('Not selected')).toBeVisible()
  expect(screen.getByRole('link', { name: 'View download evidence' })).toHaveAttribute('href', '/downloads/fixture-baseline')
  fireEvent.click(screen.getByRole('button', { name: 'Refresh delivery evidence' }))
  expect(await screen.findByText(/Ordinary refresh preview: full set/)).toBeVisible()
  expect(screen.getByText('Acknowledgement comparison uncertain')).toBeVisible()
  expect(screen.queryByText('Different from acknowledged bytes')).not.toBeInTheDocument()
  expect(apiFetch).toHaveBeenCalledTimes(2)
  expect(apiFetch).toHaveBeenCalledWith('/api/terminals/fixture-phone/configuration-delivery-review', expect.objectContaining({ signal: expect.any(AbortSignal) }))
})
