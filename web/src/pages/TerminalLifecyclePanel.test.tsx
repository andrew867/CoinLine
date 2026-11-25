import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TerminalLifecyclePanel } from './TerminalLifecyclePanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn() }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

const path = '/api/terminals/fixture-phone/lifecycle'
const initial = { active: true, deactivatedAtUtc: null, deletionReviewDate: null, reviewDue: false,
  defaultDeletionReviewDate: '2028-03-17', revision: 2 }
const inactive = { ...initial, active: false, deactivatedAtUtc: '2028-02-01T12:00:00Z', deletionReviewDate: '2028-07-01', revision: 3 }

it('deactivates with an editable grace date and reactivates without deleting records', async () => {
  vi.mocked(apiGet).mockResolvedValue(initial)
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json(inactive))
    .mockResolvedValueOnce(Response.json({ ...initial, revision: 4 }))
  const onChanged = vi.fn()
  render(<TerminalLifecyclePanel terminalId="fixture-phone" onChanged={onChanged} />)
  expect(await screen.findByLabelText('Deletion review date')).toHaveValue('2028-03-17')
  expect(screen.getByText(/does not delete them automatically/)).toBeVisible()
  fireEvent.change(screen.getByLabelText('Deletion review date'), { target: { value: '2028-07-01' } })
  fireEvent.click(screen.getByRole('button', { name: 'Deactivate phone' }))
  expect(await screen.findByText('Phone deactivated.')).toBeVisible()
  expect(screen.getByText('Inactive in Manager')).toBeVisible()
  expect(apiFetch).toHaveBeenLastCalledWith(path, expect.objectContaining({
    method: 'PUT', body: JSON.stringify({ active: false, revision: 2, deletionReviewDate: '2028-07-01' }),
  }))
  expect(onChanged).toHaveBeenCalledWith(inactive)
  fireEvent.click(screen.getByRole('button', { name: 'Reactivate phone' }))
  expect(await screen.findByText('Phone reactivated.')).toBeVisible()
  expect(apiFetch).toHaveBeenLastCalledWith(path, expect.objectContaining({
    method: 'PUT', body: JSON.stringify({ active: true, revision: 3, deletionReviewDate: null }),
  }))
})

it('shows dates but disables changes for a read-only lifecycle profile', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'phone-lifecycle': 1 })
  vi.mocked(apiGet).mockResolvedValue(inactive)
  render(<TerminalLifecyclePanel terminalId="fixture-phone" onChanged={() => {}} />)
  expect(await screen.findByText('Inactive in Manager')).toBeVisible()
  expect(screen.getByLabelText('Deletion review date')).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Reactivate phone' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Update review date' })).toBeDisabled()
})
