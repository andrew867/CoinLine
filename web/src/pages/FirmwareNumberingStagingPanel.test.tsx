import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'
import { FirmwareNumberingStagingPanel } from './FirmwareNumberingStagingPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

it('reviews distinct graph capacities, retained site fields and fresh choices before staging', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'Later layout', tableProfileId: 'mtr211-multipay-180-double-compressed' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target template', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'Earlier layout', targetFirmwareLabel: 'Later layout', sourceSetId: 'source',
      sourceProfileId: 'mtr20-early-multipay-200-single-compressed', targetProfileId: 'mtr211-multipay-180-double-compressed',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], records: [
        { tableNumber: 62, sourceBytes: 353, targetExpectedBytes: 353, candidateBytes: 353, action: 'review-native-layout' }] })
  const preview = { recordCount: 3, canCreate: false, changedBytes: 19, previewToken: 'first',
    graphs: [
      { section: 'dial', sourceCapacity: 180, targetCapacity: 180, sourceRules: 2, candidateRules: 1, proposedRules: 2,
        sourceUnassignedBytes: 1, targetUnassignedBytes: 1, action: 'carried', changedBytes: 7 },
      { section: 'format', sourceCapacity: 132, targetCapacity: 140, sourceRules: 1, candidateRules: 2, proposedRules: 1,
        sourceUnassignedBytes: 0, targetUnassignedBytes: 1, action: 'carried', changedBytes: 9 },
    ], fields: [
      { key: 'screenTimeout', label: 'Screening digit timeout', action: 'changed', protected: false, sourceValue: '25', targetBefore: '40', targetAfter: '25' },
      { key: 'currencySymbol', label: 'Currency symbol', action: 'source-only', protected: false, sourceValue: '$', targetBefore: null, targetAfter: null },
      { key: 'keyCardNumber', label: 'Desk terminal key card number', action: 'retained-target', protected: true, sourceValue: 'not-visible', targetBefore: null, targetAfter: null },
    ], findings: [{ blocking: true, message: 'Review the graph changes.' }] }
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(preview)).mockResolvedValueOnce(Response.json({ ...preview, canCreate: true,
    previewToken: 'reviewed', findings: [], changedBytes: 10, graphs: [preview.graphs[0], { ...preview.graphs[1], proposedRules: 2, changedBytes: 0, action: 'retained-target' }] }))
    .mockResolvedValueOnce(Response.json({ id: 'numbering-draft', changedBytes: 10 }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward numbering-plan configuration' })
  fireEvent.change(within(region).getByLabelText('New numbering draft name'), { target: { value: 'Numbering draft' } })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview numbering-plan carry-forward' }))
  expect(await within(region).findByText('19 MT62 bytes would change.')).toBeVisible()
  expect(within(region).getByRole('row', { name: /Display formatting.*1 rules \/ 132 bytes available.*2 rules \/ 140 bytes available/ })).toBeVisible()
  expect(within(region).getByRole('row', { name: /Currency symbol.*No direct target mapping/ })).toBeVisible()
  const desk = within(region).getByRole('row', { name: /Desk terminal key card number/ })
  expect(within(desk).getAllByText('Protected')).toHaveLength(3); expect(within(desk).queryByText('not-visible')).not.toBeInTheDocument()
  expect(within(region).queryByRole('button', { name: 'Create numbering target draft' })).not.toBeInTheDocument()
  fireEvent.click(within(region).getByLabelText('Carry the source display-format graph'))
  fireEvent.click(within(region).getByLabelText('I reviewed the graph changes and profile-specific retained settings'))
  expect(within(region).getByText('Preview again to confirm your choices and review.')).toBeVisible()
  fireEvent.click(within(region).getByRole('button', { name: 'Preview numbering-plan carry-forward' }))
  expect(await within(region).findByText('10 MT62 bytes would change.')).toBeVisible()
  expect(within(region).getByRole('row', { name: /Display formatting.*Keep candidate setting/ })).toBeVisible()
  fireEvent.click(within(region).getByRole('button', { name: 'Create numbering target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open numbering draft' })).toHaveAttribute('href', '/table-sets/numbering-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/firmware/transition-numbering', expect.objectContaining({ carryHeader: true, carryDial: true,
    carryFormat: false, carryDeskCredential: false, reviewedDifferences: true, previewToken: 'reviewed' }))
  fireEvent.click(within(region).getByRole('button', { name: 'Use as target candidate for further review' }))
  expect(screen.getByLabelText('Prepared target configuration')).toHaveValue('numbering-draft')
})

it('defaults graphical targets to their prepared display-format graph and keeps desk data explicit', () => {
  render(<MemoryRouter><FirmwareNumberingStagingPanel phoneId="phone" targetId="target" candidateId="candidate" graphical parentBusy={false}
    onBusyChange={vi.fn()} onDraftCreated={vi.fn()} canAdd /></MemoryRouter>)
  expect(screen.getByLabelText('Carry the source display-format graph')).not.toBeChecked()
  expect(screen.getByLabelText('Carry the source desk key-card field (value stays protected)')).not.toBeChecked()
  expect(screen.getByLabelText('Carry the source dialing-rule graph')).toBeChecked()
})
