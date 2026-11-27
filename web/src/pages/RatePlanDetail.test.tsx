import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { apiDownload, apiFetch, apiGet, apiPost, apiPut } from '../api/client'
import { RatePlanDetail } from './RatePlanDetail'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn(), apiPut: vi.fn(), apiDelete: vi.fn(), apiDownload: vi.fn() }))

afterEach(() => { cleanup(); vi.resetAllMocks() })

it('authors flat host rules and separate first and following minute tariff prices', async () => {
  vi.mocked(apiGet).mockResolvedValue({ id: 'plan', name: 'Local', mode: 1, customerId: null, publishedVersionId: null,
    versions: [{ id: 'version', versionNumber: 1, status: 0, publishedAtUtc: null }] })
  const version = { id: 'version', versionNumber: 1, status: 0, rules: [{ id: 'rule', priority: 1, matchKind: 0, pattern: '202', outcome: 0, ratePerMinuteUsd: .25, expression: '{}' }],
    tariffs: [{ id: 'tariff', name: 'Local', ratePerMinuteUsd: .25, notes: '' }], destinationPrefixes: [], timeBands: [] }
  vi.mocked(apiFetch).mockImplementation(async () => Response.json(version))
  vi.mocked(apiPut).mockResolvedValue(undefined)
  vi.mocked(apiPost).mockResolvedValue({})
  render(<MemoryRouter initialEntries={['/rate-plans/plan']}><Routes><Route path="/rate-plans/:id" element={<RatePlanDetail />} /></Routes></MemoryRouter>)
  fireEvent.click(await screen.findByRole('button', { name: 'v1' }))
  await screen.findByRole('heading', { name: /Rules \(version/ })
  vi.mocked(apiDownload).mockResolvedValue(undefined)
  fireEvent.click(screen.getByRole('button', { name: 'Export selected version as JSON' }))
  expect(apiDownload).toHaveBeenCalledWith('/api/rate-plans/packages/plan/version', 'coinline-rate-plan.json')
  const pricing = screen.getAllByLabelText('Pricing structure')
  expect(pricing[0]).toHaveValue('0')
  fireEvent.change(pricing[0], { target: { value: '1' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save rule pricing' }))
  await waitFor(() => expect(apiPut).toHaveBeenCalledWith('/api/rate-rules/rule', expect.objectContaining({
    pricingMode: 1, initialChargeUsd: .25, ratePerMinuteUsd: 0, pattern: '202' })))

  const tariffForm = screen.getByRole('button', { name: 'Add tariff' }).closest('form')!
  fireEvent.change(within(tariffForm).getByLabelText('Pricing structure'), { target: { value: '3' } })
  fireEvent.change(within(tariffForm).getByLabelText('First minute including connection (USD)'), { target: { value: '.25' } })
  fireEvent.change(within(tariffForm).getByLabelText('Each following minute (USD)'), { target: { value: '.10' } })
  expect(within(tariffForm).getByText('$0.25 first minute, then $0.10/minute')).toBeVisible()
  fireEvent.click(within(tariffForm).getByRole('button', { name: 'Add tariff' }))
  await waitFor(() => expect(apiPost).toHaveBeenCalledWith('/api/rate-plans/plan/versions/version/tariffs', expect.objectContaining({
    pricingMode: 3, initialChargeUsd: .25, ratePerMinuteUsd: .1 })))
}, 15000)
