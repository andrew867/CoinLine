import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostBare } from '../api/client'
import { TableDownloadScheduleList } from './TableDownloadScheduleList'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostBare: vi.fn() }))
afterEach(cleanup)

it('queues a delivered firmware cancellation and shows the batch still needs acknowledgement', async () => {
  const row = {
    id: 'schedule-fixture', terminalId: 'phone-fixture', phone: 'Fixture phone',
    tableSetId: 'set-fixture', tableSet: 'Firmware settings', availableAtUtc: '2026-11-23T10:00:00Z',
    preparedAtUtc: '2026-11-23T10:00:00Z', startedAtUtc: '2026-11-23T10:01:00Z',
    completedAtUtc: '2026-11-23T10:02:00Z', recordNumbers: null, status: 'Delivered',
    lastBatchId: 'delivered-fixture', lastError: null, canQueueFirmwareCancellation: true,
  }
  vi.mocked(apiGet).mockResolvedValueOnce([row]).mockResolvedValueOnce([{ ...row, canQueueFirmwareCancellation: false }])
  vi.mocked(apiPostBare).mockResolvedValue(Response.json({ batchId: 'cancellation-fixture' }))

  render(<MemoryRouter><TableDownloadScheduleList setId="set-fixture" /></MemoryRouter>)
  fireEvent.click(await screen.findByText(/Scheduled downloads/))
  fireEvent.click(await screen.findByRole('button', { name: 'Queue firmware cancellation' }))
  expect(apiPostBare).toHaveBeenCalledWith('/api/tables/sets/set-fixture/schedules/schedule-fixture/clear-firmware')
  expect(await screen.findByRole('status')).toHaveTextContent('The phone must acknowledge it on a subsequent call-in.')
  expect(screen.getByRole('link', { name: 'View cancellation batch' })).toHaveAttribute('href', '/downloads/cancellation-fixture')
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Queue firmware cancellation' })).toBeNull())
})

it('shows the allocated terminal start and links its immutable record from the source history', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{
    id: 'schedule-fixture', terminalId: 'phone-fixture', phone: 'Fixture phone',
    tableSetId: 'allocated-fixture', sourceTableSetId: 'source-fixture', tableSet: 'Allocated firmware parameters',
    availableAtUtc: '2026-11-23T10:00:00Z', preparedAtUtc: '2026-11-23T10:00:00Z',
    firmwareStartAtUtc: '2026-11-24T02:00:00Z', firmwareSlotEndAtUtc: '2026-11-24T02:30:00Z',
    status: 'Ready', lastBatchId: 'prepared-fixture', recordNumbers: null, canQueueFirmwareCancellation: false,
  }])
  render(<MemoryRouter><TableDownloadScheduleList setId="source-fixture" /></MemoryRouter>)
  fireEvent.click(await screen.findByText(/Scheduled downloads/))
  expect(await screen.findByRole('link', { name: 'MT94 allocated record' })).toHaveAttribute('href', '/table-sets/allocated-fixture')
  expect(screen.getByText('2026-11-24 02:00')).toHaveAttribute('datetime', '2026-11-24T02:00:00Z')
  expect(screen.getByText('2026-11-24 02:30')).toHaveAttribute('datetime', '2026-11-24T02:30:00Z')
  expect(screen.getByRole('link', { name: 'Prepared batch' })).toHaveAttribute('href', '/downloads/prepared-fixture')
})
