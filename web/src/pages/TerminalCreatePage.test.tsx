import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { readDownloadReviews } from '../api/downloadReviews'
import { mayOpenScreen } from '../components/Layout'
import { TerminalCreatePage } from './TerminalCreatePage'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

const options = {
  sites: [
    { id: 'site-one', name: 'Fixture site', code: 'ONE', customerId: 'customer-one' },
    { id: 'site-two', name: 'Other site', code: 'TWO', customerId: 'customer-two' },
  ],
  groups: [],
  firmwareVersions: [{ id: 'firmware-one', label: 'Fixture firmware', buildId: null }],
  families: [{ id: 'mtr19-multipay', release: 'MTR 1.9', variant: 'Multi-pay', maximizerTypeNumbers: [28] }],
  canAssignGroup: false,
}

function renderPage() {
  render(<MemoryRouter><TerminalCreatePage /></MemoryRouter>)
}

it('registers a phone with Add-only access using registration choices instead of the unrestricted catalogues', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { phones: 4 })
  expect(mayOpenScreen('/terminals/new', 'Operator', { phones: 4 })).toBe(true)
  vi.mocked(apiGet).mockResolvedValue(options)
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ id: 'fixture-phone' }, { status: 201 }))
  renderPage()
  expect(await screen.findByRole('option', { name: 'Fixture site (ONE)' })).toBeVisible()
  expect(screen.queryByLabelText('Phone group')).toBeNull()
  fireEvent.change(screen.getByLabelText('Installed firmware'), { target: { value: 'firmware-one' } })
  fireEvent.change(screen.getByLabelText('Terminal family'), { target: { value: 'mtr19-multipay' } })
  fireEvent.change(screen.getByTestId('terminal-create-hex'), { target: { value: '0000000101' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create terminal' }))
  expect(await screen.findByText('Phone registered. You can register another phone.')).toBeVisible()
  expect(apiGet).toHaveBeenCalledTimes(1)
  expect(apiGet).toHaveBeenCalledWith('/api/terminals/create-options')
  expect(apiPostRaw).toHaveBeenCalledWith('/api/terminals', expect.objectContaining({
    siteId: 'site-one', terminalGroupId: null, firmwareVersionId: 'firmware-one',
    terminalFamilyId: 'mtr19-multipay', terminalIdHex: '0000000101',
  }))
  expect(screen.getByTestId('terminal-create-hex')).toHaveValue('')
  expect(readDownloadReviews()).toEqual([])
})

it('filters group choices by the selected site customer and clears an incompatible selection', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { phones: 4, memberships: 4 })
  vi.mocked(apiGet).mockResolvedValue({ ...options, canAssignGroup: true, groups: [
    { id: 'group-one', name: 'First group', customerId: 'customer-one' },
    { id: 'group-two', name: 'Second group', customerId: 'customer-two' },
    { id: 'global', name: 'Global group', customerId: null },
  ] })
  renderPage()
  expect(await screen.findByRole('option', { name: 'First group' })).toBeVisible()
  expect(screen.queryByRole('option', { name: 'Second group' })).toBeNull()
  fireEvent.change(screen.getByLabelText('Phone group'), { target: { value: 'group-one' } })
  fireEvent.change(screen.getByTestId('terminal-create-site'), { target: { value: 'site-two' } })
  expect(screen.queryByRole('option', { name: 'First group' })).toBeNull()
  expect(screen.getByRole('option', { name: 'Second group' })).toBeVisible()
  expect(screen.getByRole('option', { name: 'Global group' })).toBeVisible()
  expect(screen.getByLabelText('Phone group')).toHaveValue('')
})

it('registers multiple group memberships atomically with the chosen primary, customer filtering and membership limit', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { phones: 4, memberships: 4 })
  vi.mocked(apiGet).mockResolvedValue({ ...options, canAssignGroup: true, maximumGroups: 3, groups: [
    { id: 'group-one', name: 'First group', customerId: 'customer-one' },
    { id: 'group-two', name: 'Second group', customerId: 'customer-two' },
    { id: 'global-one', name: 'Shared one', customerId: null },
    { id: 'global-two', name: 'Shared two', customerId: null },
    { id: 'global-three', name: 'Shared three', customerId: null },
  ] })
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ id: 'fixture-phone' }, { status: 201 }))
  renderPage()
  await screen.findByRole('option', { name: 'First group' })
  const primary = screen.getByLabelText('Phone group')
  fireEvent.change(primary, { target: { value: 'group-one' } })
  function add(id: string) {
    fireEvent.change(screen.getByLabelText('Additional group'), { target: { value: id } })
    fireEvent.click(screen.getByRole('button', { name: 'Add group' }))
  }
  add('global-one'); add('global-two')
  expect(screen.getByText('Phone groups (3 of 3)')).toBeVisible()
  expect(screen.getByRole('button', { name: 'Add group' })).toBeDisabled()
  expect(within(screen.getByRole('list', { name: 'Additional phone groups' })).getAllByRole('listitem')).toHaveLength(2)
  fireEvent.change(primary, { target: { value: 'global-one' } })
  expect(within(screen.getByRole('list', { name: 'Additional phone groups' })).getByText('First group')).toBeVisible()
  fireEvent.change(screen.getByTestId('terminal-create-site'), { target: { value: 'site-two' } })
  expect(primary).toHaveValue('global-one')
  expect(screen.getByText('Phone groups (2 of 3)')).toBeVisible()
  add('group-two')
  expect(apiPostRaw).not.toHaveBeenCalled()
  fireEvent.change(screen.getByTestId('terminal-create-hex'), { target: { value: '0000000101' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create terminal' }))
  expect(await screen.findByText('Phone registered. You can register another phone.')).toBeVisible()
  expect(apiPostRaw).toHaveBeenCalledExactlyOnceWith('/api/terminals', expect.objectContaining({
    siteId: 'site-two', terminalGroupId: 'global-one', groupIds: ['global-one', 'global-two', 'group-two'], terminalIdHex: '0000000101',
  }))
})

it('retains a new-phone review after navigating to the registered phone, without scheduling a download', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { phones: 5 })
  vi.mocked(apiGet).mockResolvedValue(options)
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ id: 'registered-fixture' }, { status: 201 }))
  render(<MemoryRouter initialEntries={['/terminals/new']}><Routes>
    <Route path="/terminals/new" element={<TerminalCreatePage />} />
    <Route path="/terminals/:id" element={<h1>Registered phone</h1>} />
  </Routes></MemoryRouter>)
  await screen.findByRole('option', { name: 'Fixture site (ONE)' })
  fireEvent.change(screen.getByTestId('terminal-create-hex'), { target: { value: '0000000101' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create terminal' }))
  expect(await screen.findByRole('heading', { name: 'Registered phone' })).toBeVisible()
  expect(readDownloadReviews()).toEqual([{ terminalId: 'registered-fixture', tableSetId: null, reason: 'registration' }])
  expect(apiPostRaw).toHaveBeenCalledTimes(1)
})

it('blocks direct entry and makes no lookup for a read-only operator', () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { phones: 1 })
  expect(mayOpenScreen('/terminals/new', 'Operator', { phones: 1 })).toBe(false)
  renderPage()
  expect(screen.getByRole('alert')).toHaveTextContent('Add permission for Phones is required')
  expect(apiGet).not.toHaveBeenCalled()
})
