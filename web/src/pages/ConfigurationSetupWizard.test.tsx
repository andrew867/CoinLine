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
    family: { familyLabel: 'Multi-pay', documented: true, tables: [{ name: 'Carrier', required: true, status: 'missing' }],
      operatingMode: { expectedCode: 2, expectedLabel: 'Multi-pay', configuredCode: 3, configuredLabel: 'Coin only', status: 'differs',
        message: 'Review whether the payment-mode difference is intentional for this phone.' } },
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
  expect(screen.getByText('Multi-pay (code 2)')).toBeVisible()
  expect(screen.getByText('Coin only (code 3)')).toBeVisible()
  expect(screen.getByText(/payment-mode difference is intentional/)).toBeVisible()
  expect(screen.getByRole('button', { name: 'Create reviewed draft' })).toBeEnabled()
  expect(apiPostRaw).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByRole('button', { name: 'Create reviewed draft' }))
  expect(await screen.findByText('Draft created')).toBeVisible()
  await waitFor(() => expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/base/setup', expect.objectContaining({
    name: 'New configuration', paymentMode: 3, pricing: 'flat', tariffIndex: 7, firstChargeCents: 25, previewToken: 'reviewed' })))
  expect(done).toHaveBeenCalledTimes(1)
  expect(screen.getByRole('link', { name: 'Review the selected terminal tariff' })).toHaveAttribute('href', '/table-sets/draft?record=73')
}, 15000)

it('offers a targeted default starter draft, not ordinary drafts or untargeted examples', async () => {
  vi.mocked(apiGet).mockResolvedValue([{ id: 'mtr19-multipay', release: 'MTR 1.9', variant: 'Multi-pay', maximizerTypeNumbers: [28] }])
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ previewToken: 'starter-preview', canCreate: true,
    sourceIsStarterDraft: true, targetFirmware: 'MTR 1.9', changedNumbers: [], recordCount: 13,
    family: { familyLabel: 'Multi-pay', documented: true, tables: [{ name: 'Management access', required: true, status: 'missing' }] },
    provisioning: { operatingType: 'Multi-pay', acceptedCoinSlots: 1, configuredCardRanges: 0, findings: [] }, findings: [] }))
  render(<MemoryRouter><ConfigurationSetupWizard onCreated={vi.fn()} sources={[
    { id: 'starter', name: 'Demo starter', status: 0, isDefault: true, targetFirmwareVersionId: 'version', targetFirmware: 'MTR 1.9' },
    { id: 'reference', name: 'Reference draft', status: 0, isDefault: false, targetFirmwareVersionId: 'version', targetFirmware: 'MTR 1.9' },
    { id: 'untargeted', name: 'Untargeted default', status: 0, isDefault: true, targetFirmware: null },
  ]} /></MemoryRouter>)
  await screen.findByRole('option', { name: 'MTR 1.9 Multi-pay (28)' })
  expect(screen.getByRole('option', { name: /Demo starter.*Default starter draft/ })).toBeVisible()
  expect(screen.queryByRole('option', { name: /Reference draft|Untargeted default/ })).toBeNull()
  fireEvent.change(screen.getByLabelText('Starting configuration'), { target: { value: 'starter' } })
  expect(screen.getByText(/This starter is an unpublished example/)).toBeVisible()
  fireEvent.change(screen.getByLabelText('Terminal family'), { target: { value: 'mtr19-multipay' } })
  fireEvent.change(screen.getByLabelText('Draft configuration name'), { target: { value: 'Site configuration' } })
  fireEvent.click(screen.getByRole('button', { name: 'Next: payment and pricing' }))
  fireEvent.click(screen.getByRole('button', { name: 'Review draft' }))
  expect(await screen.findByText(/This is a copy of an unpublished starter/)).toBeVisible()
  expect(screen.getByText('Management access: missing')).toBeVisible()
  expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/starter/setup/preview', expect.objectContaining({ pricing: 'keep' }))
})

it('defaults a graphical family to graphical idle rotation and exposes text and off as distinct choices', async () => {
  vi.mocked(apiGet).mockResolvedValue([{ id: 'mtr20-multipay-graphical', release: 'MTR 2.0', variant: 'Graphical Multi-pay', maximizerTypeNumbers: [40] }])
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ previewToken: 'graphical', canCreate: true, targetFirmware: 'Graphical', changedNumbers: [26], recordCount: 35,
    family: { familyLabel: 'Graphical Multi-pay', documented: true, tables: [] },
    provisioning: { operatingType: 'Multi-pay', acceptedCoinSlots: 2, configuredCardRanges: 1, onHookAdvertising: false, graphicalOnHookAdvertising: true,
      visibleOnHookTextMessages: 2, findings: [], collectionConfiguration: {
        statisticsRecordStatus:'missing', callInRecordStatus:'configured', statisticsIntervalDays:null, statisticsUploadThreshold:null,
        carrierStatisticsEnabled:null, optionalCdrMode:null, optionalCdrModeMask:null, optionalCdrUploadThreshold:null,
        optionalWindowStart:null, optionalWindowDays:null, optionalWindowHours:null, normalCdrUploadThreshold:7,
        findings:[{tableNumber:34,message:'MT34 is not included.'}],
      } }, findings: [] }))
  render(<MemoryRouter><ConfigurationSetupWizard onCreated={vi.fn()} sources={[
    { id: 'graphical', name: 'Graphical starter', status: 0, isDefault: true, targetFirmwareVersionId: 'version', targetFirmware: 'Graphical' },
  ]} /></MemoryRouter>)
  await screen.findByRole('option', { name: 'MTR 2.0 Graphical Multi-pay (40)' })
  fireEvent.change(screen.getByLabelText('Starting configuration'), { target: { value: 'graphical' } })
  fireEvent.change(screen.getByLabelText('Terminal family'), { target: { value: 'mtr20-multipay-graphical' } })
  fireEvent.change(screen.getByLabelText('Draft configuration name'), { target: { value: 'Graphical site' } })
  fireEvent.click(screen.getByRole('button', { name: 'Next: payment and pricing' }))
  expect(screen.getByLabelText('Idle advertising mode')).toHaveValue('graphical')
  expect(screen.getByRole('option', { name: 'Two-line text rotation' })).toBeVisible()
  expect(screen.getByRole('option', { name: 'No idle advertisements' })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Review draft' }))
  expect(await screen.findByText('Idle graphical rotation (MT131)')).toBeVisible()
  expect(screen.getByText('Idle text rotation (MT26)')).toBeVisible()
  expect(screen.getByRole('region',{name:'Collection configuration review'})).toBeVisible()
  expect(screen.getByText(/MT34 is not included/)).toBeVisible()
  expect(screen.getByText('Normal CDR count threshold (outside optional window)').nextElementSibling).toHaveTextContent('7')
  expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/graphical/setup/preview', expect.objectContaining({ idleAdvertisingMode: 'graphical' }))
})
