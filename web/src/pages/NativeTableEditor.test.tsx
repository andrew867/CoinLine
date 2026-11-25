import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn() }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

function renderEditor(number = 55) {
  render(<NativeTableEditor setId="set-fixture" number={number} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
}

it('does not let Read plus Add edit an existing draft record', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 5 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ supported: true, editable: true, profile: 'Fixture',
    length: 42, revision: 1, sha256: 'fixture-hash', sections: [{ key: 'key-0', label: 'Key 1', fields: [
      { key: 'key-0.number', label: 'Destination', editor: 'text', value: '123', min: 0, max: 0, maxLength: 20, choices: null, help: null },
    ] }] }))
  renderEditor()
  expect(await screen.findByText('Read-only access')).toBeVisible()
  expect(screen.getByLabelText('Destination')).toBeDisabled()
  expect(screen.queryByRole('button', { name: 'Save record' })).toBeNull()
  expect(apiPostRaw).not.toHaveBeenCalled()
})

it('shows an international fallback action separately from MT73 tariffs and preserves an imported code until edited', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const layout = { supported: true, editable: true, profile: 'Later packed profile', length: 604, revision: 1, sha256: 'fixture-hash',
    sections: [{ key: 'country-0', label: 'Destination 1', fields: [{ key: 'country-0.rate', label: 'Destination rate', editor: 'select',
      value: '255', min: 0, max: 100, maxLength: null, choices: null, help: null, tariffIndexOffset: 28, tariffFallbackCode: 100 }] }] }
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(layout))
  renderEditor(151)
  const select = await screen.findByLabelText('Destination rate')
  expect(select).toHaveValue('255')
  expect(screen.getByRole('option', { name: 'Stored value 255' })).toBeVisible()
  expect(screen.getByRole('option', { name: 'MT73 rate 127' })).toBeVisible()
  expect(screen.queryByRole('option', { name: 'MT73 rate 128' })).toBeNull()
  expect(screen.getByRole('option', { name: 'Use configured fallback action' })).toBeVisible()
  fireEvent.change(select, { target: { value: '100' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/set-fixture/records/151/fields',
    expect.objectContaining({ method: 'PUT', body: expect.stringContaining('"country-0.rate":"100"') })))
})

it('offers a missing record template only with Add permission', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ supported: false, reason: 'Choose a template',
    templates: [{ profile: 'Fixture', length: 42 }] }))
  renderEditor()
  expect(await screen.findByText('Choose a template')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Start record for Fixture' })).toBeNull()
  expect(apiPostRaw).not.toHaveBeenCalled()
})
