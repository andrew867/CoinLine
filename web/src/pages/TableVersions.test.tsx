import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { mayOpenScreen } from '../components/Layout'
import { TableVersions } from './TableVersions'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })
const endpoint = '/api/tables/configuration-library'
const options = { records: [{ number: 29, name: 'Advertising' }], sets: [{ id: 'fixture-set', name: 'Fixture set' }],
  firmware: [{ id: 'fixture-firmware', name: 'Fixture firmware' }] }
const row = { id: 'fixture-record', tableSetId: 'fixture-set', setName: 'Fixture set', setStatus: 0, number: 29,
  definitionName: 'Advertising', configurationId: '000099', tableRevision: 2, payloadSha256Hex: 'a'.repeat(64),
  payloadLengthBytes: 481, validationPassed: true, targetFirmwareVersionId: 'fixture-firmware', targetFirmware: 'Fixture firmware',
  recordCountInSet: 1, assignedPhoneCount: 3, nativeLayoutKnown: true }
function show(path = '/table-versions') { render(<MemoryRouter initialEntries={[path]}><TableVersions /></MemoryRouter>) }
function json(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status }) }

it('opens a set-filtered library, searches names and configuration IDs with leading zeroes, and links the exact native editor', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3, 'table-sets': 1, 'table-assignments': 1 })
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path === `${endpoint}/options`) return json(options)
    const query = new URL(path, 'http://localhost').searchParams
    if (query.get('configurationId')) {
      expect(query.get('configurationId')).toBe('000099')
      expect(query.get('number')).toBe('29')
      expect(query.get('firmwareVersionId')).toBe('fixture-firmware')
      expect(query.get('status')).toBe('draft')
    }
    return json({ items: [row], total: 1, page: 1, pageSize: 50, canReadAssignment: true })
  })
  show('/table-versions?set=fixture-set')
  expect(await screen.findByRole('link', { name: 'Open editor' })).toHaveAttribute('href', '/table-sets/fixture-set?record=29#config-heading')
  expect(vi.mocked(apiFetch).mock.calls.some(([path]) => path.includes('tableSetId=fixture-set'))).toBe(true)
  expect(screen.getByRole('columnheader', { name: 'Assigned phones' })).toBeVisible()
  expect(within(screen.getByRole('region', { name: 'Saved configuration records' })).getByText('3')).toBeVisible()
  expect(screen.queryByRole('button', { name: /Review phones for/ })).not.toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Configuration ID'), { target: { value: '000099' } })
  fireEvent.change(screen.getByLabelText('Record type'), { target: { value: '29' } })
  fireEvent.change(screen.getByLabelText('Firmware target'), { target: { value: 'fixture-firmware' } })
  fireEvent.change(screen.getByLabelText('Set status'), { target: { value: 'draft' } })
  fireEvent.click(screen.getByRole('button', { name: 'Search' }))
  expect(await screen.findByRole('link', { name: 'Open editor' })).toBeVisible()
  expect(vi.mocked(apiFetch).mock.calls.some(([path]) => path.includes('configurationId=000099'))).toBe(true)
})

const usageVersion = { id: row.id, tableSetId: row.tableSetId, setName: row.setName, number: row.number,
  tableRevision: row.tableRevision, configurationId: row.configurationId }
const fixturePhone = { terminalId: 'fixture-a', displayName: 'Fixture phone A', terminalIdHex: '0000000001',
  deactivated: false, assignedAtUtc: '2026-10-01T10:00:00Z', lastExactAcknowledgement: { downloadBatchId: 'receipt-a', recordedAtUtc: '2026-10-01T10:01:00Z' } }

it('drills into the selected exact version, separates assignment from historical receipts and pages phones', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 1, 'table-assignments': 1, phones: 1, downloads: 1 })
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path === `${endpoint}/options`) return json(options)
    if (path.startsWith(`${endpoint}/${row.id}/assignments`)) {
      const page = Number(new URL(path, 'http://localhost').searchParams.get('page'))
      return json({ version: usageVersion, total: 51, page, pageSize: 50, canReadDownloads: true,
        items: page === 1 ? [fixturePhone, { ...fixturePhone, terminalId: 'fixture-b', displayName: 'Fixture phone B', deactivated: true, lastExactAcknowledgement: null }]
          : [{ ...fixturePhone, terminalId: 'fixture-c', displayName: 'Fixture phone C', lastExactAcknowledgement: null }] })
    }
    if (path.includes('/assignments')) throw new Error('Wrong exact version selected')
    return json({ items: [row, { ...row, id: 'copy-version', tableSetId: 'copy-set', setName: 'Other copy' }], total: 2, page: 1, pageSize: 50, canReadAssignment: true })
  })
  show()
  fireEvent.click(await screen.findByRole('button', { name: 'Review phones for MT29 000099, Fixture set, revision 2' }))
  const panel = await screen.findByRole('region', { name: 'Assigned phones and delivery evidence' })
  expect(await within(panel).findByRole('link', { name: 'Fixture phone A' })).toHaveAttribute('href', '/terminals/fixture-a')
  expect(within(panel).getByRole('link', { name: 'View matched receipt' })).toHaveAttribute('href', '/downloads/receipt-a')
  expect(within(panel).getByText('Inactive')).toBeVisible()
  expect(within(panel).getByText('No exact version acknowledgement recorded')).toBeVisible()
  expect(within(panel).getByText(/not terminal memory readback/)).toBeVisible()
  expect(within(panel).getByText('51 assigned phones')).toBeVisible()
  fireEvent.click(within(panel).getByRole('button', { name: 'Next usage page' }))
  expect(await within(panel).findByRole('link', { name: 'Fixture phone C' })).toBeVisible()
  expect(within(panel).queryByRole('link', { name: 'Fixture phone A' })).not.toBeInTheDocument()
  expect(within(panel).getByRole('button', { name: 'Next usage page' })).toBeDisabled()
  expect(vi.mocked(apiFetch).mock.calls.some(([path]) => path === `${endpoint}/${row.id}/assignments?page=2`)).toBe(true)
  fireEvent.click(within(panel).getByRole('button', { name: 'Close usage' }))
  expect(screen.queryByRole('region', { name: 'Assigned phones and delivery evidence' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Review phones for MT29 000099, Fixture set, revision 2' })).toHaveFocus()
})

it('shows usage errors, refreshes them and does not expose receipt information without Downloads Read', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 1, 'table-assignments': 1, phones: 1 })
  let attempts = 0
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path === `${endpoint}/options`) return json(options)
    if (path.includes('/assignments')) return ++attempts === 1 ? json({ error: 'Usage unavailable' }, 500) : json({ version: usageVersion,
      total: 1, page: 1, pageSize: 50, canReadDownloads: false, items: [{ ...fixturePhone, lastExactAcknowledgement: null }] })
    return json({ items: [row], total: 1, page: 1, pageSize: 50, canReadAssignment: true })
  })
  show()
  fireEvent.click(await screen.findByRole('button', { name: /Review phones for MT29/ }))
  const panel = await screen.findByRole('region', { name: 'Assigned phones and delivery evidence' })
  expect(await within(panel).findByRole('alert')).toHaveTextContent('Usage unavailable')
  expect(within(panel).queryByText(/No phones are assigned/)).not.toBeInTheDocument()
  fireEvent.click(within(panel).getByRole('button', { name: 'Refresh usage' }))
  expect(await within(panel).findByRole('link', { name: 'Fixture phone A' })).toBeVisible()
  expect(within(panel).getByText(/Download evidence is unavailable/)).toBeVisible()
  expect(within(panel).queryByRole('columnheader', { name: 'Last exact version acknowledgement' })).not.toBeInTheDocument()
  expect(within(panel).queryByRole('link', { name: 'View matched receipt' })).not.toBeInTheDocument()
})

it('keeps pagination and ambiguity visible without providing restricted assignment counts or editor links', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 1 })
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path === `${endpoint}/options`) return json(options)
    const page = Number(new URL(path, 'http://localhost').searchParams.get('page') ?? 1)
    return json({ items: [{ ...row, id: `record-${page}`, recordCountInSet: 2, assignedPhoneCount: null }], total: 51, page, pageSize: 50, canReadAssignment: false })
  })
  show()
  expect(await screen.findByText('Multiple records of this type in the set')).toBeVisible()
  expect(screen.queryByRole('columnheader', { name: 'Assigned phones' })).toBeNull()
  expect(screen.queryByRole('link', { name: 'Open editor' })).toBeNull()
  expect(screen.queryByRole('link', { name: 'View record' })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
  expect(await screen.findByText('Page 2 of 2')).toBeVisible()
  expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled()
})

it('shows request errors rather than an empty library and hides it from a write-only operator', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 1 })
  vi.mocked(apiFetch).mockImplementation(async path => path === `${endpoint}/options` ? json(options) : json({ error: 'Search unavailable' }, 500))
  show()
  expect(await screen.findByRole('alert')).toHaveTextContent('Configuration search failed: Search unavailable')
  expect(screen.queryByText('No records match this search.')).toBeNull()
  cleanup(); vi.resetAllMocks()
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 2 })
  expect(mayOpenScreen('/table-versions', 'Operator', { 'table-records': 2 })).toBe(false)
  show()
  expect(screen.getByRole('alert')).toHaveTextContent('You do not have permission')
  expect(apiFetch).not.toHaveBeenCalled()
})

it('does not direct an unsupported record to an unrelated native editor', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3, 'table-sets': 1 })
  vi.mocked(apiFetch).mockImplementation(async path => path === `${endpoint}/options` ? json(options) :
    json({ items: [{ ...row, nativeLayoutKnown: false, payloadLengthBytes: 3 }], total: 1, page: 1, pageSize: 50, canReadAssignment: false }))
  show()
  expect(await screen.findByText('No matching native layout')).toBeVisible()
  expect(screen.queryByRole('link', { name: 'Open editor' })).toBeNull()
  expect(screen.getByRole('link', { name: 'Fixture set' })).toBeVisible()
})
