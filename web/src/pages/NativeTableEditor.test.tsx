import { cleanup, render, screen } from '@testing-library/react'
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

function renderEditor() {
  render(<NativeTableEditor setId="set-fixture" number={55} revision={1} exchangeFile={null}
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

it('offers a missing record template only with Add permission', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ supported: false, reason: 'Choose a template',
    templates: [{ profile: 'Fixture', length: 42 }] }))
  renderEditor()
  expect(await screen.findByText('Choose a template')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Start record for Fixture' })).toBeNull()
  expect(apiPostRaw).not.toHaveBeenCalled()
})
