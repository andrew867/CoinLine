import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

it('requires a reviewed card mapping and saves only a separate target candidate', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'Later layout', tableProfileId: 'later' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target template', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'Earlier layout', targetFirmwareLabel: 'Later layout', sourceSetId: 'source',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], records: [
        { tableNumber: 22, sourceBytes: 661, action: 'retire-from-target' }, { tableNumber: 23, sourceBytes: 678, action: 'retire-from-target' },
        { tableNumber: 92, sourceBytes: 3401, targetExpectedBytes: 3061, candidateBytes: 3061, action: 'replace-native-layout' },
        { tableNumber: 134, targetExpectedBytes: 1153, candidateBytes: 1153, action: 'candidate-addition' },
        { tableNumber: 135, targetExpectedBytes: 1109, candidateBytes: 1109, action: 'candidate-addition' }] })
  const preview = { recordCount: 4, canCreate: false, changedSlots: [2], defaultSelectionsChanged: true,
    rows: [{ reference: 7, sourceSlot: 3, targetSlot: 2, changed: true }],
    mappings: [{ kind: 'card', sourceSlot: 1, options: [2, 4], targetSlot: null }],
    findings: [{ blocking: true, carrierReference: null, message: 'Choose exactly one matching target card position.' }], previewToken: 'first' }
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(preview)).mockResolvedValueOnce(Response.json({ ...preview,
    canCreate: true, findings: [], mappings: [{ ...preview.mappings[0], targetSlot: 4 }], previewToken: 'reviewed' }))
    .mockResolvedValueOnce(Response.json({ id: 'carrier-draft', changedSlots: [2] }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward carrier configuration' })
  fireEvent.change(within(region).getByLabelText('New carrier draft name'), { target: { value: 'Carrier draft' } })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview carrier carry-forward' }))
  expect(await within(region).findByText('1 source carriers mapped; 1 target carrier positions changed.')).toBeVisible()
  expect(within(region).getByText('Choose exactly one matching target card position.')).toBeVisible()
  expect(within(region).queryByRole('button', { name: 'Create carrier target draft' })).not.toBeInTheDocument()
  fireEvent.change(within(region).getByLabelText('Target card for source 1'), { target: { value: '4' } })
  expect(within(region).getByText('Preview again to confirm your changes.')).toBeVisible()
  fireEvent.click(within(region).getByRole('button', { name: 'Preview carrier carry-forward' }))
  fireEvent.click(await within(region).findByRole('button', { name: 'Create carrier target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open carrier draft' })).toHaveAttribute('href', '/table-sets/carrier-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/firmware/transition-carriers', expect.objectContaining({ cardChoices: { '1': 4 }, screeningChoices: {}, previewToken: 'reviewed' }))
  fireEvent.click(within(region).getByRole('button', { name: 'Use as target candidate for further review' }))
  expect(screen.getByLabelText('Prepared target configuration')).toHaveValue('carrier-draft')
})
