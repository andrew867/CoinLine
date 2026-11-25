import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
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
function editor(number: number, data: unknown) {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(data))
  render(<NativeTableEditor setId="set-fixture" number={number} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
}
function openSpareFields() {
  screen.getByText(/^Spare fields/).closest('details')!.open = true
}
async function saved() {
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  const request = vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')
  return JSON.parse(request![1]!.body as string)
}

it('labels later predial settings by packet-data route and keeps craft credentials out of unrelated saves', async () => {
  editor(31, layout(37, [
    { key: 'installation', label: 'Installation and packet timing', fields: [field('retries', 'Retries before out of service', '40')] },
    { key: 'predial', label: 'Predial strings', fields: [
      field('predialPrimaryManager', 'Apply predial to the primary packet-data access number', 'false', 'checkbox'),
      field('predialAlternateManager', 'Apply predial to the alternate packet-data access number', 'false', 'checkbox'),
      field('spare.predial.bit7', 'Predial control spare bit 7 (0x80)', 'true', 'checkbox', true),
    ] },
    { key: 'craft', label: 'Craft credentials', fields: [
      { ...field('craftAccessCode', 'Craft access code', null, 'password'), maxLength: 7 },
      { ...field('keyCardNumber', 'Desk terminal key card number', null, 'password'), maxLength: 10 },
    ] },
  ]))
  const section = await screen.findByLabelText('Section or entry')
  fireEvent.change(section, { target: { value: 'craft' } })
  expect(screen.getByLabelText('Craft access code')).toHaveValue('')
  expect(screen.getByLabelText('Craft access code')).toHaveAttribute('type', 'password')
  expect(screen.getByLabelText('Desk terminal key card number')).toHaveValue('')
  fireEvent.change(section, { target: { value: 'predial' } })
  expect(screen.queryByLabelText('Apply predial to the primary manager number')).toBeNull()
  fireEvent.click(screen.getByLabelText('Apply predial to the primary packet-data access number'))
  openSpareFields()
  fireEvent.click(screen.getByLabelText('Predial control spare bit 7 (0x80)'))
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect((await saved()).changes).toEqual({ predialPrimaryManager: 'true', 'spare.predial.bit7': 'false' })
})

it.each([false, true])('keeps the modem timer in its correct profile section (later=%s)', async later => {
  const timer = later
    ? { ...field('answerRemovalDelay', 'Delay after answer tone removal', '4660'), max: 65535 }
    : { ...field('spare.timer', 'Spare timer word', '4660', 'number', true), max: 65535 }
  editor(33, layout(36, [{ key: 'modem', label: 'Modem line timing', fields: [
    field('carrierLossTimeout', 'Enable carrier-loss timeout', 'true', 'checkbox'), timer,
    field('spare.general.bit7', 'General modem configuration spare bit 7 (0x80)', 'true', 'checkbox', true),
  ] }], later ? 'Later packed profile' : 'MTR 1.9 packed'))
  await screen.findByLabelText('Enable carrier-loss timeout')
  if (later) {
    expect(screen.getByLabelText('Delay after answer tone removal').closest('details')).toBeNull()
    expect(screen.queryByLabelText('Spare timer word')).toBeNull()
  } else {
    const spare = screen.getByLabelText('Spare timer word')
    expect(spare.closest('details')).not.toBeNull()
    expect(screen.queryByLabelText('Delay after answer tone removal')).toBeNull()
  }
  openSpareFields()
  fireEvent.change(screen.getByLabelText(timer.label), { target: { value: '1234' } })
  fireEvent.click(screen.getByLabelText('General modem configuration spare bit 7 (0x80)'))
  expect(screen.getByLabelText('Enable carrier-loss timeout')).toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect((await saved()).changes).toEqual({ [timer.key]: '1234', 'spare.general.bit7': 'false' })
})

it('stages an answer-supervision row spare and tail spare without replacing line settings', async () => {
  editor(49, layout(263, [
    { key: 'control', label: 'Line control', fields: [
      field('features', 'Supervision feature flags', '8'), field('spare.byte32', 'Spare byte 32', '91', 'number', true),
    ] },
    { key: 'pattern-15', label: 'Dial pattern 16', fields: [
      field('pattern-15.digits', 'Number pattern', '1234', 'text'),
      field('pattern-15.definition', 'Supervision definition code', '3'),
      field('pattern-15.spare.byte1', 'Spare byte 1', '83', 'number', true),
    ] },
  ]))
  const section = await screen.findByLabelText('Section or entry')
  openSpareFields()
  fireEvent.change(screen.getByLabelText('Spare byte 32'), { target: { value: '129' } })
  fireEvent.change(section, { target: { value: 'pattern-15' } })
  openSpareFields()
  fireEvent.change(screen.getByLabelText('Spare byte 1'), { target: { value: '99' } })
  expect(screen.getByLabelText('Number pattern')).toHaveValue('1234')
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect((await saved()).changes).toEqual({ 'spare.byte32': '129', 'pattern-15.spare.byte1': '99' })
})

it('previews Western promotion prompts and exposes the minimum usage threshold separately from spares', async () => {
  const prompt = (key: string, label: string, value: string | null) =>
    ({ ...field(key, label, value, 'text'), maxLength: 20, displayGlyphs: true })
  editor(95, layout(153, [
    { key: 'display', label: 'Promotion messages', fields: [
      prompt('prompt1', 'First prompt', 'Cafe'), prompt('prompt2', 'Second prompt', null), prompt('prompt3', 'Third prompt', ''),
      field('spare.byte2', 'Spare byte 2', '165', 'number', true),
    ] },
    { key: 'promotion-0', label: 'Promotion range 1', fields: [
      field('promotion-0.enable', 'Enable promotion', 'true', 'checkbox'),
      { ...field('promotion-0.usageThreshold', 'Minimum card usage', '0', 'select'),
        choices: [{ value: 0, label: 'Disabled' }, { value: 1, label: '25% used' }, { value: 2, label: '50% used' }, { value: 3, label: '75% used' }] },
      field('promotion-0.spare.flags.bit7', 'Promotion flags spare bit 7 (0x80)', 'true', 'checkbox', true),
    ] },
  ]))
  const first = await screen.findByLabelText('First prompt')
  fireEvent.change(first, { target: { value: 'Cafe\u0301' } })
  expect(screen.getByRole('img', { name: 'First prompt, Café' })).toBeVisible()
  fireEvent.change(screen.getByLabelText('Preview promotion prompt'), { target: { value: '1' } })
  expect(screen.getByRole('img', { name: 'Second prompt, Unmapped glyphs' })).toBeVisible()
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'promotion-0' } })
  fireEvent.change(screen.getByLabelText('Minimum card usage'), { target: { value: '3' } })
  openSpareFields()
  fireEvent.click(screen.getByLabelText('Promotion flags spare bit 7 (0x80)'))
  expect(screen.getByLabelText('Enable promotion')).toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect((await saved()).changes).toEqual({ prompt1: 'Cafe\u0301', 'promotion-0.usageThreshold': '3', 'promotion-0.spare.flags.bit7': 'false' })
})

it('does not offer the optional promotion editor for an incompatible selected firmware', async () => {
  editor(95, { supported: false, reason: 'This record type is absent from the selected firmware catalogue.' })
  expect(await screen.findByText(/absent from the selected firmware catalogue/)).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Save record' })).toBeNull()
  expect(apiFetch).toHaveBeenCalledTimes(1)
})
