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

it('schedules the reviewed selected phone and cancels only before possible MT21 application', async () => {
  const mapping = { source: '000001', replacement: '000002', scope: 'all' }
  const row = { id: 'phone-a', displayName: 'Test phone A', currentId: '0000010123', replacementId: '0000020123',
    tableSetId: 'set-fixture', tableSetName: 'Current assignment', tableSetRevision: 1, accessRevision: 3, accessLength: 48,
    sourceHash: 'source-digest', replacementHash: 'revised-digest', eligible: true, issues: [] }
  const item = { id: 'item-a', jobId: 'job-a', terminalId: 'phone-a', sourceWireId: row.currentId, targetWireId: row.replacementId,
    dueAtUtc: '2026-12-01T00:00:00Z', state: 'scheduled', revision: 0, canCancel: true }
  vi.mocked(apiFetch)
    .mockResolvedValueOnce(Response.json({ eligibleCount: 1, blockedCount: 1, scheduled: false, reviewToken: 'review-token',
      targets: [row, { ...row, id: 'phone-b', displayName: 'Test phone B', eligible: false, issues: ['Destination occupied.'] }] }))
    .mockResolvedValueOnce(Response.json({ jobId: 'job-a', items: [item] }, { status: 201 }))
    .mockResolvedValueOnce(Response.json({ ...item, state: 'cancelled', revision: 1, canCancel: false }))
  vi.mocked(apiGet)
    .mockResolvedValueOnce([item, { ...item, id: 'item-b', terminalId: 'phone-b', state: 'uncertain', canCancel: false }])
    .mockResolvedValueOnce([{ ...item, state: 'cancelled', revision: 1, canCancel: false }])

  render(<MemoryRouter><TerminalIdCutoverPage /></MemoryRouter>)
  fireEvent.change(screen.getByLabelText('Existing ID or prefix'), { target: { value: mapping.source } })
  fireEvent.change(screen.getByLabelText('Replacement ID or prefix'), { target: { value: mapping.replacement } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview cutover' }))
  expect(await screen.findByLabelText('Select Test phone A')).toBeChecked()
  expect(screen.getByLabelText('Select Test phone B')).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Schedule cutover for 1 phone' }))
  expect(await screen.findByText(/Scheduled 1 phone/)).toBeVisible()
  expect(apiFetch).toHaveBeenNthCalledWith(2, '/api/terminals/identity-cutover/jobs', expect.objectContaining({
    method: 'POST', body: JSON.stringify({ mapping, terminalIds: ['phone-a'], reviewToken: 'review-token', dueAtUtc: null }),
  }))
  expect(await screen.findByText('Delivery uncertain; replacement reserved')).toBeVisible()
  expect(screen.getAllByRole('button', { name: 'Cancel cutover' })).toHaveLength(1)
  fireEvent.click(screen.getByRole('button', { name: 'Cancel cutover' }))
  expect(await screen.findByText('Cancelled before possible application')).toBeVisible()
  expect(apiFetch).toHaveBeenNthCalledWith(3, '/api/terminals/identity-cutover/jobs/job-a/items/item-a/cancel',
    expect.objectContaining({ method: 'POST', body: JSON.stringify({ revision: 0 }) }))
})

it('requires an explicit compatible assignment choice for an observed replacement identity', async () => {
  const item = { id: 'item-a', jobId: 'job-a', terminalId: 'phone-a', sourceWireId: '0000010123', targetWireId: '0000020123',
    dueAtUtc: '2026-12-01T00:00:00Z', state: 'activation_blocked', revision: 4, canCancel: false,
    newIdentityObservedAtUtc: '2026-12-01T00:05:00Z', activationError: 'The reviewed assignment changed.' }
  vi.mocked(apiGet)
    .mockResolvedValueOnce([item])
    .mockResolvedValueOnce({ revision: 4, assignmentId: 'assignment-a', assignmentRevision: 3,
      currentSetId: 'current', currentSetName: 'New assignment', currentIssue: 'The current access record does not match.',
      stagedSetId: 'staged', stagedSetName: 'Dispatched replacement', stagedIssue: null })
    .mockResolvedValueOnce([{ ...item, state: 'activated', revision: 5, activationError: null }])
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ ...item, state: 'activated', revision: 5 }))

  render(<MemoryRouter><TerminalIdCutoverPage /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Show cutover jobs' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Review reconciliation' }))
  expect(await screen.findByLabelText('Keep current assignment')).toBeDisabled()
  expect(screen.getByText(/Dispatched replacement/)).toBeVisible()
  expect(screen.getByRole('button', { name: 'Confirm reviewed replacement' })).toBeDisabled()
  fireEvent.click(screen.getByLabelText('Use the dispatched replacement set'))
  fireEvent.click(screen.getByRole('button', { name: 'Confirm reviewed replacement' }))
  expect(await screen.findByText('New identity confirmed')).toBeVisible()
  expect(apiFetch).toHaveBeenCalledWith('/api/terminals/identity-cutover/jobs/job-a/items/item-a/reconcile', expect.objectContaining({
    method: 'POST', body: JSON.stringify({ choice: 'staged', revision: 4, assignmentId: 'assignment-a', assignmentRevision: 3 }),
  }))
})
