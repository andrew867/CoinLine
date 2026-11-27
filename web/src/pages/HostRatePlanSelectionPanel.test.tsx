import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { HostRatePlanSelectionPanel } from './HostRatePlanSelectionPanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

const options = [{ id: 'legacy', name: 'Previous default', customerId: 'customer', publishedVersionId: 'v1' },
  { id: 'regional', name: 'Regional tariff', customerId: null, publishedVersionId: 'v2' }]
const initial = { selectionId: null, revision: null, selectedPlanId: null, inheritedPhones: 2, options,
  effective: { customerId: 'customer', ratePlanId: 'legacy', publishedVersionId: 'v1', name: 'Previous default', source: 'legacy' } }
const chosen = { ...initial, selectionId: 'selection', revision: 1, selectedPlanId: 'regional',
  effective: { ...initial.effective, ratePlanId: 'regional', publishedVersionId: 'v2', name: 'Regional tariff', source: 'customer' } }

it('reviews and explicitly selects a shared plan for inheriting phones', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce(initial).mockResolvedValueOnce(chosen)
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response(null, { status: 204 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><HostRatePlanSelectionPanel scope="customers" id="customer" /></MemoryRouter>)
  expect(await screen.findByText(/Existing automatic customer default/)).toBeVisible()
  expect(screen.getByText('2 active phones inherit this default. Explicit phone selections are retained.')).toBeVisible()
  fireEvent.change(screen.getByLabelText('Customer default host rate plan'), { target: { value: 'regional' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save host rate plan selection' }))
  await waitFor(() => expect(apiFetch).toHaveBeenCalledWith('/api/rate-plans/selection/customers/customer', expect.objectContaining({
    method: 'PUT', body: JSON.stringify({ ratePlanId: 'regional', expectedSelectionId: null, expectedRevision: null, confirm: true }) })))
  expect(await screen.findByText('Host rate plan selection saved. No terminal table download was requested.')).toBeVisible()
  expect(screen.getByRole('link', { name: 'Regional tariff' })).toHaveAttribute('href', '/rate-plans/regional')
})

it('can explicitly clear an unavailable phone override and exposes a stale conflict', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ ...chosen, selectedPlanId: 'unavailable',
    effective: { ...initial.effective, ratePlanId: null, publishedVersionId: null, name: null, source: 'terminal-unavailable' } })
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ error: 'Host rate plan selection changed. Reload and review again.' }, { status: 409 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><HostRatePlanSelectionPanel scope="terminals" id="phone" /></MemoryRouter>)
  expect(await screen.findByText(/Phone selection unavailable/)).toBeVisible()
  expect(screen.getByRole('option', { name: 'Unavailable selected plan' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Phone host rate plan'), { target: { value: '' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save host rate plan selection' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Host rate plan selection changed.')
  expect(apiFetch).toHaveBeenCalledWith('/api/rate-plans/selection/terminals/phone', expect.objectContaining({
    body: JSON.stringify({ ratePlanId: null, expectedSelectionId: 'selection', expectedRevision: 1, confirm: true }) }))
})
