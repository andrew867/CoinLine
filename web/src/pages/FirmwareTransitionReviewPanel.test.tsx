import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet } from '../api/client'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

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
