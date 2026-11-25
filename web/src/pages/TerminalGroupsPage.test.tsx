import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { mayOpenScreen } from '../components/Layout'
import { TerminalGroupsPage } from './TerminalGroupsPage'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn() }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

const group = { id: 'fixture-group', name: 'Fixture group' }
const member = { id: 'existing-phone', displayName: 'Existing member', terminalIdHex: '0000000101',
  revision: 2, member: true, primary: true, eligible: true, atLimit: false }
const candidate = { id: 'candidate-phone', displayName: 'Candidate phone', terminalIdHex: '0000000102',
  revision: 4, member: false, primary: false, eligible: true, atLimit: false }

it('allows membership additions without group administration or membership removal', async () => {
  consoleSession.save('fixture', '', 'Fixture', 'Operator', { memberships: 5 })
  expect(mayOpenScreen('/terminal-groups', 'Operator', { memberships: 5 })).toBe(true)
  vi.mocked(apiFetch).mockImplementation(async (path, init) => {
    if (path === '/api/terminal-groups/membership-options') return Response.json([group])
    if (path === '/api/terminal-groups/fixture-group/phones' && init?.method === 'PUT')
      return Response.json({ changed: 1 })
    throw new Error(`Unexpected request ${path}`)
  })
  vi.mocked(apiGet).mockResolvedValueOnce([member, candidate]).mockResolvedValueOnce([member, { ...candidate, member: true }])
  render(<MemoryRouter><TerminalGroupsPage /></MemoryRouter>)
  await screen.findByRole('option', { name: group.name })
  expect(screen.queryByRole('button', { name: 'Add group' })).toBeNull()
  expect(apiFetch).not.toHaveBeenCalledWith('/api/terminal-groups')
  fireEvent.change(screen.getByLabelText('Manage group memberships'), { target: { value: group.id } })
  expect(await screen.findByRole('checkbox', { name: /Membership for Existing member/ })).toBeDisabled()
  fireEvent.click(screen.getByLabelText('Show members only'))
  const addition = screen.getByRole('checkbox', { name: /Membership for Candidate phone/ })
  expect(addition).toBeEnabled()
  fireEvent.click(addition)
  fireEvent.click(screen.getByRole('button', { name: 'Save 1 membership change' }))
  await screen.findByText('1 phone memberships updated.')
  expect(apiFetch).toHaveBeenCalledWith('/api/terminal-groups/fixture-group/phones', expect.objectContaining({
    method: 'PUT', body: JSON.stringify([{ terminalId: candidate.id, revision: candidate.revision, member: true }]),
  }))
  await waitFor(() => expect(screen.getByRole('checkbox', { name: /Membership for Candidate phone/ })).toBeDisabled())
})

it('keeps group records read-only and skips unavailable lookups', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { groups: 1 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json([{ ...group, code: 'ABC', description: 'Example',
    customerId: null, terminalCount: 1, revision: 2 }]))
  render(<MemoryRouter><TerminalGroupsPage /></MemoryRouter>)
  expect(await screen.findByLabelText('Name for Fixture group')).toHaveAttribute('readonly')
  expect(screen.getByRole('button', { name: 'Save group' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Remove' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Manage phones in Fixture group' })).toBeDisabled()
  expect(screen.queryByRole('button', { name: 'Add group' })).toBeNull()
  expect(apiFetch).toHaveBeenCalledTimes(1)
  expect(apiFetch).toHaveBeenCalledWith('/api/terminal-groups')
})

it('lets a delete-only membership operator remove, but not add or copy phones', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { memberships: 9 })
  vi.mocked(apiFetch).mockImplementation(async (path, init) => {
    if (path === '/api/terminal-groups/membership-options') return Response.json([group])
    if (path === '/api/terminal-groups/fixture-group/phones' && init?.method === 'PUT')
      return Response.json({ changed: 1 })
    throw new Error(`Unexpected request ${path}`)
  })
  vi.mocked(apiGet).mockResolvedValueOnce([member, candidate]).mockResolvedValueOnce([{ ...member, member: false }, candidate])
  render(<MemoryRouter><TerminalGroupsPage /></MemoryRouter>)
  await screen.findByRole('option', { name: group.name })
  fireEvent.change(screen.getByLabelText('Manage group memberships'), { target: { value: group.id } })
  const removal = await screen.findByRole('checkbox', { name: /Membership for Existing member/ })
  expect(removal).toBeEnabled()
  expect(screen.queryByLabelText('Copy phones from group')).toBeNull()
  fireEvent.click(screen.getByLabelText('Show members only'))
  expect(screen.getByRole('checkbox', { name: /Membership for Candidate phone/ })).toBeDisabled()
  fireEvent.click(removal)
  fireEvent.click(screen.getByRole('button', { name: 'Save 1 membership change' }))
  await screen.findByText('1 phone memberships updated.')
  expect(apiFetch).toHaveBeenCalledWith('/api/terminal-groups/fixture-group/phones', expect.objectContaining({
    method: 'PUT', body: JSON.stringify([{ terminalId: member.id, revision: member.revision, member: false }]),
  }))
  await waitFor(() => expect(screen.getByRole('checkbox', { name: /Membership for Existing member/ })).toBeDisabled())
})

it('lets an add-only group operator create a group without reading existing records', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { groups: 4 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json(group, { status: 201 }))
  render(<MemoryRouter><TerminalGroupsPage /></MemoryRouter>)
  fireEvent.change(screen.getByLabelText('Name'), { target: { value: group.name } })
  fireEvent.change(screen.getByLabelText('Group ID'), { target: { value: 'ABC' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add group' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Phone group added.')
  expect(apiFetch).toHaveBeenCalledTimes(1)
  expect(apiFetch).toHaveBeenCalledWith('/api/terminal-groups', expect.objectContaining({
    method: 'POST', body: JSON.stringify({ name: group.name, code: 'ABC', description: '', customerId: null }),
  }))
})
