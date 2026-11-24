import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { DlogTransactionDetail } from './DlogTransactionDetail'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiDownload: vi.fn() }))

it('shows the alarm and phone clock while keeping hex and JSON in the technical record', async () => {
  vi.mocked(apiFetch).mockImplementation(async (path) => {
    if (path === '/api/dlog/message-types') return Response.json([])
    if (path !== '/api/dlog/transactions/alarm-fixture') throw new Error('Unexpected fixture request')
    return Response.json({
      id: 'alarm-fixture',
      terminalId: 'phone-fixture',
      terminalName: 'Fixture phone',
      direction: 1,
      messageType: 7,
      messageTypeName: 'Alarm',
      processingStatus: 1,
      capturedAtUtc: '2026-11-23T12:05:00Z',
      fieldEvent: {
        title: 'Alarm',
        category: 'alarm',
        summary: 'Memory lost (alarm 22)',
        phoneTime: '2026-11-23T12:00:00',
      },
      rawPayloadHex: '077E0B170C000016',
      decodedJson: JSON.stringify({ Fields: { alarmNum: 22 } }),
    })
  })

  render(<MemoryRouter initialEntries={['/dlog/alarm-fixture']}>
    <Routes><Route path="/dlog/:id" element={<DlogTransactionDetail />} /></Routes>
  </MemoryRouter>)

  expect(await screen.findByText('Memory lost (alarm 22)')).toBeVisible()
  expect(screen.getByRole('heading', { name: 'Decoded alarm' })).toBeVisible()
  expect(screen.getByRole('link', { name: 'Fixture phone' })).toHaveAttribute('href', '/terminals/phone-fixture')
  expect(screen.getByText('2026-11-23 12:00:00')).toHaveAttribute('datetime', '2026-11-23T12:00:00')
  expect(screen.getByTestId('hex-viewer')).not.toBeVisible()
  expect(screen.getByText('Decoded JSON')).not.toBeVisible()

  fireEvent.click(screen.getByText('Technical record'))
  expect(screen.getByTestId('hex-viewer')).toBeVisible()
  expect(screen.getByRole('heading', { name: 'Decoded JSON' })).toBeVisible()
})
