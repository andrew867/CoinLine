import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const field = (key: string, label: string, value: string | null, editor = 'number', isSpare = false) =>
  ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null, choices: null, help: null })
const layout = (length: number, sections: unknown[], profile = 'Later packed profile') =>
  ({ supported: true, editable: true, profile, length, revision: 1, sha256: 'fixture-hash', sections })
function editor(number: number, data: unknown, permissions = 3) {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': permissions })
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(data))
  render(<NativeTableEditor setId="set-fixture" number={number} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
}
function openSpares() {
  screen.getByText(/^Spare fields/).closest('details')!.open = true
}
async function savedChanges() {
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  return JSON.parse(vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')![1]!.body as string).changes
}

it('offers an explicitly inactive coin template to a record author', async () => {
  const template = { profile: 'MTR 1.9 packed', length: 105 }
  vi.mocked(apiPostRaw).mockResolvedValue(new Response(null, { status: 204 }))
  editor(50, { supported: false, reason: 'No record in this set.', templates: [template] }, 5)
  expect(await screen.findByText(/all validator windows and acceptance disabled/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Start record for MTR 1.9 packed' }))
  await waitFor(() => expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/set-fixture/records/50/fields', template))
})

it('keeps validator acceptance separate from per-slot and cashbox spares', async () => {
  editor(50, layout(105, [
    { key: 'cash', label: 'Cash box and escrow', fields: [field('cashBoxVolume', 'Cash box capacity', '90'),
      field('spare.byte8', 'Spare byte 8', '90', 'number', true)] },
    { key: 'coin-0', label: 'Coin slot 1', fields: [
      field('coin-0.value', 'Coin value (cents)', '25'),
      field('coin-0.window', 'Enable validator window', 'true', 'checkbox'),
      field('coin-0.accepted', 'Accept coin', 'true', 'checkbox'),
      field('coin-0.spare.flags.bit7', 'Coin validator flags spare bit 7 (0x80)', 'true', 'checkbox', true),
    ] },
  ], 'MTR 1.9 packed'))
  const section = await screen.findByLabelText('Section or entry')
  openSpares()
  fireEvent.change(screen.getByLabelText('Spare byte 8'), { target: { value: '129' } })
  fireEvent.change(section, { target: { value: 'coin-0' } })
  fireEvent.change(screen.getByLabelText('Coin value (cents)'), { target: { value: '30' } })
  openSpares()
  fireEvent.click(screen.getByLabelText('Coin validator flags spare bit 7 (0x80)'))
  expect(screen.getByLabelText('Enable validator window')).toBeChecked()
  expect(screen.getByLabelText('Accept coin')).toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'spare.byte8': '129', 'coin-0.value': '30', 'coin-0.spare.flags.bit7': 'false' })
})

it('edits a tariff header spare without staging owner or tariff entries', async () => {
  editor(73, layout(1192, [{ key: 'effective', label: 'Tariff effective date', fields: [
    field('telco', 'Rate owner code', '2'), field('spare.byte32', 'Spare byte 32', '206', 'number', true),
  ] }]))
  await screen.findByLabelText('Rate owner code')
  openSpares()
  fireEvent.change(screen.getByLabelText('Spare byte 32'), { target: { value: '129' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'spare.byte32': '129' })
})

it('retains the stored-value validation fields as blank write-only inputs and distinct spare sections', async () => {
  editor(93, layout(225, [
    { key: 'discount-reserved', label: 'Discount rate reserved configuration', fields: [
      field('spare.discount.byte6', 'Spare byte 6', '182', 'number', true)] },
    { key: 'reserved', label: 'Reserved configuration', fields: [field('spare.byte34', 'Spare byte 34', '91', 'number', true)] },
    { key: 'stored-value-keys', label: 'Encoded validation keys', fields: [
      { ...field('validation-key-0', 'Encoded validation key slot 0', null, 'password-hex'), maxLength: 16 }] },
  ]))
  const section = await screen.findByLabelText('Section or entry')
  fireEvent.change(section, { target: { value: 'stored-value-keys' } })
  expect(screen.getByLabelText('Encoded validation key slot 0')).toHaveAttribute('type', 'password')
  expect(screen.getByLabelText('Encoded validation key slot 0')).toHaveValue('')
  fireEvent.change(section, { target: { value: 'discount-reserved' } })
  openSpares()
  fireEvent.change(screen.getByLabelText('Spare byte 6'), { target: { value: '99' } })
  fireEvent.change(section, { target: { value: 'reserved' } })
  openSpares()
  fireEvent.change(screen.getByLabelText('Spare byte 34'), { target: { value: '129' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'spare.discount.byte6': '99', 'spare.byte34': '129' })
})

it('keeps the international fallback selector independent of its spare flags', async () => {
  editor(151, layout(604, [{ key: 'default', label: 'International fallback', fields: [
    { ...field('noMatch', 'When no destination matches', '1', 'select'), choices: [
      { value: 0, label: 'Ask CoinLine' }, { value: 1, label: 'Use default rate' }, { value: 2, label: 'Reject the call' },
    ] },
    field('spare.fallback.bit7', 'Fallback flags spare bit 7 (0x80)', 'true', 'checkbox', true),
    field('spare.byte1', 'Spare byte 1', '165', 'number', true),
  ] }]))
  const fallback = await screen.findByLabelText('When no destination matches')
  openSpares()
  fireEvent.click(screen.getByLabelText('Fallback flags spare bit 7 (0x80)'))
  expect(fallback).toHaveValue('1')
  fireEvent.change(fallback, { target: { value: '2' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'spare.fallback.bit7': 'false', noMatch: '2' })
})

it('uses separate typed discount controls and never submits the hidden legacy byte alias', async () => {
  editor(152, layout(95, [
    { key: 'period-0', label: 'Weekdays, period 1', fields: [
      field('period-0.local', 'Local discount byte', '194'),
      { ...field('period-0.localIndex', 'Local discount', '2', 'select'), choices: [{ value: 2, label: 'MT93 discount 3' }] },
      { ...field('period-0.localPayment', 'Local payment', '0', 'select'), choices: [
        { value: 0, label: 'Disabled' }, { value: 3, label: 'Coins and cards' }] },
      field('period-0.local.spare.flags.bit6', 'Local discount flags spare bit 6 (0x40)', 'true', 'checkbox', true),
    ] },
    { key: 'reserved', label: 'Reserved configuration', fields: [field('spare.byte10', 'Spare byte 10', '90', 'number', true)] },
  ]))
  const payment = await screen.findByLabelText('Local payment')
  expect(screen.queryByLabelText('Local discount byte')).toBeNull()
  openSpares()
  fireEvent.click(screen.getByLabelText('Local discount flags spare bit 6 (0x40)'))
  fireEvent.change(payment, { target: { value: '3' } })
  expect(screen.getByLabelText('Local discount')).toHaveValue('2')
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'reserved' } })
  openSpares()
  fireEvent.change(screen.getByLabelText('Spare byte 10'), { target: { value: '129' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'period-0.local.spare.flags.bit6': 'false', 'period-0.localPayment': '3', 'spare.byte10': '129' })
})
