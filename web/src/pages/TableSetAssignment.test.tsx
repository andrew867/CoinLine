import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import { apiGet, apiGetRaw, apiPostRaw } from '../api/client'
import { TableSetAssignment } from './TableSetAssignment'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiGetRaw: vi.fn(), apiPostRaw: vi.fn(), apiPostBare: vi.fn() }))

it('keeps existing firmware schedules by default and confirms the exact records to replace', async () => {
  vi.mocked(apiGet).mockImplementation(async path => (path === '/api/terminals'
    ? [{ id: 'phone-fixture', displayName: 'Fixture phone' }] : []) as never)
  const phone = { id: 'phone-fixture', displayName: 'Fixture phone', terminalFamilyLabel: null }
  vi.mocked(apiGetRaw)
    .mockResolvedValueOnce(Response.json({ targets: [{ ...phone, eligible: false, reason: 'Existing firmware schedule will be kept.' }] }))
    .mockResolvedValueOnce(Response.json({
      targets: [{ ...phone, eligible: true, reason: 'Replace the existing pending firmware schedule.', replacesFirmwareScheduleId: 'prior-fixture' }],
    }))
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ scheduled: 1, replaced: 1, skipped: 0 }))

  render(<MemoryRouter><TableSetAssignment setId="set-fixture" revision={1} versions={[]} optionalRecords={[]} codeOnly /></MemoryRouter>)
  await screen.findByRole('option', { name: 'Fixture phone' })
  fireEvent.change(screen.getByLabelText('Phone'), { target: { value: 'phone-fixture' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  expect(await screen.findByText(/Existing firmware schedule will be kept/)).toBeVisible()
  expect(screen.getByRole('button', { name: 'Schedule for 0 phones' })).toBeDisabled()
  expect(apiGetRaw).toHaveBeenLastCalledWith(expect.stringContaining('allowFirmwareReschedule=false'))
  fireEvent.click(screen.getByLabelText('Allow rescheduling existing firmware parameters'))
  expect(screen.queryByText(/Existing firmware schedule will be kept/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  expect(await screen.findByText('1 existing schedule to replace. 0 phones to skip.')).toBeVisible()
  expect(apiGetRaw).toHaveBeenLastCalledWith(expect.stringContaining('allowFirmwareReschedule=true'))
  fireEvent.click(screen.getByRole('button', { name: 'Schedule for 1 phone' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Replaced 1 existing schedule.')
  expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/set-fixture/schedules', expect.objectContaining({
    terminalIds: ['phone-fixture'], allowFirmwareReschedule: true, replacesFirmwareScheduleIds: ['prior-fixture'],
  }))
})
