import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { PhoneStatusPanel } from './PhoneStatusPanel'
import { FieldEventsPage } from './FieldEventsPage'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })
const first = { id: 'fixture-phone', displayName: 'Fixture phone', terminalIdHex: '0102030405', groupName: 'Fixture group', onCall: false, lastSessionAtUtc: '2026-10-01T12:05:00Z' }
const state = { ...first, deactivatedAtUtc: null, latestReportAtUtc: '2026-10-01T12:05:03Z', statusReportId: 'report-old', statusReportedAtUtc: '2026-10-01T12:00:00Z',
  newerUnreadableReport: true, serialNumber: '0123456789', groups: [
    { label: 'Memory and stored records', flags: [{ name: 'Memory lost', asserted: true }, { name: 'EEPROM fault', asserted: false }] },
    { label: 'Enclosure and cash box', flags: [{ name: 'Cash box full', asserted: true }] },
  ], hasStatusBits: true, unmappedSetBitCount: 2 }

it('shows the exact retained report, reported flags, and an explicit warning for unknown newer evidence', async () => {
  vi.mocked(apiFetch).mockResolvedValue(Response.json(state))
  render(<MemoryRouter><PhoneStatusPanel terminalId={first.id} refresh={0} showReportLink /></MemoryRouter>)
  expect(await screen.findByText('0123456789')).toBeVisible()
  expect(screen.getByText('2026-10-01 12:00:00 UTC')).toBeVisible()
  expect(screen.getByRole('note')).toHaveTextContent('2026-10-01 12:05:03 UTC: A more recent MT10 could not be interpreted.')
  expect(screen.getByText('2 named conditions reported')).toBeVisible()
  expect(screen.getByText(/2 unmapped status bits set/)).toBeVisible()
  const memory = screen.getByRole('region', { name: 'Memory and stored records' })
  expect(within(memory).getByText('Memory lost').closest('li')).toHaveTextContent('Reported')
  expect(within(memory).getByText('EEPROM fault').closest('li')).toHaveTextContent('Not indicated')
  expect(screen.getByRole('link', { name: 'Open source status report' })).toHaveAttribute('href', '/dlog/report-old')
})

it('distinguishes a valid all-clear snapshot from a phone that has never reported', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ ...state, statusReportedAtUtc: null, latestReportAtUtc: null, newerUnreadableReport: false,
    statusReportId: null, groups: null, serialNumber: null, hasStatusBits: null, unmappedSetBitCount: null }))
    .mockResolvedValueOnce(Response.json({ ...state, newerUnreadableReport: false, groups: [{ label: 'Memory', flags: [{ name: 'Memory lost', asserted: false }] }],
      statusReportId: 'report-clear', unmappedSetBitCount: 0, hasStatusBits: false }))
  const { rerender } = render(<MemoryRouter><PhoneStatusPanel terminalId={first.id} refresh={0} showReportLink={false} /></MemoryRouter>)
  expect(await screen.findByText('Awaiting a valid status report from a matched NCC session.')).toBeVisible()
  expect(screen.queryByText('No named conditions reported')).toBeNull()
  rerender(<MemoryRouter><PhoneStatusPanel terminalId={first.id} refresh={1} showReportLink={false} /></MemoryRouter>)
  expect(await screen.findByText('No named conditions reported')).toBeVisible()
  expect(screen.queryByRole('link', { name: 'Open source status report' })).toBeNull()
})

it('allows a status-only operator to select a phone without fetching or linking denied registry and message screens', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'phone-status': 1 })
  vi.mocked(apiFetch).mockImplementation(async url => {
    if (url === '/api/field-events/phone-options') return Response.json([first])
    if (url === '/api/field-events/reference') return Response.json({ alarms: [], maintenance: [], canEdit: false })
    if (url.includes('/current-status')) return Response.json(state)
    if (url.startsWith('/api/field-events')) return Response.json({ items: [{ id: 'old-alarm', terminalId: first.id, terminalName: first.displayName,
      title: 'Alarm', summary: 'Memory lost (alarm 22)', category: 'alarm', capturedAtUtc: '2026-10-01T12:00:00Z' }] })
    return new Response(null, { status: 403 })
  })
  render(<MemoryRouter><FieldEventsPage /></MemoryRouter>)
  await screen.findByRole('option', { name: 'Fixture phone (0102030405)' })
  fireEvent.change(screen.getByLabelText('Phone'), { target: { value: first.id } })
  expect(await screen.findByText('0123456789')).toBeVisible()
  expect(screen.getByText('Memory lost (alarm 22)')).toBeVisible()
  expect(screen.queryByRole('link', { name: 'Open source status report' })).toBeNull()
  expect(screen.queryByRole('link', { name: 'Memory lost (alarm 22)' })).toBeNull()
  expect(vi.mocked(apiFetch).mock.calls.every(([path]) => path.startsWith('/api/field-events'))).toBe(true)
})
