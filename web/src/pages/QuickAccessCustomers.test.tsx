import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { QuickAccessCustomersPage } from './QuickAccessCustomersPage'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear(); vi.restoreAllMocks() })

const endpoint = '/api/quick-access-customers'
const first = { id: 'customer-one', reference: '000123', name: 'Synthetic first', revision: 1, archived: false,
  billingComments: 'Fixture billing note', contactName: 'Synthetic contact', phone: '', street: '', city: '', province: '', postalCode: '', notes: '' }
const second = { ...first, id: 'customer-two', reference: '000124', name: 'Synthetic second', billingComments: '', contactName: '' }
const page = (rows: unknown[], current = 1, total = rows.length, pageSize = 50) => Response.json({ rows, total, page: current, pageSize })
const directory = () => render(<MemoryRouter><QuickAccessCustomersPage /></MemoryRouter>)

it('authors a new directory entry, retains leading zeroes and opens the saved record', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'quick-access-customers': 7 })
  let saved = false
  vi.mocked(apiFetch).mockImplementation(async (url, init) => {
    if (url === endpoint + '/search') return page(saved ? [first] : [])
    if (url === endpoint && init?.method === 'POST') { saved = true; return Response.json(first, { status: 201 }) }
    if (url === endpoint + '/' + first.id) return Response.json(first)
    return new Response(null, { status: 404 })
  })
  directory()
  expect(screen.getByRole('button', { name: 'Save customer' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Customer reference'), { target: { value: '000123' } })
  fireEvent.change(screen.getByLabelText('Customer name'), { target: { value: first.name } })
  fireEvent.change(screen.getByLabelText('Billing comments'), { target: { value: first.billingComments } })
  fireEvent.change(screen.getByLabelText('Contact name'), { target: { value: first.contactName } })
  fireEvent.click(screen.getByRole('button', { name: 'Save customer' }))
  expect(await screen.findByText('Customer directory entry saved.')).toBeVisible()
  await waitFor(() => expect(screen.getByLabelText('Customer reference')).toHaveValue('000123'))
  expect(screen.getByLabelText('Customer reference')).toHaveAttribute('readonly')
  expect(screen.getByLabelText('Contact name')).toHaveValue(first.contactName)
  const request = vi.mocked(apiFetch).mock.calls.find(([url, init]) => url === endpoint && init?.method === 'POST')
  expect(JSON.parse(request![1]!.body as string)).toMatchObject({ reference: '000123', name: first.name, billingComments: first.billingComments, contactName: first.contactName })
})

it('searches by name, navigates ordered customer records across pages and archives with confirmation', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'quick-access-customers': 15 })
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  let archived = false
  vi.mocked(apiFetch).mockImplementation(async (url, init) => {
    if (url === endpoint + '/search') {
      const query = JSON.parse(init!.body as string)
      return page(query.page === 2 ? [second] : [first], query.page, 2, 1)
    }
    if (url === endpoint + '/' + first.id) return Response.json(first)
    if (url === endpoint + '/' + second.id) return Response.json({ ...second, archived, revision: archived ? 2 : 1 })
    if (String(url).includes(second.id) && init?.method === 'DELETE') { archived = true; return new Response(null, { status: 204 }) }
    if (String(url).includes(second.id + '/restore')) { archived = false; return new Response(null, { status: 204 }) }
    return new Response(null, { status: 404 })
  })
  directory()
  fireEvent.change(await screen.findByLabelText('Customer reference or name'), { target: { value: 'Synthetic' } })
  fireEvent.change(screen.getByLabelText('Sort customers by'), { target: { value: 'name' } })
  await waitFor(() => expect(screen.getByRole('button', { name: 'Search customers' })).toBeEnabled())
  fireEvent.click(screen.getByRole('button', { name: 'Search customers' }))
  fireEvent.click(await screen.findByRole('button', { name: first.reference }))
  await waitFor(() => expect(screen.getByLabelText('Customer name')).toHaveValue(first.name))
  fireEvent.click(screen.getByRole('button', { name: 'Next customer' }))
  await waitFor(() => expect(screen.getByLabelText('Customer name')).toHaveValue(second.name))
  fireEvent.click(screen.getByRole('button', { name: 'Previous customer' }))
  await waitFor(() => expect(screen.getByLabelText('Customer name')).toHaveValue(first.name))
  fireEvent.click(screen.getByRole('button', { name: 'Next customer' }))
  await waitFor(() => expect(screen.getByLabelText('Customer name')).toHaveValue(second.name))
  fireEvent.click(screen.getByRole('button', { name: 'Archive customer' }))
  expect(await screen.findByText(/no longer offered in the key lookup/)).toBeVisible()
  expect(vi.mocked(apiFetch)).toHaveBeenCalledWith(endpoint + '/' + second.id + '?revision=1&confirm=true', { method: 'DELETE' })
  fireEvent.click(screen.getByRole('button', { name: 'Restore customer' }))
  await waitFor(() => expect(screen.getByLabelText('Customer name')).toBeEnabled())
  expect(vi.mocked(apiFetch)).toHaveBeenCalledWith(endpoint + '/' + second.id + '/restore?revision=2', { method: 'POST' })
})

it('does not expose directory details for a lookup-only role and shows API failures', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'quick-access-customer-lookup': 1 })
  directory()
  expect(screen.getByText(/do not have permission/)).toBeVisible()
  expect(apiFetch).not.toHaveBeenCalled()
  cleanup()
  consoleSession.save('fixture', '', undefined, 'Operator', { 'quick-access-customers': 1 })
  vi.mocked(apiFetch).mockResolvedValue(new Response(null, { status: 500 }))
  directory()
  expect(await screen.findByRole('alert')).toHaveTextContent('HTTP 500')
  expect(screen.queryByRole('button', { name: 'New customer' })).toBeNull()
})

it.each([['5', 5], ['10', 9]])('uses lookup in the %s-key panel without touching other fields', async (panel, index) => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3, 'quick-access-customer-lookup': 1 })
  const field = (key: string, label: string, value: string, editor: string) =>
    ({ key, label, value, editor, min: 0, max: 4294967295, maxLength: null, choices: null, help: null })
  const layout = { supported: true, editable: true, profile: 'Fixture', length: 571, revision: 1, sha256: 'fixture-hash',
    sections: Array.from({ length: 10 }, (_, n) => ({ key: `key-${n}`, label: `Key ${n + 1}`, fields: [
      field(`key-${n}.number`, 'Destination', '12', 'text'),
      field(`key-${n}.customerReference`, 'Customer reference', n === index ? '305419896' : '0', 'number'),
    ] })) }
  vi.mocked(apiFetch).mockImplementation(async (url, init) => {
    if (url === endpoint + '/lookup') return page([{ reference: first.reference, name: first.name }])
    if (init?.method === 'PUT') return new Response(null, { status: 204 })
    return Response.json(layout)
  })
  render(<NativeTableEditor setId="set-fixture" number={55} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
  fireEvent.change(await screen.findByLabelText('Panel layout'), { target: { value: panel } })
  fireEvent.click(screen.getByRole('button', { name: new RegExp(`^Key ${index + 1} `) }))
  expect(screen.getByLabelText('Customer reference')).toHaveValue(305419896)
  expect(screen.getByText(/outside the six-digit directory/)).toBeVisible()
  expect(screen.getByRole('button', { name: 'Save record' })).toBeDisabled()
  expect(screen.queryByText('Manage customer directory')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Clear customer reference' }))
  expect(screen.getByLabelText('Customer reference')).toHaveValue(0)
  expect(screen.getByLabelText('Destination')).toHaveValue('12')
  fireEvent.click(screen.getByRole('button', { name: 'Look up customer' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Use 000123' }))
  expect(screen.getByLabelText('Customer reference')).toHaveValue(123)
  expect(screen.getByLabelText('Destination')).toHaveValue('12')
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  const update = vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')
  expect(JSON.parse(update![1]!.body as string).changes).toEqual({ [`key-${index}.customerReference`]: '123' })
})
