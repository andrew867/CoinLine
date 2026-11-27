import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'
import { FirmwareSetupStagingPanel } from './FirmwareSetupStagingPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

it('reviews source-only and target-only fields, protected settings and mapped references before staging', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'Later layout', tableProfileId: 'mtr211-multipay-180-double-compressed' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target template', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'Earlier layout', targetFirmwareLabel: 'Later layout', sourceSetId: 'source',
      sourceProfileId: 'mtr20-early-multipay-200-single-compressed', targetProfileId: 'mtr211-multipay-180-double-compressed',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], records: [
        { tableNumber: 26, sourceBytes: 72, targetExpectedBytes: 72, candidateBytes: 72, action: 'review-native-layout' },
        { tableNumber: 31, sourceBytes: 37, targetExpectedBytes: 37, candidateBytes: 37, action: 'review-native-layout' },
        { tableNumber: 92, sourceBytes: 3401, targetExpectedBytes: 3061, candidateBytes: 3061, action: 'replace-native-layout' }] })
  const preview = { recordCount: 4, canCreate: false, changedRecords: [26, 31], previewToken: 'first',
    references: [{ sourceSlot: 3, fields: ['Operator screening reference'], options: [8, 9], targetSlot: null }],
    fields: [
      { tableNumber: 26, key: 'terminalType', label: 'Terminal operating mode', action: 'changed', protected: false,
        sourceValue: 'Multi-pay (cards and coins)', targetBefore: 'Coin only', targetAfter: 'Multi-pay (cards and coins)' },
      { tableNumber: 26, key: 'incomingRate', label: 'Incoming call rate index', action: 'source-only', protected: false, sourceValue: '255', targetBefore: null, targetAfter: null },
      { tableNumber: 26, key: 'languageFirst', label: 'Default language', action: 'target-only', protected: false, sourceValue: null, targetBefore: 'French', targetAfter: 'French' },
      { tableNumber: 31, key: 'craftAccessCode', label: 'Craft access code', action: 'changed', protected: true, sourceValue: 'not-visible', targetBefore: null, targetAfter: null },
    ], findings: [{ blocking: true, tableNumber: 26, message: 'Choose the matching target screening position.' }] }
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(preview)).mockResolvedValueOnce(Response.json({ ...preview,
    canCreate: true, previewToken: 'reviewed', findings: [], references: [{ ...preview.references[0], targetSlot: 8 }] }))
    .mockResolvedValueOnce(Response.json({ id: 'setup-draft', changedRecords: [26, 31] }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward terminal feature and installation settings' })
  fireEvent.change(within(region).getByLabelText('New setup draft name'), { target: { value: 'Setup draft' } })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview terminal setup carry-forward' }))
  expect(await within(region).findByText('Changes proposed to MT26, MT31.')).toBeVisible()
  expect(within(region).getByRole('row', { name: /MT26: Incoming call rate index.*No direct target mapping/ })).toBeVisible()
  expect(within(region).getByRole('row', { name: /MT26: Default language.*Retain target-only setting/ })).toBeVisible()
  const protectedRow = within(region).getByRole('row', { name: /MT31: Craft access code/ })
  expect(within(protectedRow).getAllByText('Protected')).toHaveLength(3)
  expect(within(protectedRow).queryByText('not-visible')).not.toBeInTheDocument()
  expect(within(region).queryByRole('button', { name: 'Create setup target draft' })).not.toBeInTheDocument()
  fireEvent.change(within(region).getByLabelText('Target screening for source 3'), { target: { value: '8' } })
  fireEvent.click(within(region).getByLabelText('I reviewed the source-only, target-only and retained options'))
  expect(within(region).getByText('Preview again to confirm your choices and review.')).toBeVisible()
  fireEvent.click(within(region).getByRole('button', { name: 'Preview terminal setup carry-forward' }))
  fireEvent.click(await within(region).findByRole('button', { name: 'Create setup target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open setup draft' })).toHaveAttribute('href', '/table-sets/setup-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/firmware/transition-setup', expect.objectContaining({ referenceChoices: { '3': 8 },
    carryCredentials: true, retainPresentation: false, reviewedDifferences: true, previewToken: 'reviewed' }))
  fireEvent.click(within(region).getByRole('button', { name: 'Use as target candidate for further review' }))
  expect(screen.getByLabelText('Prepared target configuration')).toHaveValue('setup-draft')
})

it('defaults graphical targets to retaining their prepared presentation', () => {
  render(<MemoryRouter><FirmwareSetupStagingPanel phoneId="phone" targetId="target" candidateId="candidate" graphical
    parentBusy={false} onBusyChange={vi.fn()} onDraftCreated={vi.fn()} canAdd /></MemoryRouter>)
  expect(screen.getByLabelText('Retain target display and advertising settings')).toBeChecked()
})
