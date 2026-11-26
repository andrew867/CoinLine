import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
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

it('reviews the selected saved tariff row even on a published read-only record, and discards the old row review', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 1 })
  const layout = { supported: true, editable: false, profile: 'MTR 1.9 packed', length: 1192, revision: 1, sha256: 'fixture-hash',
    sections: [0, 1].map(index => ({ key: `rate-${index}`, label: `Rate ${index}`, fields: [
      { key: `rate-${index}.initialCharge`, label: 'Initial charge', editor: 'number', value: '25', min: 0, max: 65535, choices: null, maxLength: null, help: null },
    ] })) }
  vi.mocked(apiFetch).mockImplementation(async path => {
    const selected = String(path).match(/tariffs\/(\d+)\/references$/)
    if (!selected) return Response.json(layout)
    return Response.json({ rateIndex: Number(selected[1]), setRevision: 1, profile: 'Fixture profile', review: {
      supported: true, reason: null, groups: [{ recordNumber: 92, kind: 'Screening rules', use: 'configured', count: Number(selected[1]) + 1,
        entries: [`Fixture entry ${selected[1]}`] }], reviewedRecords: [92], missingRecords: [], unreviewedRecords: [], warnings: [] } })
  })
  renderEditor(73)
  expect(await screen.findByRole('heading', { name: 'What uses tariff 0?' })).toBeVisible()
  expect(screen.getByLabelText('Initial charge')).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Review references' }))
  expect(await screen.findByText(/Screening rules \(1 reference\)/)).toBeVisible()
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'rate-1' } })
  expect(screen.getByRole('heading', { name: 'What uses tariff 1?' })).toBeVisible()
  expect(screen.queryByText(/Screening rules \(1 reference\)/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Review references' }))
  expect(await screen.findByText(/Screening rules \(2 references\)/)).toBeVisible()
  expect(vi.mocked(apiFetch).mock.calls.some(([path]) => path === '/api/tables/sets/set-fixture/tariffs/1/references')).toBe(true)
  expect(screen.queryByRole('button', { name: 'Save record' })).toBeNull()
})

it.each([
  ['flat', 'Whole call charge (cents)', '25', null, '2', '-1', '0', '0', '$0.25'],
  ['minute', 'Charge per minute (cents)', '25', null, '1', '60', '60', '25', '$1.25'],
  ['split', 'First minute including connection (cents)', '25', '10', '1', '60', '60', '10', '$0.65'],
])('stages %s local-call pricing without changing any other tariff row', async (pricing, label, first, extra, classification, period, nextPeriod, nextCharge, fiveMinutes) => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const layout = { supported: true, editable: true, profile: 'Fixture', length: 1192, revision: 1, sha256: 'fixture-hash',
    sections: [0, 1].map(index => ({ key: `rate-${index}`, label: `Rate ${index}`, fields: [
      { key: `rate-${index}.classification`, label: 'Rate classification', editor: 'select', value: '3', min: 0, max: 8,
        choices: [{ value: 1, label: 'Local measured service' }, { value: 2, label: 'Fixed local charge' }, { value: 3, label: 'Rate unavailable' }], maxLength: null, help: null },
      ...['initialPeriod', 'initialCharge', 'additionalPeriod', 'additionalCharge'].map(name => ({ key: `rate-${index}.${name}`, label: name,
        editor: name.endsWith('Period') ? 'rate-period' : 'number', value: '0', min: 0, max: 32767, maxLength: null, choices: null, help: null })),
    ] })) }
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(layout))
  renderEditor(73)
  fireEvent.change(await screen.findByLabelText('Pricing structure'), { target: { value: pricing } })
  fireEvent.change(screen.getByLabelText(label), { target: { value: first } })
  if (extra !== null) fireEvent.change(screen.getByLabelText('Each following minute (cents)'), { target: { value: extra } })
  expect(screen.getByLabelText('Example pricing')).toHaveTextContent(`5 minutes: ${fiveMinutes}`)
  expect(apiFetch).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByRole('button', { name: 'Apply pricing to this row' }))
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/set-fixture/records/73/fields', expect.objectContaining({ method: 'PUT' })))
  const request = vi.mocked(apiFetch).mock.calls.find(([, options]) => options?.method === 'PUT')
  const body = JSON.parse(String(request?.[1]?.body))
  expect(body.changes).toEqual({ 'rate-0.classification': classification, 'rate-0.initialPeriod': period, 'rate-0.initialCharge': first,
    ...(nextPeriod !== '0' ? { 'rate-0.additionalPeriod': nextPeriod } : {}), ...(nextCharge !== '0' ? { 'rate-0.additionalCharge': nextCharge } : {}) })
})

it('moves a card range through the native editor and reopens its actual saved destination', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  let moved = false
  const layout = () => ({ supported: true, editable: true, profile: 'Later packed profile', length: 1153,
    revision: moved ? 2 : 1, sha256: moved ? 'card-moved' : 'card-initial',
    sections: [0, 1, 2].map(index => ({ key: `card-${index}`, label: `Card range ${index + 1}`, fields: [
      { key: `card-${index}.start`, label: 'Lowest six-digit prefix', editor: 'text', value: index === (moved ? 2 : 0) ? '000001' : '000000',
        min: 0, max: 0, maxLength: 6, choices: null, help: null },
      { key: `card-${index}.end`, label: 'Highest six-digit prefix', editor: 'text', value: index === (moved ? 2 : 0) ? '000002' : '000000',
        min: 0, max: 0, maxLength: 6, choices: null, help: null },
    ] })) })
  vi.mocked(apiFetch).mockImplementation(async (url, init) => {
    if (String(url).endsWith('/card-sequence')) {
      if (init?.method === 'POST') { moved = true; return Response.json({ carrierChanged: true }) }
      return Response.json({ available: true, editable: true, cards: { revision: layout().revision, sha256: layout().sha256 },
        carriers: { revision: moved ? 2 : 1, sha256: moved ? 'carrier-moved' : 'carrier-initial' }, carrierNumber: 135,
        slots: [1, 2, 3].map(number => ({ number, blank: number !== (moved ? 3 : 1), movable: number === (moved ? 3 : 1) })) })
    }
    return Response.json(layout())
  })
  renderEditor(134)
  const from = await screen.findByLabelText('Move from')
  const field = screen.getByLabelText('Lowest six-digit prefix')
  fireEvent.change(field, { target: { value: '000003' } })
  expect(from).toBeDisabled()
  fireEvent.change(field, { target: { value: '000001' } })
  expect(from).toBeEnabled()
  fireEvent.change(from, { target: { value: '1' } })
  fireEvent.change(screen.getByLabelText('Move to'), { target: { value: '3' } })
  fireEvent.click(screen.getByRole('button', { name: 'Move card entry' }))
  await waitFor(() => expect(screen.getByLabelText('Section or entry')).toHaveValue('card-2'))
  expect(screen.getByLabelText('Lowest six-digit prefix')).toHaveValue('000001')
  expect(screen.getByText('Card entry moved to position 3. Matching carrier acceptance was updated.')).toBeVisible()
  expect(await screen.findByRole('option', { name: 'Position 3 · 000001 to 000002' })).toBeVisible()
})

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

it('adds the complete graphical starter without importing unrelated records', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 5 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ supported: false, reason: 'Choose a template',
    templates: [{ profile: 'Later packed profile', length: 9224 }] }))
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ created: 3, records: [131, 132, 133] }))
  const saved = vi.fn()
  render(<TableSetConfiguration setId="set-fixture" draft initialRecord={131}
    versions={[]} onSaved={saved} recordDirty={false} onRecordDirty={vi.fn()} />)
  fireEvent.click(screen.getByText('Start graphical advertising from sample artwork'))
  fireEvent.click(screen.getByLabelText(/Enable the sample off-hook navigation menu/))
  fireEvent.click(screen.getByRole('button', { name: 'Add graphical starter' }))
  await waitFor(() => expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/set-fixture/graphical-starter',
    { enableSampleMenu: true }))
  expect(await screen.findByText(/3 graphical records added/)).toBeVisible()
  expect(saved).toHaveBeenCalledTimes(1)
})

it('previews globe frames at half-second intervals and static advertisements at five seconds', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 5 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ supported: false, reason: 'Choose a template',
    templates: [{ profile: 'Later packed profile', length: 9224 }] }))
  render(<TableSetConfiguration setId="set-fixture" draft initialRecord={131}
    versions={[]} onSaved={vi.fn()} recordDirty={false} onRecordDirty={vi.fn()} />)
  fireEvent.click(screen.getByText('Start graphical advertising from sample artwork'))
  await screen.findByText('Choose a template')
  vi.useFakeTimers()
  try {
    fireEvent.click(screen.getByRole('button', { name: 'Play sample animation' }))
    expect(screen.getByText(/Screen 1 of 20.*500 ms/)).toBeVisible()
    act(() => vi.advanceTimersByTime(499))
    expect(screen.getByText(/Screen 1 of 20/)).toBeVisible()
    act(() => vi.advanceTimersByTime(1))
    expect(screen.getByText(/Screen 2 of 20/)).toBeVisible()
    for (let count = 0; count < 15; count++)
      act(() => vi.advanceTimersByTime(500))
    expect(screen.getByText(/Screen 17 of 20.*5 seconds/)).toBeVisible()
    act(() => vi.advanceTimersByTime(4999))
    expect(screen.getByText(/Screen 17 of 20/)).toBeVisible()
    act(() => vi.advanceTimersByTime(1))
    expect(screen.getByText(/Screen 18 of 20/)).toBeVisible()
    for (let count = 0; count < 3; count++)
      act(() => vi.advanceTimersByTime(5000))
    expect(screen.getByText(/Screen 1 of 20.*500 ms/)).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: 'Pause sample animation' }))
  } finally {
    vi.useRealTimers()
  }
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
