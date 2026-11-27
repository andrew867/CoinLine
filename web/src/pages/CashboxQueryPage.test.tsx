import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { mayOpenScreen } from '../components/Layout'
import { CashboxQueryLink, CashboxQueryPage } from './CashboxQueryPage'
import { TerminalCashboxPanel } from './TerminalCashboxPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiFetch: vi.fn(), apiPostRaw: vi.fn() }))

const phones = ['Alpha', 'Beta', 'Gamma'].map((displayName, index) => ({
  id: `fixture-${index}`, displayName, terminalIdHex: `000000000${index + 1}`,
}))
const receivedAtUtc = '2028-10-01T12:00:00Z'
const terminalTime = '2028-10-01T08:00:00'
const status = { boxNumber: '00000001', terminalTime, balanceMinorUnits: 75, percentFull: 40,
  condition: 'Value threshold exceeded', unmappedSetBitCount: 1, coinCounts: [0, 0, 0, 3] }
const reference = (value: number, revision: number, laterUncertainAttempt = false) => ({
  status: 'acknowledged', tableRevision: revision, ackRecordedAtUtc: '2028-10-01T11:30:00Z', laterUncertainAttempt,
  slots: [{ number: 2, valueMinorUnits: 5, validatorWindowEnabled: true, accepted: true },
    { number: 4, valueMinorUnits: value, validatorWindowEnabled: true, accepted: true }],
})
const firstLedger = { statuses: [{ id: 'status-a', receivedAtUtc, status, isCurrent: true }], collections: [],
  coinConfigurations: { 'status-a': { ...reference(25, 2), downloadBatchId: 'fixture-download' } } }
const secondLedger = { statuses: [{ id: 'status-b', receivedAtUtc, status, isCurrent: false }], collections: [{
  id: 'collection', receivedAtUtc, terminalTime, boxNumber: '00000001', newBoxNumber: '00000002',
  totalMinorUnits: 125, removedBoxBalanceMinorUnits: 75, isSummed: true, coinCounts: [0, 0, 0, 3],
}], coinConfigurations: { 'status-b': reference(50, 4, true), collection: reference(10, 1) } }

function mockReads() {
  vi.mocked(apiGet).mockImplementation(async path => {
    const url = new URL(String(path), 'http://localhost')
    if (url.pathname === '/api/cashbox/phones') {
      const exact = url.searchParams.get('terminalId'), query = url.searchParams.get('query'), page = Number(url.searchParams.get('page') ?? 1)
      const all = phones.filter(phone => exact ? phone.id === exact : !query || phone.terminalIdHex.includes(query))
      return { phones: all.slice((page - 1) * 2, page * 2), total: all.length, page, pageSize: 2 } as never
    }
    const ledgers = [firstLedger, secondLedger, { statuses: [], collections: [] }]
    const index = phones.findIndex(phone => url.pathname === `/api/terminals/${phone.id}/cashbox`)
    if (index >= 0) return ledgers[index] as never
    throw new Error(`Unexpected read ${path}`)
  })
}

afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

it('finds and browses cashboxes across pages without treating collections or absent reports as the current balance', async () => {
  mockReads()
  render(<MemoryRouter><CashboxQueryPage /></MemoryRouter>)
  const summary = await screen.findByRole('region', { name: 'Current reported cashbox' })
  expect(within(summary).getByText('0.75')).toBeVisible()
  expect(within(summary).getByText('Value threshold exceeded')).toBeVisible()
  expect(within(summary).getByText('1 unmapped status bit set')).toBeVisible()
  expect(within(summary).getByText('2028-10-01 12:00:00 UTC')).toBeVisible()
  expect(within(summary).getByText('2028-10-01 08:00:00 (phone clock)')).toBeVisible()
  expect(within(summary).getByText('Slot 2: 0 (configured 0.05)')).toBeVisible()
  expect(within(summary).getByText('Slot 4: 3 (configured 0.25)')).toBeVisible()
  expect(within(summary).getByText(/MT50 revision 2, acknowledged 2028-10-01 11:30:00 UTC/)).toBeVisible()
  expect(within(summary).getByText(/not verified denominations of accumulated coins/)).toBeVisible()
  expect(within(summary).getByRole('link', { name: 'Source download' })).toHaveAttribute('href', '/downloads/fixture-download')
  expect(screen.getByRole('button', { name: 'Find previous' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Find next' }))
  expect(await screen.findByText(/No current box status is available/)).toBeVisible()
  const history = within(screen.getByRole('table', { name: 'Reported collections' }))
  expect(history.getByText('1.25 (combined)')).toBeVisible()
  expect(history.getByText(/Slot 4: 3 \(configured 0.10\)/)).toBeVisible()
  const previous = within(screen.getByRole('table', { name: 'Latest status by box' }))
  expect(previous.getByText('Slot 4: 3', { exact: true })).toBeVisible()
  expect(previous.getByText(/A later transfer lacks a confirmed acknowledgement/)).toBeVisible()
  expect(previous.queryByText(/configured 0.50/)).not.toBeInTheDocument()
  expect(previous.queryByRole('link', { name: 'Source download' })).not.toBeInTheDocument()
  expect(screen.queryByRole('region', { name: 'Current reported cashbox' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Find next' }))
  expect(await screen.findByText('No cashbox reports yet.')).toBeVisible()
  const unknown = screen.getByRole('note')
  expect(within(unknown).getByText('Current balance unknown.')).toBeVisible()
  expect(unknown).toHaveTextContent('cashbox status (MT38) or collection (MT51) report')
  expect(unknown).toHaveTextContent('A successfully uploaded coin call can still exist without a cashbox report.')
  expect(screen.queryByRole('region', { name: 'Current reported cashbox' })).not.toBeInTheDocument()
  expect(screen.queryByRole('table', { name: 'Reported collections' })).not.toBeInTheDocument()
  expect(within(screen.getByRole('region', { name: 'Find a phone' })).getByRole('status')).toHaveTextContent('Phone 3 of 3')
  fireEvent.click(screen.getByRole('button', { name: 'Find previous' }))
  expect(await screen.findByText(/No current box status is available/)).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Phone 2 of 3')
  fireEvent.change(screen.getByRole('searchbox'), { target: { value: phones[0].terminalIdHex } })
  fireEvent.click(screen.getByRole('button', { name: 'Find phones' }))
  expect(await screen.findByRole('region', { name: 'Current reported cashbox' })).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Phone 1 of 1')
  expect(apiGet).toHaveBeenCalledWith(`/api/cashbox/phones?query=${phones[0].terminalIdHex}&page=1`)
})

it('locks query from a selected phone and refreshes only that phone', async () => {
  mockReads()
  render(<MemoryRouter initialEntries={['/cashbox?terminalId=fixture-0']}><CashboxQueryPage /></MemoryRouter>)
  expect(await screen.findByRole('region', { name: 'Current reported cashbox' })).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Find next' })).not.toBeInTheDocument()
  expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Browse all cashboxes' })).toHaveAttribute('href', '/cashbox')
  fireEvent.click(screen.getByRole('button', { name: 'Refresh reports' }))
  await waitFor(() => expect(apiGet).toHaveBeenCalledTimes(3))
  expect(vi.mocked(apiGet).mock.calls.map(call => call[0])).toEqual([
    '/api/cashbox/phones?terminalId=fixture-0', '/api/terminals/fixture-0/cashbox', '/api/terminals/fixture-0/cashbox',
  ])
})

it('keeps the selected-phone shortcut and screen tied to cashbox read permission', () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'phone-status': 1 })
  const { rerender } = render(<MemoryRouter><CashboxQueryLink terminalId="fixture-0" /></MemoryRouter>)
  expect(screen.queryByRole('link')).not.toBeInTheDocument()
  expect(mayOpenScreen('/cashbox', 'Operator', { 'phone-status': 1 })).toBe(false)
  expect(mayOpenScreen('/cashbox', 'Operator', { cashbox: 2 })).toBe(false)
  consoleSession.save('fixture', '', undefined, 'Operator', { cashbox: 1 })
  rerender(<MemoryRouter><CashboxQueryLink terminalId="fixture-0" /></MemoryRouter>)
  expect(screen.getByRole('link', { name: 'Query cashbox' })).toHaveAttribute('href', '/cashbox?terminalId=fixture-0')
  expect(mayOpenScreen('/cashbox', 'Operator', { cashbox: 1 })).toBe(true)
})

it('keeps raw counts when denomination evidence is missing, ambiguous or invalid and labels disabled slots explicitly', async () => {
  const states = ['missing', 'ambiguous', 'invalid', 'disabled']
  vi.mocked(apiGet).mockResolvedValue({ statuses: states.map((id, index) => ({ id, receivedAtUtc, isCurrent: false,
    status: { ...status, boxNumber: `0000000${index + 1}` } })), collections: [], coinConfigurations: {
    missing: { status: 'missing' }, ambiguous: { status: 'ambiguous' }, invalid: { status: 'invalid' },
    disabled: { ...reference(25, 2), slots: [{ number: 4, valueMinorUnits: 25, validatorWindowEnabled: true, accepted: false }] },
  } })
  render(<MemoryRouter><TerminalCashboxPanel terminalId="fixture-0" /></MemoryRouter>)
  const table = await screen.findByRole('table', { name: 'Latest status by box' })
  const rows = within(table).getAllByRole('row').slice(1)
  expect(within(rows[0]).getByText(/No matching MT50 acknowledgement before receipt/)).toBeVisible()
  expect(within(rows[1]).getByText(/Multiple MT50 acknowledgements/)).toBeVisible()
  expect(within(rows[2]).getByText(/no verified coin-slot layout/)).toBeVisible()
  for (const row of rows.slice(0, 3)) {
    expect(within(row).getByText('Slot 4: 3', { exact: true })).toBeVisible()
    expect(within(row).queryByText(/configured 0/)).not.toBeInTheDocument()
  }
  expect(within(rows[3]).getByText('Slot 4: 3 (configured 0.25, not enabled for acceptance)')).toBeVisible()
})

it('records only an explicitly verified empty box and keeps it separate from terminal reports', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { cashbox: 5 })
  vi.mocked(apiGet).mockResolvedValueOnce({ statuses: [], collections: [], initializations: [], canInitializeEmpty: true })
    .mockResolvedValue({ statuses: [], collections: [], canInitializeEmpty: false, initializations: [{
      id: 'init-1', boxNumber: '012345AB', openingTime: '2028-02-29T10:11:12', recordedAtUtc: receivedAtUtc, isCurrent: true,
    }] })
  vi.mocked(apiPostRaw).mockResolvedValue(new Response('{}', { status: 200 }))
  render(<MemoryRouter><TerminalCashboxPanel terminalId="fixture-0" /></MemoryRouter>)
  const form = await screen.findByRole('region', { name: 'Initialize observed empty box' })
  const submit = within(form).getByRole('button', { name: 'Record empty-box initialization' })
  expect(submit).toBeDisabled()
  fireEvent.change(within(form).getByLabelText('Box identifier (8 hexadecimal digits)'), { target: { value: '012345ab' } })
  fireEvent.change(within(form).getByLabelText('Observed phone opening clock'), { target: { value: '2028-02-29T10:11:12' } })
  expect(submit).toBeDisabled()
  fireEvent.click(within(form).getByRole('checkbox'))
  expect(submit).toBeEnabled()
  fireEvent.click(submit)
  await waitFor(() => expect(apiPostRaw).toHaveBeenCalledWith('/api/terminals/fixture-0/cashbox/initialize-empty', {
    boxNumber: '012345AB', openingClock: '2028-02-29T10:11:12', confirmEmpty: true,
  }))
  const baseline = await screen.findByRole('region', { name: 'Operator-confirmed empty box' })
  expect(within(baseline).getByText(/Starting balance 0.00/)).toBeVisible()
  expect(within(baseline).getByText(/not a terminal report or collection/)).toBeVisible()
  expect(screen.getByText('No cashbox reports yet.')).toBeVisible()
  expect(screen.queryByRole('region', { name: 'Current reported cashbox' })).not.toBeInTheDocument()
})

it('does not expose empty-box initialization to a read-only operator', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { cashbox: 1 })
  vi.mocked(apiGet).mockResolvedValue({ statuses: [], collections: [], initializations: [], canInitializeEmpty: true })
  render(<MemoryRouter><TerminalCashboxPanel terminalId="fixture-0" /></MemoryRouter>)
  await screen.findByText('No cashbox reports yet.')
  expect(screen.queryByRole('region', { name: 'Initialize observed empty box' })).not.toBeInTheDocument()
})
