import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { UploadDetail } from './UploadDetail'
import { UploadsPage } from './UploadsPage'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostBareOk: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const base = {
  id: 'graphical-001', terminalId: 'fixture-phone', status: 2, idempotencyKey: 'fixture', rawPayloadHex: '',
  isTerminalStatistics: true, recordCount: 1, statisticsMessageType: 153, statisticsName: 'Graphical usage statistics',
}

it('finds graphical usage by report type and opens decoded owner and key counts', async () => {
  vi.mocked(apiFetch).mockImplementation(async url => url === '/api/uploads'
    ? Response.json([base, { ...base, id: 'carrier-001', statisticsMessageType: 71, statisticsName: 'Carrier statistics' }])
    : Response.json({ ...base, decodedMetadataJson: '{}', relatedDlogTransactionId: null,
      createdAtUtc: '2028-03-01T12:00:00Z', updatedAtUtc: '2028-03-01T12:00:00Z', records: [{
        id: 'record-1', terminalStatistics: {
          name: 'Graphical usage statistics', messageType: 153, periodStart: '2028-02-29T10:11:12',
          periodEnd: '2028-03-01T10:11:12', groups: [], carriers: [], carrierFormat: null,
          graphicalScreens: [{ screenNumber: 24, ownerCode: 1234, directionKeyPressCount: 321,
            keys: Array.from({ length: 6 }, (_, i) => ({
              keyNumber: i + 1, ownerCode: 2000 + i, pressCount: i === 5 ? 65535 : 0,
            })) }],
        },
      }] }))

  render(<MemoryRouter initialEntries={['/uploads']}><Routes>
    <Route path="/uploads" element={<UploadsPage />} />
    <Route path="/uploads/:id" element={<UploadDetail />} />
  </Routes></MemoryRouter>)
  const reportType = await screen.findByLabelText('Statistics report')
  fireEvent.change(reportType, { target: { value: '153' } })
  expect(screen.queryByText('Carrier statistics (MT71)', { selector: 'td' })).toBeNull()
  const item = screen.getByText('Graphical usage statistics (MT153)', { selector: 'td' }).closest('tr')!
  fireEvent.click(within(item).getByRole('link'))
  const table = await screen.findByRole('table', { name: 'Graphical usage by screen' })
  expect(within(table).getByRole('columnheader', { name: 'Softkey 6' })).toBeVisible()
  expect(within(table).getByRole('rowheader', { name: 'Screen 24' })).toBeVisible()
  expect(within(table).getByText('1234')).toBeVisible()
  expect(within(table).getByText('Owner 2005')).toBeVisible()
  expect(within(table).getByText((65535).toLocaleString())).toBeVisible()
  expect(within(table).getByText('321')).toBeVisible()
  expect(screen.getByText(/Counts are key presses, not advertisement impressions/)).toBeVisible()
  expect(screen.queryByText('No carrier entries in this report.')).toBeNull()
})
