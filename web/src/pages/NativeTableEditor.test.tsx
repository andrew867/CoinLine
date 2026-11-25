import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { TableSetConfiguration } from './TableSetConfiguration'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

function renderEditor(number = 55) {
  render(<NativeTableEditor setId="set-fixture" number={number} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
}

it('identifies MT72 and its native IOPR configuration from the table-set editor', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 1 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ supported: true, editable: false, profile: 'Later packed profile',
    length: 1001, revision: 1, sha256: 'fixture-hash', sections: [{ key: 'operator', label: 'IOPR configuration', fields: [
      { key: 'screenReference', label: 'IOPR access number (MT92 entry)', editor: 'number', value: '180', min: 0, max: 180,
        maxLength: null, choices: null, help: 'Zero disables IOPR.' },
    ] }] }))
  render(<TableSetConfiguration setId="set-fixture" draft={false} initialRecord={72}
    versions={[{ tableDefinitionId: 'fixture-definition', tableNumber: 72, definitionName: 'Spare table', tableRevision: 1, sortOrder: 0,
      dependsOnTableDefinitionId: null }]} onSaved={vi.fn()} recordDirty={false} onRecordDirty={vi.fn()} />)
  expect(screen.getByRole('option', { name: 'MT72 · Spare table, e-purse and IOPR' })).toBeVisible()
  expect(await screen.findByRole('spinbutton', { name: /IOPR access number/ })).toHaveValue(180)
})

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

it('previews edited phone-local service hours and identifies the later permitted-route mode', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const timing = (key: string, label: string, value: string, max: number) =>
    ({ key, label, value, editor: 'number', min: 0, max, maxLength: null, choices: null, help: null })
  const layout = { supported: true, editable: true, profile: 'Later packed profile', length: 26, revision: 1, sha256: 'fixture-hash',
    sections: [{ key: 'restriction', label: 'Daily service restriction', fields: [
      { ...timing('restriction', 'Restriction level', '4', 4), editor: 'select', choices: [{ value: 0, label: 'None' }, { value: 4, label: 'Configurable restricted access' }] },
      timing('startHour', 'Start hour (phone local time)', '20', 23), timing('startMinute', 'Start minute', '30', 59),
      timing('durationHours', 'Duration hours', '6', 24), timing('durationMinutes', 'Duration minutes', '15', 59),
    ] }] }
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(layout))
  renderEditor(58)
  expect(await screen.findByText(/20:30 to 02:45 the next day/)).toBeVisible()
  expect(screen.getByText(/Mark the permitted entries/)).toBeVisible()
  fireEvent.change(screen.getByLabelText('Duration minutes'), { target: { value: '20' } })
  expect(screen.getByText(/20:30 to 02:50 the next day/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/set-fixture/records/58/fields',
    expect.objectContaining({ method: 'PUT', body: expect.stringContaining('"durationMinutes":"20"') })))
})

it('previews Audio-Gram permissions and changes the independent statistics option without exposing a key', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const toggle = (key: string, label: string, value: string) => ({ key, label, value, editor: 'checkbox', min: 0, max: 1, maxLength: null, choices: null, help: null })
  const select = (key: string, label: string, value: string) => ({ ...toggle(key, label, value), editor: 'select', choices: [{ value: 0, label: 'Disabled' }, { value: 1, label: 'Enabled' }] })
  const layout = { supported: true, editable: true, profile: 'MTR 1.9 packed', length: 72, revision: 1, sha256: 'fixture-hash', sections: [
    { key: 'service', label: 'Access and reporting', fields: [select('enabled', 'Messaging service', '1'), select('statistics', 'Collect service statistics', '0'), select('cdrs', 'Send service call records', '1'),
      { key: 'serviceKey', label: 'Messaging service key', editor: 'password', value: null, min: 0, max: 0, maxLength: 8, choices: null, help: 'Leave blank to keep the stored key.' }] },
    { key: 'permissions', label: 'Call and payment permissions', fields: [toggle('allowLocal', 'Local calls', 'true'), toggle('allowCoin', 'Coins', 'true')] },
    { key: 'signaling', label: 'Signaling and timing', fields: [{ ...toggle('ringCycles', 'Unanswered ringing cycles', '1'), editor: 'number' }, { ...toggle('busyCycles', 'Busy cycles', '1'), editor: 'number' }] },
  ] }
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(layout))
  renderEditor(68)
  expect(await screen.findByRole('region', { name: 'Audio-Gram configuration preview' })).toBeVisible()
  expect(screen.getByText('Local', { exact: true })).toBeVisible()
  expect(screen.getByText('Coins', { exact: true })).toBeVisible()
  expect(screen.getByText(/internal offer-detection thresholds/)).toBeVisible()
  expect(screen.getByLabelText(/Messaging service key/)).toHaveValue('')
  fireEvent.change(screen.getByLabelText('Collect service statistics'), { target: { value: '1' } })
  expect(screen.getByText('Collect MT69')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/set-fixture/records/68/fields',
    expect.objectContaining({ method: 'PUT', body: expect.stringContaining('"changes":{"statistics":"1"}') })))
})

it('edits the optional CDR mode independently of the later combined carrier mode', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const numeric = (key: string, label: string, value: string, max: number) => ({ key, label, value, editor: 'number', min: 0, max, maxLength: null, choices: null, help: null })
  const select = (key: string, label: string, value: string, choices: { value: number; label: string }[]) =>
    ({ ...numeric(key, label, value, 255), editor: 'select', choices })
  const layout = { supported: true, editable: true, profile: 'Later packed profile', length: 19, revision: 1, sha256: 'fixture-hash', sections: [
    { key: 'summary', label: 'Call and carrier statistics', fields: [numeric('days', 'Collection interval (days)', '3', 255), numeric('threshold', 'Statistics upload threshold', '2', 255),
      numeric('startHour', 'Collection start hour', '2', 23), numeric('startMinute', 'Collection start minute', '15', 59),
      select('carrierStatistics', 'Carrier statistics', '17', [{ value: 0, label: 'Disabled' }, { value: 1, label: 'Per carrier' }, { value: 17, label: 'Combined carrier summary' }])] },
    { key: 'detail', label: 'Optional CDR logging', fields: [select('enabled', 'Optional CDR mode', '4', [{ value: 0, label: 'Normal calls only' }, { value: 4, label: 'Completed calls only' }, { value: 16, label: 'Completed except local coin and stored-value' }]),
      numeric('detailThreshold', 'Optional CDR upload threshold', '40', 255), numeric('detailHour', 'Optional logging start hour', '20', 23), numeric('detailMinute', 'Optional logging start minute', '0', 59),
      numeric('detailDays', 'Duration days', '0', 255), numeric('detailHours', 'Duration hours', '6', 23)] },
    ...Array.from({ length: 4 }, (_, i) => ({ key: `timestamp-${i}`, label: `Report timestamp ${i + 1}`, fields: [
      numeric(`timestamp-${i}.hour`, 'Hour', i === 0 ? '6' : '0', 23), numeric(`timestamp-${i}.minute`, 'Minute', '0', 59)] })),
  ] }
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(layout))
  renderEditor(34)
  expect(await screen.findByText(/Daily checkpoints at 02:15, 06:00/)).toBeVisible()
  expect(screen.getByText('Combined (MT71 reference 253)')).toBeVisible()
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'detail' } })
  const mode = screen.getByLabelText('Optional CDR mode')
  fireEvent.change(mode, { target: { value: '16' } })
  expect(screen.getByText('Combined (MT71 reference 253)')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/set-fixture/records/34/fields',
    expect.objectContaining({ method: 'PUT', body: expect.stringContaining('"changes":{"enabled":"16"}') })))
})
