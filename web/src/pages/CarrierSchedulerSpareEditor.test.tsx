import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
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
  render(<MemoryRouter><NativeTableEditor setId="set-fixture" number={number} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
}
function openSpares() { screen.getByText(/^Spare fields/).closest('details')!.open = true }
async function savedChanges() {
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  return JSON.parse(vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')![1]!.body as string).changes
}

it.each([23, 135])('keeps MT%s carrier acceptance and branding separate from row and header spares', async number => {
  const later = number === 135
  const tailKey = later ? 'spare.byte10' : 'spare.byte2'
  const rowKey = later ? 'carrier-0.spare.rate.bit7' : 'carrier-0.spare.cardBitmap.bit7'
  const rowLabel = later ? 'Terminal rate acceptance spare bit 7 (0x80)' : 'Card bitmap spare bit 7 (0x80)'
  editor(number, layout(later ? 1109 : 678, [
    { key: 'selection', label: 'Carrier selection', fields: [field('primary', 'Primary carrier reference', '9'),
      field(tailKey, 'Last spare byte', '165', 'number', true)] },
    { key: 'carrier-0', label: 'Carrier 0', fields: [
      field('carrier-0.reference', 'Carrier reference', '9'),
      { ...field('carrier-0.name', 'Display name', 'Fixture brand', 'text'), maxLength: 20 },
      field('carrier-0.card-0', 'Accept card range 1', 'true', 'checkbox'),
      field(rowKey, rowLabel, 'true', 'checkbox', true),
    ] },
  ], later ? 'Later packed profile' : 'MTR 1.9 packed'))
  const section = await screen.findByLabelText('Section or entry')
  openSpares()
  fireEvent.change(screen.getByLabelText('Last spare byte'), { target: { value: '129' } })
  fireEvent.change(section, { target: { value: 'carrier-0' } })
  expect(screen.getByRole('img', { name: /lower line Fixture brand/ })).toBeVisible()
  openSpares()
  fireEvent.click(screen.getByLabelText(rowLabel))
  expect(screen.getByLabelText('Accept card range 1')).toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ [tailKey]: '129', [rowKey]: 'false' })
})

it('shows typed scheduler spares while retaining write-only authentication and hiding the legacy alias', async () => {
  editor(94, layout(100, [
    { key: 'schedule', label: 'Code download schedule', fields: [
      field('enabled', 'Enable the scheduled firmware download', 'false', 'checkbox'),
      { ...field('serverAuthentication', 'Code server authentication', null, 'password-hex'), maxLength: 12 },
    ] },
    { key: 'reserved', label: 'Reserved code-download bytes', fields: [
      field('spareParameters', 'Encoded spare parameters', '00'.repeat(26), 'text'),
      field('spare.byte26', 'Spare byte 26', '91', 'number', true),
    ] },
  ]))
  const section = await screen.findByLabelText('Section or entry')
  expect(screen.getByLabelText('Code server authentication')).toHaveValue('')
  expect(screen.getByLabelText('Code server authentication')).toHaveAttribute('type', 'password')
  expect(screen.getByLabelText('Enable the scheduled firmware download')).not.toBeChecked()
  fireEvent.change(section, { target: { value: 'reserved' } })
  expect(screen.queryByLabelText('Encoded spare parameters')).toBeNull()
  openSpares()
  fireEvent.change(screen.getByLabelText('Spare byte 26'), { target: { value: '129' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'spare.byte26': '129' })
})

it('still supports the encoded scheduler spare field when connected to an older API', async () => {
  editor(94, layout(100, [{ key: 'reserved', label: 'Reserved code-download bytes', fields: [
    field('spareParameters', 'Encoded spare parameters', '00'.repeat(26), 'text'),
  ] }]))
  const legacy = await screen.findByLabelText('Encoded spare parameters')
  fireEvent.change(legacy, { target: { value: '00'.repeat(25) + '81' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ spareParameters: '00'.repeat(25) + '81' })
})
