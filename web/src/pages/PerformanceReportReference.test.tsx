import { cleanup, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { UploadDetail } from './UploadDetail'
import type { PerformanceReference } from './PerformanceReportReference'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostBareOk: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const received = '2028-03-01T12:00:00Z'
const reference: PerformanceReference = {
  status: 'acknowledged', downloadBatchId: 'fixture-batch', tableRevision: 2,
  ackRecordedAtUtc: '2028-03-01T11:50:00Z', laterUncertainAttempt: true,
  timing: [
    { measurementIndex: 0, startTicks: 100, intervalTicks: 20 },
    { measurementIndex: 1, startTicks: -10, intervalTicks: 20 },
    { measurementIndex: 2, startTicks: 777, intervalTicks: 777 }, // Reserved; not a report group.
    { measurementIndex: 3, startTicks: 32767, intervalTicks: 32767 },
    { measurementIndex: 4, startTicks: 500, intervalTicks: 0 },
  ],
}
function show(performanceReference?: PerformanceReference | null, type = 37) {
  const counts = Array.from({ length: 7 }, (_, i) => ({ label: `Bucket ${i + 1}`, value: 80 + i }))
  vi.mocked(apiFetch).mockResolvedValue(Response.json({
    id: 'fixture-report', terminalId: 'fixture-phone', status: 2, idempotencyKey: 'fixture',
    isTerminalStatistics: true, rawPayloadHex: '', decodedMetadataJson: '{}',
    createdAtUtc: received, updatedAtUtc: received, performanceReference,
    records: [{ id: 'fixture-record', terminalStatistics: {
      messageType: type, name: 'Communications performance', periodStart: '2028-02-29T10:00:00',
      periodEnd: '2028-03-01T10:00:00', carrierFormat: null, carriers: [],
      groups: [
        { label: 'Connection counters', counters: [{ label: 'Connection attempts', value: 12 }] },
        ...['Line seizure to end of dialing', 'End of dialing to carrier', 'Carrier to first packet',
          'Caller wait for NCC response'].map(label => ({ label, counters: counts })),
      ],
    } }],
  }))
  render(<MemoryRouter initialEntries={['/uploads/fixture-report']}><Routes>
    <Route path="/uploads/:id" element={<UploadDetail />} />
  </Routes></MemoryRouter>)
}

it('labels original report counts with their retained reference ranges and warns about uncertain later transfers', async () => {
  show(reference)
  const summary = await screen.findByRole('region', { name: 'MT32 timing reference' })
  expect(within(summary).getByRole('link', { name: 'View source download' })).toHaveAttribute('href', '/downloads/fixture-batch')
  expect(summary).toHaveTextContent(/Revision 2/)
  expect(summary).toHaveTextContent(/Reference ranges are not proven historical bucket boundaries/)
  expect(within(summary).getByRole('status')).toHaveTextContent(/later download attempt/)
  const group = screen.getByRole('heading', { name: 'Line seizure to end of dialing' }).closest('section')!
  expect(within(group).getByText('Reference: Below 1.00 s')).toBeVisible()
  expect(within(group).getByText('Reference: 1.00 s to below 1.20 s')).toBeVisible()
  expect(within(group).getByText('Reference: 2.00 s or more')).toBeVisible()
  expect(within(group).getByText('80')).toBeVisible()
  expect(within(group).getByText('86')).toBeVisible()
  const invalid = screen.getByRole('heading', { name: 'End of dialing to carrier' }).closest('section')!
  expect(invalid).toHaveTextContent('Reference timing is unavailable or invalid')
  const wrap = screen.getByRole('heading', { name: 'Carrier to first packet' }).closest('section')!
  expect(wrap).toHaveTextContent('Reference timing wraps the terminal timer')
  const zero = screen.getByRole('heading', { name: 'Caller wait for NCC response' }).closest('section')!
  expect(within(zero).getAllByText('Reference: Empty range')).toHaveLength(5)
  expect(within(zero).getByText('Reference: 5.00 s or more')).toBeVisible()
  expect(screen.queryByText(/7\.77 s/)).toBeNull()
})

it('keeps report counts visible with no usable or permitted reference', async () => {
  show({ ...reference, status: 'restricted', downloadBatchId: null, timing: null })
  expect(await screen.findByText(/requires read access to Downloads and Table records/)).toBeVisible()
  expect(screen.queryByRole('link', { name: 'View source download' })).toBeNull()
  expect(screen.queryByText(/Reference: /)).toBeNull()
  expect(screen.getAllByText('80')).toHaveLength(4)
})

it('does not add the MT32 reference to other report types', async () => {
  show(reference, 56)
  await screen.findByRole('heading', { name: 'Communications performance' })
  expect(screen.queryByRole('region', { name: 'MT32 timing reference' })).toBeNull()
  expect(screen.queryByText(/Reference: /)).toBeNull()
})
