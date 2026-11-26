import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

it('reviews grouped exact rates, excludes protected positions and requires a fresh mapping before creating the draft', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'MTR 2.11', tableProfileId: 'later' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target template', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'MTR 2.0', targetFirmwareLabel: 'MTR 2.11', sourceSetId: 'source',
      sourceProfileId: 'mtr20-early-multipay-200-single-compressed', targetProfileId: 'mtr211-multipay-180-double-compressed',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], records: [
        { tableNumber: 73, sourceBytes: 1192, targetExpectedBytes: 1192, candidateBytes: 1192,
          action: 'review-native-layout', detail: 'Review tariff rows.', targetAcknowledged: true, requiresLayoutReview: true },
        { tableNumber: 92, sourceBytes: null, targetExpectedBytes: 3061, candidateBytes: null,
          action: 'available-unassigned', detail: 'Prepare screening.', targetAcknowledged: true, requiresLayoutReview: false }] })
  const initial = { sourceRows: 128, targetRows: 128, recordCount: 12, canCreate: true, changedPositions: [7], groups: [
    { sourceIndex: 0, sourceIndexes: [0, 2], classification: 2, initialPeriod: -1, initialCharge: 25,
      additionalPeriod: 0, additionalCharge: 0, targetOptions: [7, 8, 9], targetIndex: 7, action: 'allocate', replacedClassification: 4 },
    { sourceIndex: 1, sourceIndexes: [1, ...Array.from({ length: 125 }, (_, i) => i + 3)], classification: 4,
      initialPeriod: 0, initialCharge: 0, additionalPeriod: 0, additionalCharge: 0, targetOptions: [0, 1, 2, 3], targetIndex: 0,
      action: 'reuse', replacedClassification: null }], candidateReview: { protectedPositions: [{ index: 4, recordNumbers: [92] }],
        reviewedRecords: [73, 92], missingReferenceRecords: [55], unreviewedRecords: [] }, findings: [], previewToken: 'suggested-token' }
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(initial)).mockResolvedValueOnce(Response.json({ ...initial,
    changedPositions: [9], groups: [{ ...initial.groups[0], targetIndex: 9 }, initial.groups[1]], previewToken: 'reviewed-token' }))
    .mockResolvedValueOnce(Response.json({ id: 'tariff-draft', changedPositions: [9] }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward terminal tariffs' })
  fireEvent.change(within(region).getByLabelText('New tariff draft name'), { target: { value: 'Prepared tariff draft' } })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview tariff carry-forward' }))
  expect(await within(region).findByText('128 source rows, 2 distinct tariff definitions.')).toBeVisible()
  const fixed = within(region).getByRole('row', { name: /Fixed local charge/ })
  expect(within(fixed).getByText('25¢ / Unlimited')).toBeVisible()
  expect(within(fixed).getByText('0, 2')).toBeVisible()
  expect(within(fixed).getByText('Allocate empty Invalid number')).toBeVisible()
  const selected = within(region).getByLabelText('Target tariff for source 0')
  expect(selected).toHaveValue('7'); expect(within(selected).queryByRole('option', { name: '4' })).not.toBeInTheDocument()
  fireEvent.click(within(region).getByText('Protected target indexes (1)'))
  expect(within(region).getByText('Rate 4: MT92')).toBeVisible()
  fireEvent.change(selected, { target: { value: '9' } })
  expect(within(region).getByText('Preview again to confirm your changes.')).toBeVisible()
  expect(within(region).queryByRole('button', { name: 'Create tariff target draft' })).not.toBeInTheDocument()
  fireEvent.click(within(region).getByRole('button', { name: 'Preview tariff carry-forward' }))
  expect(await within(region).findByRole('button', { name: 'Create tariff target draft' })).toBeEnabled()
  fireEvent.click(within(region).getByRole('button', { name: 'Create tariff target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open tariff draft' })).toHaveAttribute('href', '/table-sets/tariff-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/firmware/transition-tariffs', expect.objectContaining({ choices: { '0': 9 }, previewToken: 'reviewed-token' }))
  fireEvent.click(within(region).getByRole('button', { name: 'Use as target candidate for further review' }))
  expect(screen.getByLabelText('Prepared target configuration')).toHaveValue('tariff-draft')
})
