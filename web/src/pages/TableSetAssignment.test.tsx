import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiGetRaw, apiPostRaw } from '../api/client'
import { TableSetAssignment } from './TableSetAssignment'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiGetRaw: vi.fn(), apiPostRaw: vi.fn(), apiPostBare: vi.fn() }))
afterEach(cleanup)

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

it('previews the daily firmware window and confirms its exact allocation', async () => {
  vi.mocked(apiGet).mockImplementation(async path => (path === '/api/terminals'
    ? [{ id: 'window-phone', displayName: 'Window phone' }] : []) as never)
  const start = '2028-02-28T02:00:00Z'
  const end = '2028-02-28T02:30:00Z'
  vi.mocked(apiGetRaw).mockResolvedValueOnce(Response.json({
    targets: [{ id: 'window-phone', displayName: 'Window phone', eligible: true, firmwareStartAtUtc: start,
      firmwareSlotEndAtUtc: end, firmwareServerSlot: 1 }],
    firmwareAllocation: { previewToken: 'window-token', selectedBlocks: 3, minimumTransferMinutes: 1,
      activeServers: 2, slotMinutes: 30, firstStartAtUtc: start, lastEndAtUtc: end,
      existingReservations: 2, fixedTimeSchedules: 1 },
  }))
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ scheduled: 1, replaced: 0, skipped: 0 }))

  render(<MemoryRouter><TableSetAssignment setId="window-fixture" revision={1} versions={[]} optionalRecords={[]} codeOnly
    firmwareSchedule={{ enabled: true, selectedBlocks: 3, minimumTransferMinutes: 1, startAtUtc: start }} /></MemoryRouter>)
  await screen.findByRole('option', { name: 'Window phone' })
  fireEvent.change(screen.getByLabelText('Phone'), { target: { value: 'window-phone' } })
  fireEvent.click(screen.getByLabelText('Allocate terminal starts across a daily download window'))
  fireEvent.change(screen.getByLabelText('First date'), { target: { value: '2028-02-28' } })
  fireEvent.change(screen.getByLabelText('Daily start'), { target: { value: '02:00' } })
  fireEvent.change(screen.getByLabelText('Daily end'), { target: { value: '02:30' } })
  fireEvent.change(screen.getByLabelText('Active servers'), { target: { value: '2' } })
  fireEvent.change(screen.getByLabelText('Minutes per phone'), { target: { value: '30' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  expect(await screen.findByText(/2028-02-28 02:00 UTC to 2028-02-28 02:30 UTC/)).toBeVisible()
  expect(screen.getByText(/2 existing reservations accounted for/)).toBeVisible()
  expect(screen.getByRole('note')).toHaveTextContent('1 fixed-time schedule has no reserved duration')
  expect(apiGetRaw).toHaveBeenLastCalledWith(expect.stringContaining('firmwareWindow.activeServers=2'))
  fireEvent.click(screen.getByRole('button', { name: 'Schedule for 1 phone' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Scheduled firmware parameters for 1 phone.')
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/window-fixture/schedules', expect.objectContaining({
    firmwareWindow: { startDate: '2028-02-28', dailyStart: '02:00:00', dailyEnd: '02:30:00', activeServers: 2, slotMinutes: 30 },
    firmwarePreviewToken: 'window-token', terminalIds: ['window-phone'],
  }))
})
