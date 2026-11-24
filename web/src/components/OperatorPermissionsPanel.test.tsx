import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { OperatorPermissionsPanel } from './OperatorPermissionsPanel'
import { mayOpenScreen } from './Layout'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('copies a profile, permits only supported operations, and saves the observed revision', async () => {
  const person = { id: 'fixture-person', displayName: 'Fixture person', role: 'Operator' }
  const source = { id: 'fixture-source', displayName: 'Fixture source', role: 'Technician' }
  vi.mocked(apiFetch)
    .mockResolvedValueOnce(Response.json([{ key: 'memberships', label: 'Group membership', operations: 13 }]))
    .mockResolvedValueOnce(Response.json({ role: 'Operator', revision: 3, permissions: null }))
    .mockResolvedValueOnce(Response.json({ role: 'Technician', revision: 2, permissions: { memberships: 5 } }))
    .mockResolvedValueOnce(Response.json({ role: 'Operator', revision: 4, permissions: { memberships: 9 } }))

  render(<OperatorPermissionsPanel person={person} people={[person, source]} onClose={() => {}} />)
  await screen.findByLabelText('Copy from person')
  fireEvent.change(screen.getByLabelText('Copy from person'), { target: { value: source.id } })
  fireEvent.click(screen.getByRole('button', { name: 'Copy permissions' }))
  await screen.findByText('Copied to this draft. Save to apply.')
  expect(screen.getByLabelText('Group membership: Read')).toBeChecked()
  expect(screen.getByLabelText('Group membership: Add')).toBeChecked()
  expect(screen.queryByLabelText('Group membership: Update')).toBeNull()
  fireEvent.click(screen.getByLabelText('Group membership: Add'))
  fireEvent.click(screen.getByLabelText('Group membership: Delete'))
  fireEvent.click(screen.getByRole('button', { name: 'Save permissions' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Permissions saved.')
  expect(apiFetch).toHaveBeenLastCalledWith('/api/auth/accounts/fixture-person/permissions', expect.objectContaining({
    method: 'PUT', body: JSON.stringify({ revision: 3, permissions: { memberships: 9 } }),
  }))
})

it('hides screens outside the custom profile and keeps account settings accessible', () => {
  const grants = { phones: 1 }
  expect(mayOpenScreen('/terminals/example', 'Operator', grants)).toBe(true)
  expect(mayOpenScreen('/field-events', 'Operator', grants)).toBe(false)
  expect(mayOpenScreen('/operators', 'Operator', grants)).toBe(false)
  expect(mayOpenScreen('/settings', 'Operator', grants)).toBe(true)
  expect(mayOpenScreen('/field-events', 'Admin', {})).toBe(true)
})
