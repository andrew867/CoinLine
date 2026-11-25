import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })
const field = (key: string, label: string, value: string, editor = 'number', isSpare = false) =>
  ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null, choices: null, help: null })
const layout = (length: number, sections: unknown[], profile = 'Later packed profile') =>
  ({ supported: true, editable: true, profile, length, revision: 1, sha256: 'fixture-hash', sections })
function editor(number: number, data: unknown) {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  vi.mocked(apiFetch).mockImplementation(async (url, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 })
    : String(url).endsWith('/card-sequence') ? Response.json({ available: false, reason: 'No companion record' }) : Response.json(data))
  render(<MemoryRouter><NativeTableEditor setId="set-fixture" number={number} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
}
function openSpares() { screen.getByText(/^Spare fields/).closest('details')!.open = true }
async function savedChanges() {
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  return JSON.parse(vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')![1]!.body as string).changes
}

it('keeps the older stored-value reserved byte independent of named configuration', async () => {
  editor(22, layout(661, [
    { key: 'card-0', label: 'Card range 1', fields: [field('card-0.reference', 'Card reference number', '8')] },
    { key: 'card-0-stored-value', label: 'Card range 1 stored-value settings', fields: [
      field('card-0.spare.storedValue', 'Stored-value spare byte', '165', 'number', true),
    ] },
    { key: 'card-1', label: 'Card range 2', fields: [field('card-1.reference', 'Card reference number', '7')] },
  ], 'MTR 1.9 packed'))
  const section = await screen.findByLabelText('Section or entry')
  fireEvent.change(section, { target: { value: 'card-0-stored-value' } })
  openSpares()
  fireEvent.change(screen.getByLabelText('Stored-value spare byte'), { target: { value: '129' } })
  fireEvent.change(section, { target: { value: 'card-1' } })
  expect(screen.queryByText(/^Spare fields/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'card-0.spare.storedValue': '129' })
})

it('edits later card control and spill spares without modifying validation or stored-value flags', async () => {
  editor(134, layout(1153, [
    { key: 'card-0', label: 'Card range 1', fields: [field('card-0.hostValidation', 'Require host validation', 'true', 'checkbox'),
      field('card-0.spare.control.bit7', 'Additional card control spare bit 7 (0x80)', 'true', 'checkbox', true)] },
    { key: 'card-0-spill', label: 'Card range 1 data spill', fields: [
      field('card-0.spare.spillTerminator.bit7', 'Spill terminator spare bit 7 (0x80)', 'true', 'checkbox', true),
    ] },
    { key: 'card-1-stored-value', label: 'Card range 2 stored-value settings', fields: [field('card-1.discount', 'Embedded discount index', '5')] },
  ]))
  const section = await screen.findByLabelText('Section or entry')
  expect(screen.getByLabelText('Require host validation')).toBeChecked()
  openSpares()
  fireEvent.click(screen.getByLabelText(/Additional card control spare bit 7/))
  fireEvent.change(section, { target: { value: 'card-0-spill' } })
  openSpares()
  fireEvent.click(screen.getByLabelText(/Spill terminator spare bit 7/))
  fireEvent.change(section, { target: { value: 'card-1-stored-value' } })
  expect(screen.getByLabelText('Embedded discount index')).toHaveValue(5)
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'card-0.spare.control.bit7': 'false', 'card-0.spare.spillTerminator.bit7': 'false' })
})

it.each(['MTR 1.9 packed', 'Later packed profile'])('keeps %s carrier routing separate from payment settings', async profile => {
  const old = profile === 'MTR 1.9 packed'
  editor(26, layout(72, [
    { key: 'payment', label: 'Terminal and payment', fields: [field('localAuthorization', 'Require host authorization for local card calls', 'true', 'checkbox')] },
    { key: 'callSetup', label: 'Call setup', fields: old ? [field('spare.byte5', 'Spare byte 5', '165', 'number', true)] : [field('preAuthorizationLimit', 'Preauthorization credit limit', '12')] },
    { key: 'carrierReroute', label: 'Carrier payment rerouting', fields: [
      field('rerouteCoin', 'Permit alternate carrier routing for coins', 'false', 'checkbox'),
      field('rerouteCredit', 'Permit alternate carrier routing for credit cards', 'false', 'checkbox'),
      field('spare.carrierReroute.bit7', 'Carrier reroute control spare bit 7 (0x80)', 'true', 'checkbox', true),
    ] },
  ], profile))
  const section = await screen.findByLabelText('Section or entry')
  expect(screen.getByLabelText(/Require host authorization/)).toBeChecked()
  fireEvent.change(section, { target: { value: 'carrierReroute' } })
  fireEvent.click(screen.getByLabelText(/Permit alternate carrier routing for coins/))
  expect(screen.getByLabelText(/Permit alternate carrier routing for credit cards/)).not.toBeChecked()
  openSpares()
  fireEvent.click(screen.getByLabelText(/Carrier reroute control spare bit 7/))
  fireEvent.change(section, { target: { value: 'callSetup' } })
  if (old) { openSpares(); fireEvent.change(screen.getByLabelText('Spare byte 5'), { target: { value: '129' } }) }
  else expect(screen.queryByText(/^Spare fields/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ rerouteCoin: 'true', 'spare.carrierReroute.bit7': 'false', ...(old ? { 'spare.byte5': '129' } : {}) })
})

it('retains the imported ADSI identifiers when editing the verified access-record tail', async () => {
  editor(21, layout(48, [
    { key: 'access', label: 'Management access', fields: [field('terminalNumber', 'Terminal wire ID', 'fixture-id', 'text')] },
    { key: 'adsi', label: 'ADSI identifiers', fields: [field('cadId', 'CAD identifier', 'A5A5A5A5', 'text'),
      field('cpeId', 'CPE identifier', 'B6B6B6B6', 'text'), field('spare.byte14', 'Spare byte 14', '165', 'number', true)] },
  ]))
  const section = await screen.findByLabelText('Section or entry')
  fireEvent.change(section, { target: { value: 'adsi' } })
  expect(screen.getByLabelText('CAD identifier')).toHaveValue('A5A5A5A5')
  openSpares()
  fireEvent.change(screen.getByLabelText('Spare byte 14'), { target: { value: '129' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'spare.byte14': '129' })
})
