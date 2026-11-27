import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { DlogTransactionDetail } from './DlogTransactionDetail'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiDownload: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it.each([true, false])('distinguishes live version reports from diagnostic records (%s)', async (liveVersionObservation) => {
  vi.mocked(apiFetch).mockResolvedValue(Response.json({
    id: 'version-fixture', terminalId: 'phone-fixture', terminalName: 'Fixture phone', direction: 1,
    messageType: 60, messageTypeName: 'Software version', processingStatus: 1, liveVersionObservation,
    capturedAtUtc: '2026-11-23T12:05:00Z', fieldEvent: { title: 'Software version', category: 'status', summary: 'Controller version LABROM A002' },
    rawPayloadHex: '3C', decodedJson: '{}', parseDiagnostics: [], correlationLinks: [],
  }))
  render(<MemoryRouter initialEntries={['/dlog/version-fixture']}><Routes><Route path="/dlog/:id" element={<DlogTransactionDetail />} /></Routes></MemoryRouter>)
  expect(await screen.findByText(liveVersionObservation ? 'Observed on a matched NCC connection' : 'Imported or historical report; not eligible for version-based scheduling')).toBeVisible()
})

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
  expect(await screen.findByText('No records.')).toBeVisible()
})

it('opens the stored MT10 as readable profile-aware status rather than stale decoded JSON', async () => {
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ id: 'status-fixture', terminalId: 'phone-fixture', terminalName: 'Fixture phone',
    direction: 1, messageType: 10, messageTypeName: 'Phone status', processingStatus: 1, capturedAtUtc: '2026-11-23T12:05:00Z',
    fieldEvent: { title: 'Phone status', category: 'status', summary: 'EPM/SAM not responding', phoneTime: null },
    terminalStatus: { profile: 'Later', rawStatusHex: '0400000000', unmappedSetBitCount: 0, serialNumber: null,
      groups: [{ label: 'Handset and telephony', rawMask: 4, flags: [{ name: 'EPM/SAM not responding', asserted: true, statusIndex: 2 }], unresolvedFlags: [] }] },
    rawPayloadHex: '0A00000000000400000000', decodedJson: JSON.stringify({ Fields: { unmappedStatusBits: 1 } }) }))
  render(<MemoryRouter initialEntries={['/dlog/status-fixture']}><Routes><Route path="/dlog/:id" element={<DlogTransactionDetail />} /></Routes></MemoryRouter>)
  const section = await screen.findByRole('region', { name: 'Decoded terminal status' })
  expect(section).toHaveTextContent('EPM/SAM not responding')
  expect(section).toHaveTextContent('Unresolved asserted bits: 0')
  expect(section).toHaveTextContent('Registered later profile')
  expect(screen.getByText('Original decoded JSON')).not.toBeVisible()
  expect(screen.getByTestId('hex-viewer')).not.toBeVisible()
})
