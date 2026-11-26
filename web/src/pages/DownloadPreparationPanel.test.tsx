import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { DownloadsPage } from './DownloadsPage'
import { DownloadPreparationPanel } from './DownloadPreparationPanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPostRaw: vi.fn(), apiPostBare: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const first = { id: 'phone-one', terminalIdHex: '0102030401', displayName: 'Lobby phone', active: true }
const second = { id: 'phone-two', terminalIdHex: '0102030402', displayName: 'Shop phone', active: true }
const blocked = { id: 'phone-locked', terminalIdHex: '0102030403', displayName: 'Closed site', active: false }
const reviewRow = (phone: typeof first, tableSetName: string, numbers: number[], reason?: string) => ({
  terminalId: phone.id, terminalIdHex: phone.terminalIdHex, displayName: phone.displayName, tableSetId: 'set-' + phone.id,
  tableSetName, records: numbers.map(tableNumber => ({ tableNumber, versionId: 'record-' + tableNumber, payloadSha256Hex: 'fixture' })),
  eligible: !reason, reason: reason ?? null,
})

it('retains a manually selected phone list across searches, reviews different sets and confirms only the reviewed phones', async () => {
  vi.mocked(apiGet).mockImplementation(async path => ({ phones: path.includes('query=Shop') ? [second] : [first, blocked], more: false, maximumPhones: 40 }) as never)
  const onPrepared = vi.fn()
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ phones: [reviewRow(first, 'Lobby configuration', [29, 50]),
    reviewRow(blocked, 'Closed configuration', [], 'Phone is deactivated.'), reviewRow(second, 'Shop configuration', [29])],
    eligibleCount: 2, preparationAtUtc: null, previewToken: 'blocked-token' }))
    .mockResolvedValueOnce(Response.json({ phones: [reviewRow(first, 'Lobby configuration', [29, 50]), reviewRow(second, 'Shop configuration', [29])],
      eligibleCount: 2, preparationAtUtc: null, previewToken: 'reviewed-token' }))
    .mockResolvedValueOnce(Response.json({ downloads: [
      { terminalId: first.id, terminalIdHex: first.terminalIdHex, displayName: first.displayName, tableSetName: 'Lobby configuration', scheduleId: 'schedule-1', batchId: 'batch-1' },
      { terminalId: second.id, terminalIdHex: second.terminalIdHex, displayName: second.displayName, tableSetName: 'Shop configuration', scheduleId: 'schedule-2', batchId: 'batch-2' },
    ] }))
  render(<MemoryRouter><DownloadPreparationPanel onPrepared={onPrepared} /></MemoryRouter>)
  fireEvent.click(await screen.findByRole('button', { name: /Add Lobby phone/ }))
  fireEvent.click(screen.getByRole('button', { name: /Add Closed site/ }))
  expect(screen.getByRole('button', { name: /Add Lobby phone/ })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Search by phone ID or name'), { target: { value: 'Shop' } })
  fireEvent.click(screen.getByRole('button', { name: 'Search phones' }))
  fireEvent.click(await screen.findByRole('button', { name: /Add Shop phone/ }))
  expect(within(screen.getByRole('list', { name: 'Selected phones' })).getAllByRole('listitem')).toHaveLength(3)
  expect(apiPostRaw).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Review selected phones' }))
  expect(await screen.findByText('Phone is deactivated.')).toBeVisible()
  expect(screen.getByRole('button', { name: 'Confirm preparation for 3 phones' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: /Remove Closed site/ }))
  expect(screen.queryByRole('button', { name: 'Confirm preparation for 2 phones' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Review selected phones' }))
  expect(await screen.findByText('MT29, MT50')).toBeVisible()
  expect(screen.getByText('Lobby configuration')).toBeVisible()
  expect(screen.getByText('Shop configuration')).toBeVisible()
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/downloads/preparation/preview', { terminalIds: [first.id, second.id], availableAtUtc: null })
  fireEvent.click(screen.getByRole('button', { name: 'Confirm preparation for 2 phones' }))
  expect(await screen.findByText('2 download schedules created')).toBeVisible()
  expect(screen.getAllByRole('link', { name: 'Open prepared batch' }).map(node => node.getAttribute('href'))).toEqual(['/downloads/batch-1', '/downloads/batch-2'])
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/downloads/preparation', { terminalIds: [first.id, second.id], availableAtUtc: null, previewToken: 'reviewed-token' })
  expect(onPrepared).toHaveBeenCalledOnce()
})

it('reviews a future UTC preparation time and invalidates stale confirmation after a change', async () => {
  vi.mocked(apiGet).mockResolvedValue({ phones: [first], more: false, maximumPhones: 40 })
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ phones: [reviewRow(first, 'Lobby configuration', [29])], eligibleCount: 1,
    preparationAtUtc: '2031-06-01T12:30:00Z', previewToken: 'future-review' }))
    .mockResolvedValueOnce(Response.json({ phones: [reviewRow(first, 'Lobby configuration', [29])], eligibleCount: 1,
      preparationAtUtc: '2031-06-01T13:00:00Z', previewToken: 'new-review' }))
    .mockResolvedValueOnce(Response.json({ error: 'The phone list, assignment or existing work changed. Review the downloads again.' }, { status: 409 }))
  render(<MemoryRouter><DownloadPreparationPanel onPrepared={vi.fn()} /></MemoryRouter>)
  fireEvent.click(await screen.findByRole('button', { name: /Add Lobby phone/ }))
  fireEvent.click(screen.getByRole('radio', { name: 'Prepare later' }))
  expect(screen.getByRole('button', { name: 'Review selected phones' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Preparation time (UTC)'), { target: { value: '2031-06-01T12:30' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review selected phones' }))
  expect(await screen.findByText(/Prepare at 2031-06-01 12:30 UTC/)).toBeVisible()
  fireEvent.change(screen.getByLabelText('Preparation time (UTC)'), { target: { value: '2031-06-01T13:00' } })
  expect(screen.queryByRole('button', { name: 'Confirm preparation for 1 phone' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Review selected phones' }))
  expect(await screen.findByText(/Prepare at 2031-06-01 13:00 UTC/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Confirm preparation for 1 phone' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Review the downloads again.')
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/downloads/preparation', { terminalIds: [first.id], availableAtUtc: '2031-06-01T13:00:00.000Z', previewToken: 'new-review' })
  expect(screen.queryByText('1 download schedule created')).not.toBeInTheDocument()
})


it('lets an Add-only download operator use the minimal preparation workflow without requesting denied registries or history', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { downloads: 4 })
  vi.mocked(apiGet).mockResolvedValue({ phones: [first], more: false, maximumPhones: 40 })
  render(<MemoryRouter><DownloadsPage /></MemoryRouter>)
  expect(screen.queryByRole('heading', { name: 'Start download' })).toBeNull()
  expect(screen.queryByRole('heading', { name: 'Recent batches' })).toBeNull()
  expect(apiFetch).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Prepare downloads for multiple phones' }))
  expect(await screen.findByRole('button', { name: /Add Lobby phone/ })).toBeVisible()
  expect(apiGet).toHaveBeenCalledExactlyOnceWith('/api/downloads/preparation/options?query=')
  expect(apiFetch).not.toHaveBeenCalled()
})

it('does not offer a new preparation or single-phone mutation to a read-only download operator', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { downloads: 1 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json([]))
  vi.mocked(apiGet).mockResolvedValue([])
  render(<MemoryRouter><DownloadsPage /></MemoryRouter>)
  expect(await screen.findByText('No download batches yet.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Prepare downloads for multiple phones' })).toBeNull()
  expect(screen.queryByRole('heading', { name: 'Start download' })).toBeNull()
  expect(apiFetch).toHaveBeenCalledExactlyOnceWith('/api/downloads')
  expect(apiGet).toHaveBeenCalledExactlyOnceWith('/api/tables/schedules')
})
