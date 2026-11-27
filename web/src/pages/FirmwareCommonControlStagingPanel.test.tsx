import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'
import { FirmwareCommonControlStagingPanel } from './FirmwareCommonControlStagingPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

const group = (id: string, number: number, label: string) => ({ group: { id, number, label, note: 'Review independently.' },
  selected: false, fields: [{ key: 'fixture', label: 'Setting value', source: '12', target: '8', status: 'different' }] })
const preview = { sourceSetId: 'source', candidateSetId: 'candidate', recordCount: 5, canCreate: false, changedBytes: 0,
  previewToken: 'initial', findings: [{ blocking: true, number: 0, message: 'Choose and review controls.' }],
  records: [{ number: 29, status: 'available', changedBytes: 0, groups: [group('text-advertising', 29, 'Text advertising messages')] },
    { number: 50, status: 'available', changedBytes: 0, groups: [group('coin-denominations', 50, 'Coin values and acceptance'),
      group('coin-hardware', 50, 'Validator windows and physical capacities')] },
    { number: 35, status: 'missing-target', changedBytes: 0, groups: [group('routine-call-in', 35, 'Routine call-in and upload threshold')] },
    { number: 58, status: 'available', changedBytes: 0, groups: [group('daily-service-restriction', 58, 'Daily service restriction')] },
    { number: 93, status: 'available', changedBytes: 0, groups: [group('stored-value-unit-0', 93, 'Stored-value unit limit, index 0'),
      group('stored-value-unit-1', 93, 'Stored-value unit limit, index 1')] }] }

it('requires explicit selections, excludes unreviewed hardware and stages only a separate draft', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'Later layout', tableProfileId: 'mtr211-multipay-180-double-compressed' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target template', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'Earlier layout', targetFirmwareLabel: 'Later layout', sourceSetId: 'source',
      sourceProfileId: 'mtr20-early-multipay-200-single-compressed', targetProfileId: 'mtr211-multipay-180-double-compressed',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0, findings: [],
      configurationReadyForReview: false, upgradeQualified: false, records: [
        { tableNumber: 29, sourceBytes: 481, targetExpectedBytes: 481, candidateBytes: 481 },
        { tableNumber: 35, sourceBytes: 21, targetExpectedBytes: 21, candidateBytes: null },
        { tableNumber: 50, sourceBytes: 105, targetExpectedBytes: 105, candidateBytes: 105 },
        { tableNumber: 58, sourceBytes: 26, targetExpectedBytes: 26, candidateBytes: 26 },
      ] })
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(preview)).mockResolvedValueOnce(Response.json({ ...preview,
    canCreate: true, changedBytes: 6, findings: [], previewToken: 'reviewed' })).mockResolvedValueOnce(Response.json({ id: 'common-draft' }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward common configuration controls' })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview common configuration carry-forward' }))
  expect(await within(region).findByText('0 groups selected.')).toBeVisible()
  expect(within(region).getByLabelText('Routine call-in and upload threshold')).toBeDisabled()
  expect(within(region).getByLabelText('Validator windows and physical capacities')).not.toBeChecked()
  fireEvent.click(within(region).getByRole('region', { name: 'MT50 common controls' }).querySelector('summary')!)
  expect(within(region).getAllByText('Setting value').length).toBeGreaterThan(0)
  fireEvent.click(within(region).getByLabelText('Coin values and acceptance'))
  fireEvent.click(within(region).getByLabelText('Text advertising messages'))
  fireEvent.click(within(region).getByLabelText('Daily service restriction'))
  expect(within(region).getByLabelText('Stored-value unit limit, index 0')).not.toBeChecked()
  expect(within(region).getByLabelText('Stored-value unit limit, index 1')).not.toBeChecked()
  fireEvent.click(within(region).getByLabelText('Stored-value unit limit, index 1'))
  expect(within(region).getByLabelText('Stored-value unit limit, index 0')).not.toBeChecked()
  fireEvent.click(within(region).getByLabelText('I reviewed the selected controls and target hardware'))
  expect(within(region).queryByRole('button', { name: 'Create common configuration target draft' })).toBeNull()
  fireEvent.click(within(region).getByRole('button', { name: 'Preview common configuration carry-forward' }))
  expect(await within(region).findByText(/6 record bytes would change/)).toBeVisible()
  fireEvent.click(within(region).getByRole('button', { name: 'Create common configuration target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open common configuration draft' })).toHaveAttribute('href', '/table-sets/common-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/firmware/transition-common-controls', expect.objectContaining({
    selections: ['coin-denominations', 'daily-service-restriction', 'stored-value-unit-1', 'text-advertising'], reviewed: true, previewToken: 'reviewed',
  }))
  fireEvent.click(within(region).getByRole('button', { name: 'Use as target candidate for further review' }))
  expect(screen.getByLabelText('Prepared target configuration')).toHaveValue('common-draft')
})

it('does not offer a draft write to a read-only operator', async () => {
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ ...preview, canCreate: true }))
  render(<MemoryRouter><FirmwareCommonControlStagingPanel phoneId="phone" targetId="target" candidateId="candidate" parentBusy={false}
    onBusyChange={vi.fn()} onDraftCreated={vi.fn()} canAdd={false} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Preview common configuration carry-forward' }))
  expect(await screen.findByText('Table set creation permission is required to save the draft.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create common configuration target draft' })).toBeNull()
})
