import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TerminalDetail } from './TerminalDetail'

vi.mock('../api/client', () => ({
  apiFetch: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn(), apiPostBare: vi.fn(), apiPostBareOk: vi.fn(), apiPut: vi.fn(),
  apiPostRaw: vi.fn(),
}))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

const phonePath = '/api/terminals/fixture-phone'
const phone = {
  id: 'fixture-phone', siteId: 'fixture-site', displayName: 'Fixture phone', terminalIdHex: '0000000101',
  status: 1, firmwareVersionId: 'fixture-firmware', terminalFamilyId: 'mtr19-multipay',
}
const set = { id: 'fixture-set', name: 'Fixture set', status: 1, targetFirmwareVersionId: null, targetFirmware: null }
const assignment = {
  terminalId: phone.id, tableSetId: set.id, tableSetName: set.name, previousTableSetId: null,
  siteId: phone.siteId, customerId: null, assignedAtUtc: '2028-02-01T12:00:00Z',
}
const defaultLookups = {
  '/api/terminals/firmware-options': [{ id: phone.firmwareVersionId, label: 'Fixture firmware', buildId: null }],
  '/api/terminals/families': [{ id: phone.terminalFamilyId, release: 'MTR 1.9', variant: 'Multi-pay', maximizerTypeNumbers: [28] }],
  [`${phonePath}/identity`]: { terminalIdHex: phone.terminalIdHex, previousId: null, previousType: null, firstContactAtUtc: null },
}

function mockReads(extra: Record<string, unknown> = {}) {
  const values: Record<string, unknown> = { ...defaultLookups, ...extra }
  vi.mocked(apiGet).mockImplementation(async path => {
    if (!(String(path) in values)) throw new Error(`Unexpected read ${path}`)
    return values[String(path)] as never
  })
}

function renderPhone() {
  render(<MemoryRouter initialEntries={['/terminals/fixture-phone']}>
    <Routes><Route path="/terminals/:id" element={<TerminalDetail />} /></Routes>
  </MemoryRouter>)
}

it('does not load denied screen data and presents permitted read-only records without mutation controls', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { phones: 1, inventory: 1, 'table-assignments': 1 })
  mockReads({
    [`${phonePath}/inventory`]: { location: 'Fixture site', customer: 'Fixture customer', serialNumber: null,
      serialReportedAtUtc: null, profile: {}, revision: null },
  })
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path === phonePath) return Response.json(phone)
    if (path === `${phonePath}/table-assignment`) return Response.json(assignment)
    throw new Error(`Unexpected request ${path}`)
  })
  renderPhone()
  expect(await screen.findByRole('heading', { name: phone.displayName })).toBeVisible()
  expect(await screen.findByText(set.name)).toBeVisible()
  expect(await screen.findByRole('button', { name: 'Edit inventory' })).toBeDisabled()
  expect(await screen.findByRole('button', { name: 'Edit terminal ID' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Save firmware' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Save family' })).toBeDisabled()
  expect(screen.queryByRole('button', { name: 'Assign set' })).toBeNull()
  expect(screen.queryByRole('link', { name: set.name })).toBeNull()
  expect(screen.queryByRole('heading', { name: 'Downloads' })).toBeNull()
  expect(screen.queryByRole('heading', { name: 'Cashbox' })).toBeNull()
  expect(screen.queryByRole('heading', { name: 'Special callback' })).toBeNull()
  expect(screen.queryByRole('heading', { name: 'Field diagnostics' })).toBeNull()
  expect(vi.mocked(apiFetch).mock.calls.map(call => call[0])).toEqual([phonePath, `${phonePath}/table-assignment`])
  expect(new Set(vi.mocked(apiGet).mock.calls.map(call => call[0]))).toEqual(new Set([
    ...Object.keys(defaultLookups), `${phonePath}/inventory`,
  ]))
})

it('uses the narrow assignment lookup and permits an assignment without table-set authoring access', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { phones: 1, 'table-assignments': 3 })
  mockReads({ [`${phonePath}/table-assignment-options`]: [set] })
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path === phonePath) return Response.json(phone)
    if (path === `${phonePath}/table-assignment`) return Response.json(assignment)
    throw new Error(`Unexpected request ${path}`)
  })
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({}))
  renderPhone()
  await screen.findByRole('option', { name: 'Fixture set (published)' })
  expect(screen.getByRole('button', { name: 'Assign set' })).toBeEnabled()
  fireEvent.click(screen.getByRole('button', { name: 'Assign set' }))
  await waitFor(() => expect(apiPostRaw).toHaveBeenCalledWith(`${phonePath}/table-assignment`, {
    tableSetId: set.id, customerId: null, siteId: null,
  }))
  expect(apiGet).not.toHaveBeenCalledWith('/api/tables/sets')
})

it('excludes incompatible published sets from defaults and shows the reason for a stale rejected assignment', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { phones: 1, 'table-assignments': 3 })
  const reason = 'MT92. The selected firmware uses 3,061 bytes. The record has 3,401.'
  mockReads({ [`${phonePath}/table-assignment-options`]: [
    { ...set, id: 'wrong', name: 'Older screening', compatibilityIssue: reason }, set,
  ] })
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path === phonePath) return Response.json(phone)
    if (path === `${phonePath}/table-assignment`) return Response.json(assignment)
    throw new Error(`Unexpected request ${path}`)
  })
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ error: reason }, { status: 400 }))
  renderPhone()
  expect(await screen.findByRole('option', { name: `Older screening (${reason})` })).toBeDisabled()
  expect(screen.getByLabelText('Table set')).toHaveValue(set.id)
  fireEvent.click(screen.getByRole('button', { name: 'Assign set' }))
  expect(await screen.findByRole('alert')).toHaveTextContent(reason)
})

it('retains the readable assignment and callback but disables new actions for an inactive phone', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', {
    phones: 1, 'table-assignments': 3, callbacks: 3, uploads: 4, downloads: 4,
  })
  mockReads({
    [`${phonePath}/table-assignment-options`]: [set],
    [`${phonePath}/special-callback`]: { callback: { revision: 1, requestedAtUtc: '2028-02-01T12:00:00Z',
      dueAtUtc: null, terminalClockOffsetMinutes: 0, deliverAssignedTables: false,
      programmedForUtc: null, acknowledgedAtUtc: null, returnedAtUtc: null, cancelledAtUtc: null } },
  })
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path === phonePath) return Response.json({ ...phone, deactivatedAtUtc: '2028-02-01T12:00:00Z' })
    if (path === `${phonePath}/table-assignment`) return Response.json({ ...assignment, previousTableSetId: 'previous' })
    throw new Error(`Unexpected request ${path}`)
  })
  renderPhone()
  expect(await screen.findByRole('button', { name: 'Replace callback' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Cancel callback' })).toBeEnabled()
  expect(screen.getByRole('button', { name: 'Assign set' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Rollback to previous set' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Request CDR upload (intent)' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Request table reload (intent)' })).toBeDisabled()
  expect(screen.queryByRole('button', { name: 'Save diagnostic snapshot' })).toBeNull()
  expect(apiGet).not.toHaveBeenCalledWith(`${phonePath}/diagnostics`)
})
