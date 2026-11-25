import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TerminalIdCutoverPage } from './TerminalIdCutoverPage'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn() }))
afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

it('shows exact prefix suffix preservation, blocked phones and read-only inventory review', async () => {
  vi.mocked(apiGet).mockResolvedValue([{ id: 'group-fixture', name: 'Test group' }])
  const base = { tableSetId: 'set-fixture', tableSetName: 'Current assignment', tableSetRevision: 1,
    accessRevision: 3, accessLength: 48, sourceHash: 'source-digest', replacementHash: 'revised-digest' }
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ eligibleCount: 1, blockedCount: 1, scheduled: false,
    targets: [
      { ...base, id: 'phone-a', displayName: 'Test phone A', currentId: '0000010123', replacementId: '0000020123', eligible: true, issues: [] },
      { ...base, id: 'phone-b', displayName: 'Test phone B', currentId: '0000010124', replacementId: '0000020124', eligible: false,
        issues: ['Another phone already uses the replacement ID.'] },
    ] }))
  render(<MemoryRouter><TerminalIdCutoverPage /></MemoryRouter>)
  fireEvent.change(screen.getByLabelText('Existing ID or prefix'), { target: { value: '000001' } })
  fireEvent.change(screen.getByLabelText('Replacement ID or prefix'), { target: { value: '000002' } })
  fireEvent.change(screen.getByLabelText('Inventory scope'), { target: { value: 'group' } })
  await screen.findByRole('option', { name: 'Test group' })
  expect(screen.getByRole('button', { name: 'Preview cutover' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Phone group'), { target: { value: 'group-fixture' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview cutover' }))
  expect(await screen.findByRole('status')).toHaveTextContent('1 eligible, 1 blocked. Nothing scheduled or changed.')
  expect(screen.getByText('0000020123')).toBeVisible()
  expect(screen.getByText('Another phone already uses the replacement ID.')).toBeVisible()
  expect(screen.getByRole('link', { name: 'Test phone A' })).toHaveAttribute('href', '/terminals/phone-a')
  expect(apiFetch).toHaveBeenCalledWith('/api/terminals/identity-cutover/preview?source=000001&replacement=000002&scope=group&targetId=group-fixture')
  fireEvent.change(screen.getByLabelText('Replacement ID or prefix'), { target: { value: '000003' } })
  expect(screen.queryByRole('status')).toBeNull()
})

it('rejects mixed-length mappings and displays an API failure rather than a zero result', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ error: 'Inventory cannot be reviewed right now.' }, { status: 409 }))
  render(<MemoryRouter><TerminalIdCutoverPage /></MemoryRouter>)
  fireEvent.change(screen.getByLabelText('Existing ID or prefix'), { target: { value: '000001' } })
  fireEvent.change(screen.getByLabelText('Replacement ID or prefix'), { target: { value: '0000020123' } })
  expect(screen.getByRole('button', { name: 'Preview cutover' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Replacement ID or prefix'), { target: { value: '000002' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview cutover' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Inventory cannot be reviewed right now.')
  expect(screen.queryByRole('status')).toBeNull()
})
