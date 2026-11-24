import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import { apiGet, apiPostBare } from '../api/client'
import { TableDownloadScheduleList } from './TableDownloadScheduleList'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostBare: vi.fn() }))

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
