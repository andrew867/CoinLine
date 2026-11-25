import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { FieldEventDestinationsPage } from './FieldEventDestinationsPage'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })
const endpoint = '/api/field-event-destinations'
const choices = { phones: [{ id: 'phone-1', name: 'Lobby phone' }], groups: [{ id: 'group-1', name: 'Facilities' }], regions: ['NORTH'] }
const rule = { id: 'rule-1', kind: 'alarm', code: 22, regionCode: 'NORTH', groupId: 'group-1', terminalId: null, destination: 'Field service', revision: 1 }
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status })

it('creates a combined scope, previews its route and shows existing historical queue assignments', async () => {
  let saved = false
  vi.mocked(apiFetch).mockImplementation(async (path, init) => {
    if (path === endpoint && init?.method === 'POST') {
      saved = true
      expect(JSON.parse(String(init.body))).toEqual({
        kind: 'alarm', code: 22, regionCode: 'NORTH', groupId: 'group-1', terminalId: null, destination: 'Field service', revision: 0,
      })
      return json(rule, 201)
    }
    if (path === endpoint) return json({ rules: saved ? [rule] : [], canCreate: true, canEdit: true, canDelete: true })
    if (path === `${endpoint}/options`) return json(choices)
    if (path.startsWith(`${endpoint}/preview?`)) {
      expect(path).toContain('terminalId=phone-1')
      expect(path).toContain('code=22')
      return json({ status: 'routed', destination: 'Field service', ruleIds: ['rule-1'] })
    }
    if (path.startsWith(`${endpoint}/queue?`)) return json([{ transactionId: 'report-1', kind: 'alarm', code: 22,
      capturedAtUtc: '2026-01-01T12:00:00Z', terminalName: 'Lobby phone', summary: 'Memory lost (alarm 22)',
      status: 'routed', destination: 'Previous queue', ruleIds: ['old-rule'] }])
    throw new Error('Unexpected request')
  })
  render(<MemoryRouter><FieldEventDestinationsPage /></MemoryRouter>)
  fireEvent.click(await screen.findByRole('button', { name: 'New rule' }))
  const form = screen.getByRole('form', { name: 'Edit destination rule' })
  fireEvent.change(within(form).getByLabelText('Code (blank for default)'), { target: { value: '22' } })
  fireEvent.change(within(form).getByLabelText('Region'), { target: { value: 'NORTH' } })
  fireEvent.change(within(form).getByLabelText('Group'), { target: { value: 'group-1' } })
  fireEvent.change(within(form).getByLabelText('Internal destination name'), { target: { value: 'Field service' } })
  fireEvent.click(within(form).getByRole('button', { name: 'Save rule' }))
  expect(await screen.findByText('Destination rule saved.')).toBeVisible()
  expect(await within(screen.getByRole('region', { name: 'Configured rules' })).findByText('Field service')).toBeVisible()
  fireEvent.change(screen.getByLabelText('Preview phone'), { target: { value: 'phone-1' } })
  fireEvent.change(screen.getByLabelText('Preview code'), { target: { value: '22' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview route' }))
  expect(await within(screen.getByRole('region', { name: 'Route preview' })).findByText('Routed')).toBeVisible()
  const history = screen.getByRole('region', { name: 'Routing history' })
  expect(within(history).getByRole('link', { name: 'Memory lost (alarm 22)' })).toHaveAttribute('href', '/dlog/report-1')
  expect(within(history).getByText('Previous queue')).toBeVisible()
})

it('shows ambiguity without selecting a winner and hides unauthorized edits', async () => {
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path === endpoint) return json({ rules: [rule], canCreate: false, canEdit: false, canDelete: false })
    if (path === `${endpoint}/options`) return json(choices)
    if (path.startsWith(`${endpoint}/preview?`)) return json({ status: 'ambiguous', destination: null, ruleIds: ['rule-1', 'rule-2'] })
    if (path.startsWith(`${endpoint}/queue?`)) return json([])
    throw new Error('Unexpected request')
  })
  render(<MemoryRouter><FieldEventDestinationsPage /></MemoryRouter>)
  await screen.findByText('Field service')
  expect(screen.queryByRole('button', { name: 'New rule' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Edit' })).toBeNull()
  fireEvent.change(screen.getByLabelText('Preview phone'), { target: { value: 'phone-1' } })
  fireEvent.change(screen.getByLabelText('Preview code'), { target: { value: '22' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview route' }))
  expect(await screen.findByText('Ambiguous rules')).toBeVisible()
})
