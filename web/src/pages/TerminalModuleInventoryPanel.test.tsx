import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TerminalModuleInventoryPanel } from './TerminalModuleInventoryPanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })
const endpoint = '/api/terminals/fixture/module-inventory'
const saved = { smartCityId: '00001234', protonId: '00AB-09', revision: 7, updatedAtUtc: null, administrativeOnly: true }

it('loads administrative identifiers on demand and preserves the other value when explicitly clearing one', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { inventory: 3 })
  vi.mocked(apiFetch).mockImplementation(async (_, init) => init?.method === 'PUT'
    ? Response.json({ ...saved, smartCityId: null, revision: 8 }) : Response.json(saved))
  render(<TerminalModuleInventoryPanel terminalId="fixture" />)
  expect(apiFetch).not.toHaveBeenCalled()
  fireEvent.click(screen.getByText('E-purse module identifiers'))
  expect(await screen.findByText('00001234')).toBeVisible()
  expect(screen.getByText('00AB-09')).toBeVisible()
  expect(screen.getByText(/not downloaded/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Edit module identifiers' }))
  expect(screen.getByLabelText('SmartCity module ID')).toHaveValue('00001234')
  fireEvent.change(screen.getByLabelText('SmartCity module ID'), { target: { value: '' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save module identifiers' }))
  expect(await screen.findByText('Module inventory saved.')).toBeVisible()
  expect(screen.getByText('Not registered')).toBeVisible()
  expect(screen.getByText('00AB-09')).toBeVisible()
  const [, request] = vi.mocked(apiFetch).mock.calls.at(-1)!
  expect(JSON.parse(String(request?.body))).toEqual({ smartCityId: null, protonId: '00AB-09', revision: 7 })
  expect(vi.mocked(apiFetch).mock.calls.at(-1)?.[0]).toBe(endpoint)
})

it('retains a staged edit after a stale revision and allows an explicit reload', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { inventory: 3 })
  let reads = 0
  vi.mocked(apiFetch).mockImplementation(async (_, init) => init?.method === 'PUT'
    ? Response.json({ error: 'The module inventory changed. Reload it and try again.' }, { status: 409 })
    : Response.json(++reads === 1 ? saved : { ...saved, protonId: 'NEW-FIXTURE', revision: 8 }))
  render(<TerminalModuleInventoryPanel terminalId="fixture" />)
  fireEvent.click(screen.getByText('E-purse module identifiers'))
  fireEvent.click(await screen.findByRole('button', { name: 'Edit module identifiers' }))
  fireEvent.change(screen.getByLabelText('Proton module ID'), { target: { value: 'EDIT-FIXTURE' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save module identifiers' }))
  expect(await screen.findByRole('alert')).toHaveTextContent(/Reload it/)
  expect(screen.getByLabelText('Proton module ID')).toHaveValue('EDIT-FIXTURE')
  fireEvent.click(screen.getByRole('button', { name: 'Reload module inventory' }))
  expect(await screen.findByText('NEW-FIXTURE')).toBeVisible()
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Save module identifiers' })).not.toBeInTheDocument())
})

it('permits read-only inspection without exposing any edit or save control', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { inventory: 1 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json(saved))
  render(<TerminalModuleInventoryPanel terminalId="fixture" />)
  fireEvent.click(screen.getByText('E-purse module identifiers'))
  expect(await screen.findByText('00001234')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Edit module identifiers' })).not.toBeInTheDocument()
  expect(apiFetch).toHaveBeenCalledTimes(1)
})
