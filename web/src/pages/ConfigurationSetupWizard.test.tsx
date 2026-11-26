import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { ConfigurationSetupWizard } from './ConfigurationSetupWizard'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('reviews the actual payment and flat rate choices before creating only a draft', async () => {
  vi.mocked(apiGet).mockResolvedValue([{ id: 'mtr19-multipay', release: 'MTR 1.9', variant: 'Multi-pay', maximizerTypeNumbers: [28] }])
  const review = { previewToken: 'reviewed', canCreate: true, targetFirmware: 'MTR 1.9', changedNumbers: [26, 73], recordCount: 12,
    family: { familyLabel: 'Multi-pay', documented: true, tables: [{ name: 'Carrier', required: true, status: 'missing' }] },
    provisioning: { operatingType: 'Coin only', acceptedCoinSlots: 1, configuredCardRanges: 0, findings: [] }, findings: [] }
  vi.mocked(apiPostRaw).mockImplementation(async path => String(path).endsWith('/preview') ? Response.json(review) : Response.json({ id: 'draft' }, { status: 201 }))
  const done = vi.fn()
  render(<MemoryRouter><ConfigurationSetupWizard onCreated={done} sources={[
    { id: 'base', name: 'Published baseline', status: 1, targetFirmwareVersionId: 'version', targetFirmware: 'MTR 1.9' },
    { id: 'draft-source', name: 'Unpublished', status: 0, targetFirmwareVersionId: 'version', targetFirmware: 'MTR 1.9' },
  ]} /></MemoryRouter>)
  await screen.findByRole('option', { name: 'MTR 1.9 Multi-pay (28)' })
  expect(screen.queryByRole('option', { name: /Unpublished/ })).toBeNull()
  fireEvent.change(screen.getByLabelText('Starting configuration'), { target: { value: 'base' } })
  fireEvent.change(screen.getByLabelText('Terminal family'), { target: { value: 'mtr19-multipay' } })
  fireEvent.change(screen.getByLabelText('Draft configuration name'), { target: { value: 'New configuration' } })
  fireEvent.click(screen.getByRole('button', { name: 'Next: payment and pricing' }))
  fireEvent.change(screen.getByLabelText('Which payments should the terminal accept?'), { target: { value: '3' } })
  fireEvent.change(screen.getByLabelText('How should the local call be priced?'), { target: { value: 'flat' } })
  expect(screen.getByRole('button', { name: 'Review draft' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('MT73 rate row used by the local route (0 to 127)'), { target: { value: '7' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review draft' }))
  expect(await screen.findByText('MT26, MT73')).toBeVisible()
  expect(screen.getByText('Carrier: missing')).toBeVisible()
  expect(apiPostRaw).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByRole('button', { name: 'Create reviewed draft' }))
  expect(await screen.findByText('Draft created')).toBeVisible()
  await waitFor(() => expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/base/setup', expect.objectContaining({
    name: 'New configuration', paymentMode: 3, pricing: 'flat', tariffIndex: 7, firstChargeCents: 25, previewToken: 'reviewed' })))
  expect(done).toHaveBeenCalledTimes(1)
  expect(screen.getByRole('link', { name: 'Review the selected terminal tariff' })).toHaveAttribute('href', '/table-sets/draft?record=73')
}, 15000)
