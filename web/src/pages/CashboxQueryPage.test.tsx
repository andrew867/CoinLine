import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { mayOpenScreen } from '../components/Layout'
import { CashboxQueryLink, CashboxQueryPage } from './CashboxQueryPage'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiFetch: vi.fn() }))

const phones = ['Alpha', 'Beta', 'Gamma'].map((displayName, index) => ({
  id: `fixture-${index}`, displayName, terminalIdHex: `000000000${index + 1}`,
}))
const receivedAtUtc = '2028-10-01T12:00:00Z'
const terminalTime = '2028-10-01T08:00:00'
const status = { boxNumber: '00000001', terminalTime, balanceMinorUnits: 75, percentFull: 40,
  condition: 'Value threshold exceeded', unmappedSetBitCount: 1, coinCounts: [0, 0, 0, 3] }
const firstLedger = { statuses: [{ id: 'status-a', receivedAtUtc, status, isCurrent: true }], collections: [] }
const secondLedger = { statuses: [{ id: 'status-b', receivedAtUtc, status, isCurrent: false }], collections: [{
  id: 'collection', receivedAtUtc, terminalTime, boxNumber: '00000001', newBoxNumber: '00000002',
  totalMinorUnits: 125, removedBoxBalanceMinorUnits: 75, isSummed: true, coinCounts: [0, 0, 0, 3],
}] }

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
  expect(screen.getByRole('button', { name: 'Find previous' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Find next' }))
  expect(await screen.findByText(/No current box status is available/)).toBeVisible()
  expect(within(screen.getByRole('table', { name: 'Reported collections' })).getByText('1.25 (combined)')).toBeVisible()
  expect(screen.queryByRole('region', { name: 'Current reported cashbox' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Find next' }))
  expect(await screen.findByText('No cashbox reports yet.')).toBeVisible()
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
