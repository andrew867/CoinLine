import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
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

it('blocks direct entry and makes no lookup for a read-only operator', () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { phones: 1 })
  expect(mayOpenScreen('/terminals/new', 'Operator', { phones: 1 })).toBe(false)
  renderPage()
  expect(screen.getByRole('alert')).toHaveTextContent('Add permission for Phones is required')
  expect(apiGet).not.toHaveBeenCalled()
})
