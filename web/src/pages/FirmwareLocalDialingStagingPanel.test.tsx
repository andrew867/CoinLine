import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

it('shows exact local rate mappings, area replacements and fresh-preview enforcement before saving an unassigned draft', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'Later layout', tableProfileId: 'later' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target template', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'Earlier layout', targetFirmwareLabel: 'Later layout', sourceSetId: 'source',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], records: [
        { tableNumber: 73, sourceBytes: 1192, targetExpectedBytes: 1192, candidateBytes: 1192, action: 'review-native-layout' },
        { tableNumber: 101, sourceBytes: 403, targetExpectedBytes: null, candidateBytes: null, action: 'retire-from-target' },
        { tableNumber: 136, sourceBytes: null, targetExpectedBytes: 203, candidateBytes: 203, action: 'candidate-addition' }] })
  const rate = { index: 5, classification: 2, initialPeriod: -1, initialCharge: 25, additionalPeriod: 0, additionalCharge: 0 }
  const preview = { recordCount: 5, canCreate: true, rates: [{ sourceIndex: 5, exchangeCount: 800, targetOptions: [1, 2], targetIndex: 1, sourceTariff: rate }],
    targetTariffs: Array.from({ length: 4 }, (_, index) => ({ ...rate, index })), areas: [{ sourceRecord: 101, areaCode: '205', targetOptions: [136, 138], targetRecord: 136,
      replacesExistingArea: false, ratesMatched: true, changedExchangeCodes: 800, changedRateTuples: 800 }], changedRecords: [136], preservedTargetRecords: [137, 138], findings: [], previewToken: 'first' }
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(preview)).mockResolvedValueOnce(Response.json({ ...preview,
    rates: [{ ...preview.rates[0], targetIndex: 2 }], areas: [{ ...preview.areas[0], targetRecord: 138 }],
    changedRecords: [138], preservedTargetRecords: [136, 137], previewToken: 'reviewed' }))
    .mockResolvedValueOnce(Response.json({ id: 'local-draft', changedRecords: [138] }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward local dialing' })
  fireEvent.change(within(region).getByLabelText('New local dialing draft name'), { target: { value: 'Local draft' } })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview local dialing carry-forward' }))
  expect(await within(region).findByText('1 source area, 1 source tariff index.')).toBeVisible()
  expect(within(region).getByText('Fixed local charge: 25¢ / Unlimited, then 0¢ / 0 s')).toBeVisible()
  expect(within(region).getByText('205')).toBeVisible(); expect(within(region).getByText('800 / 800')).toBeVisible()
  fireEvent.change(within(region).getByLabelText('Local target rate for source 5'), { target: { value: '2' } })
  fireEvent.change(within(region).getByLabelText('Target segment for MT101'), { target: { value: '138' } })
  expect(within(region).queryByRole('button', { name: 'Create local dialing target draft' })).not.toBeInTheDocument()
  fireEvent.click(within(region).getByRole('button', { name: 'Preview local dialing carry-forward' }))
  expect(await within(region).findByRole('button', { name: 'Create local dialing target draft' })).toBeEnabled()
  fireEvent.click(within(region).getByRole('button', { name: 'Create local dialing target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open local dialing draft' })).toHaveAttribute('href', '/table-sets/local-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/firmware/transition-local-dialing', expect.objectContaining({ rateChoices: { '5': 2 }, areaChoices: { '101': 138 }, previewToken: 'reviewed' }))
  fireEvent.click(within(region).getByRole('button', { name: 'Use as target candidate for further review' }))
  expect(screen.getByLabelText('Prepared target configuration')).toHaveValue('local-draft')
})
