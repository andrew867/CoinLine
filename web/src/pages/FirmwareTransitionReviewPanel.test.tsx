import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

it('reviews record size changes without offering an upgrade execution action', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'MTR 2.11', tableProfileId: 'later' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target draft', status: 0, targetFirmwareVersionId: 'target' },
      { id: 'other', name: 'Unrelated set', status: 1, targetFirmwareVersionId: 'other' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'MTR 2.0', targetFirmwareLabel: 'MTR 2.11', sourceSetId: 'source',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false,
      records: [{ tableNumber: 92, sourceBytes: 3401, targetExpectedBytes: 3061, candidateBytes: 3061,
        action: 'candidate-replacement', detail: 'Verify translated settings.', targetAcknowledged: true, requiresLayoutReview: false }],
      findings: [{ blocking: true, tableNumber: null, message: 'The target configuration is still a draft.' }] })
  render(<FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} />)
  expect(apiGet).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  expect(within(screen.getByLabelText('Prepared target configuration')).queryByText('Unrelated set')).not.toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  expect(await screen.findByText('MTR 2.0 → MTR 2.11')).toBeVisible()
  const row = screen.getByRole('row', { name: /MT92/ })
  expect(within(row).getByText('3401')).toBeVisible()
  expect(within(row).getAllByText('3061')).toHaveLength(2)
  expect(within(row).getByText('Replacement supplied')).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Remote upgrade is not qualified.')
  expect(screen.getByText(/still a draft/)).toBeVisible()
  expect(apiGet).toHaveBeenLastCalledWith('/api/firmware/transition-review?terminalId=phone&targetFirmwareVersionId=target&candidateSetId=candidate')
  expect(screen.queryByRole('button', { name: /execute|schedule|activate/i })).not.toBeInTheDocument()
})

it('previews management access only and creates a separate unassigned target draft with the reviewed token', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'MTR 2.11', tableProfileId: 'later' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target draft', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'MTR 2.0', targetFirmwareLabel: 'MTR 2.11', sourceSetId: 'source',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false,
      records: [{ tableNumber: 21, sourceBytes: 18, targetExpectedBytes: 48, candidateBytes: 48,
        action: 'candidate-replacement', detail: 'Review management access.', targetAcknowledged: true, requiresLayoutReview: false }], findings: [] })
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ sourceSetId: 'source', candidateSetId: 'candidate',
    sourceBytes: 18, targetBytes: 48, changedBytes: 16, recordCount: 12, previewToken: 'reviewed-token' }))
    .mockResolvedValueOnce(Response.json({ id: 'staged-draft', changedBytes: 16 }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward management access' })
  fireEvent.change(within(region).getByLabelText('New target draft name'), { target: { value: 'Prepared phone target' } })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview management-access carry-forward' }))
  expect(await within(region).findByText(/18 bytes to 48 bytes; 16 changed target bytes. 11 other records/)).toBeVisible()
  expect(apiPostRaw).toHaveBeenNthCalledWith(1, '/api/firmware/transition-access/preview',
    { terminalId: 'phone', targetFirmwareVersionId: 'target', candidateSetId: 'candidate', name: 'Prepared phone target', previewToken: undefined })
  fireEvent.click(within(region).getByRole('button', { name: 'Create target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open target draft and review remaining records' })).toHaveAttribute('href', '/table-sets/staged-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(2, '/api/firmware/transition-access',
    { terminalId: 'phone', targetFirmwareVersionId: 'target', candidateSetId: 'candidate', name: 'Prepared phone target', previewToken: 'reviewed-token' })
  expect(within(region).getByText(/Target draft created, not assigned/)).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Remote upgrade is not qualified.')
})
