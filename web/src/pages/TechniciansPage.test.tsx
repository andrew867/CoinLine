import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TechniciansPage } from './TechniciansPage'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn() }))
afterEach(() => { cleanup(); consoleSession.clear(); vi.resetAllMocks() })

const row = { id: 'technician', displayName: 'Fixture technician', employeeReference: 'TEST', enabled: true, revision: 1,
  hasIdentifier: true, matchingAvailable: true }
const activity = { id: 'event', terminalId: 'phone', terminalName: 'Fixture phone', capturedAtUtc: '2026-01-01T12:00:00Z', matchStatus: 'matched',
  technicianId: row.id, technicianName: row.displayName, employeeReference: row.employeeReference, opcode: 996, summary: 'Technician entered maintenance mode (opcode 996)' }

function fixture() {
  vi.mocked(apiGet).mockImplementation(async path => String(path).includes('nearby-alarms')
    ? { items: [{ id: 'alarm', capturedAtUtc: '2026-01-01T11:00:00Z', alarmNumber: 22, summary: 'Memory lost (alarm 22)' }] }
    : String(path).includes('/activity') ? { total: 1, items: [activity] } : { identifierMatchingConfigured: true, items: [row] })
  vi.mocked(apiFetch).mockResolvedValue(Response.json(row))
  render(<MemoryRouter><TechniciansPage /></MemoryRouter>)
}

it('edits a write-only association and shows reported operation and separately labelled nearby alarms', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { technicians: 7, 'message-log': 1 })
  fixture()
  expect(await screen.findByText('Technician entered maintenance mode (opcode 996)')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Edit Fixture technician' }))
  const form = screen.getByRole('form', { name: 'Technician profile' })
  const field = within(form).getByLabelText('Reported Craft identifier (write only)')
  expect(field).toHaveAttribute('type', 'password'); expect(field).toHaveValue('')
  const synthetic = String(crypto.getRandomValues(new Uint32Array(1))[0] % 100000).padStart(5, '0')
  fireEvent.change(field, { target: { value: synthetic } })
  fireEvent.click(within(form).getByRole('button', { name: 'Save technician' }))
  await waitFor(() => expect(apiFetch).toHaveBeenCalledWith('/api/technicians/technician', expect.objectContaining({ method: 'PUT',
    body: expect.stringContaining('"reportedIdentifier":"' + synthetic + '"') })))
  expect(await screen.findByText(/Technician profile saved/)).toBeVisible()
  expect(screen.queryByLabelText('Reported Craft identifier (write only)')).toBeNull()
  fireEvent.change(screen.getByLabelText('Technician'), { target: { value: 'technician' } })
  fireEvent.click(screen.getByRole('button', { name: 'Filter activity' }))
  await waitFor(() => expect(apiGet).toHaveBeenCalledWith('/api/technicians/activity?technicianId=technician'))
  fireEvent.click(screen.getByRole('button', { name: 'Nearby alarms' }))
  expect(await screen.findByRole('link', { name: 'Memory lost (alarm 22)' })).toHaveAttribute('href', '/dlog/alarm')
  expect(screen.getByText(/not confirmed responses or resolved faults/)).toBeVisible()
}, 15000)

it('allows reporting without an edit form for a read-only technician-directory operator', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { technicians: 1 })
  fixture()
  expect(await screen.findByText('Technician entered maintenance mode (opcode 996)')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Add technician' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Edit Fixture technician' })).toBeNull()
  expect(apiFetch).not.toHaveBeenCalled()
})

it('shows the reported opcode and current operator maintenance description together', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { technicians: 1 })
  vi.mocked(apiGet).mockImplementation(async path => String(path).includes('/activity')
    ? { total: 1, items: [{ ...activity, opcode: 106, summary: 'Unlisted maintenance opcode 106', operatorDescription: 'Booth cleaned' }] }
    : { identifierMatchingConfigured: true, items: [row] })
  render(<MemoryRouter><TechniciansPage /></MemoryRouter>)
  expect(await screen.findByText('Booth cleaned (opcode 106)')).toBeVisible()
  expect(screen.getByText('Unlisted maintenance opcode 106')).toBeVisible()
  expect(screen.getByText(/Current operator classification above/)).toBeVisible()
})
