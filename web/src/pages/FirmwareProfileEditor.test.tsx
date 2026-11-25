import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { FirmwareProfileEditor } from './FirmwareProfileEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('requires a new review when applying the previously reviewed choice returns a conflict', async () => {
  const applied = vi.fn()
  const review = {
    firmwareVersionId: 'fixture', revision: 1, currentProfileId: null, proposedProfileId: 'known',
    phoneCount: 2, targetedSetCount: 1, assignedSetCount: 1, pendingBatchCount: 0,
    scheduledDownloadCount: 0, activeSessionCount: 0, activeBatchCount: 0, changed: true,
    canApply: true, previewToken: 'first-review', findings: [],
  }
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json(review))
    .mockResolvedValueOnce(Response.json({ error: 'Configuration has changed. Preview the impact again.' }, { status: 409 }))
    .mockResolvedValueOnce(Response.json({ ...review, activeSessionCount: 1, canApply: false, findings: [
      { scope: 'NCC sessions', tableNumber: null, blocking: true, message: 'Active session', count: 1 },
    ] }))
  render(<FirmwareProfileEditor id="fixture" currentProfileId={null} profiles={[{ id: 'known', label: 'Known layout' }]}
    onApplied={applied} onCancel={vi.fn()} />)
  fireEvent.change(screen.getByLabelText('Proposed table layout'), { target: { value: 'known' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview impact' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Apply reviewed profile' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Preview the impact again.')
  expect(screen.queryByRole('button', { name: 'Apply reviewed profile' })).not.toBeInTheDocument()
  expect(applied).not.toHaveBeenCalled()
  expect(vi.mocked(apiFetch).mock.calls[1][1]).toMatchObject({
    method: 'PUT', body: JSON.stringify({ tableProfileId: 'known', previewToken: 'first-review' }),
  })
  fireEvent.click(screen.getByRole('button', { name: 'Preview impact' }))
  await waitFor(() => expect(screen.getByText('Active session')).toBeVisible())
  expect(screen.getByRole('button', { name: 'Apply reviewed profile' })).toBeDisabled()
})
