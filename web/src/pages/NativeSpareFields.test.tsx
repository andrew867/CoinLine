import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const field = (key: string, label: string, value: string | null, editor = 'number', isSpare = false) =>
  ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null, choices: null, help: null })
const restriction = () => ({ supported: true, editable: true, profile: 'Later packed profile', length: 26, revision: 1, sha256: 'fixture-hash',
  sections: [{ key: 'restriction', label: 'Daily service restriction', fields: [
    { ...field('restriction', 'Restriction level', '1', 'select'), choices: [{ value: 0, label: 'None' }, { value: 1, label: 'Free calls only' }] },
    field('startHour', 'Start hour', '20'), field('startMinute', 'Start minute', '30'),
    field('durationHours', 'Duration hours', '2'), field('durationMinutes', 'Duration minutes', '0'),
    field('spare.byte1', 'Spare byte 1', '17', 'number', true),
    field('spare.byte20', 'Spare byte 20', '42', 'number', true),
  ] }] })

function editor(number: number, layout: unknown, permissions = 3) {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': permissions })
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(layout))
  render(<NativeTableEditor setId="set-fixture" number={number} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
}

function openedSpareFields() {
  const details = screen.getByText(/^Spare fields/).closest('details')!
  details.open = true
  return details
}

async function saved() {
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  const request = vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')
  return JSON.parse(request![1]!.body as string)
}

it('shows typed spare bytes separately and stages only an explicit spare edit', async () => {
  editor(58, restriction())
  await screen.findByText('Spare fields')
  const details = screen.getByText('Spare fields').closest('details')!
  expect(details.open).toBe(false)
  expect(screen.getByLabelText('Duration hours')).toHaveValue(2)
  openedSpareFields()
  expect(screen.getByLabelText('Spare byte 1')).toHaveValue(17)
  fireEvent.change(screen.getByLabelText('Spare byte 20'), { target: { value: '99' } })
  expect(screen.getByText('Spare fields (1 changed)')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect((await saved()).changes).toEqual({ 'spare.byte20': '99' })
})

it('permits a read-only operator to inspect but not change reserved fields', async () => {
  editor(58, restriction(), 1)
  await screen.findByText('Spare fields')
  openedSpareFields()
  expect(screen.getByLabelText('Spare byte 1')).toBeDisabled()
  expect(screen.getByLabelText('Spare byte 20')).toBeDisabled()
  expect(screen.queryByRole('button', { name: 'Save record' })).toBeNull()
})

it('keeps later combined carrier collection independent of the spare-policy flags', async () => {
  const layout = { supported: true, editable: true, profile: 'Later packed profile', length: 19, revision: 1, sha256: 'fixture-hash',
    sections: [{ key: 'summary', label: 'Call and carrier statistics', fields: [
      field('days', 'Collection interval (days)', '1'), field('threshold', 'Threshold', '2'),
      field('startHour', 'Collection start hour', '2'), field('startMinute', 'Collection start minute', '15'),
      { ...field('carrierStatistics', 'Carrier statistics', '17', 'select'), choices: [{ value: 17, label: 'Combined carrier summary' }] },
      field('spare.policy.bit6', 'Collection policy spare bit 6 (0x40)', 'false', 'checkbox', true),
      field('spare.policy.bit7', 'Collection policy spare bit 7 (0x80)', 'true', 'checkbox', true),
    ] }] }
  editor(34, layout)
  await screen.findByText('Spare fields')
  openedSpareFields()
  expect(screen.getByLabelText('Carrier statistics')).toHaveValue('17')
  fireEvent.click(screen.getByLabelText('Collection policy spare bit 7 (0x80)'))
  expect(screen.getByLabelText('Carrier statistics')).toHaveValue('17')
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect((await saved()).changes).toEqual({ 'spare.policy.bit7': 'false' })
})

it('treats PDS as a normal later screening field and does not stage conflicting spare edits during Delete', async () => {
  const layout = { supported: true, editable: true, profile: 'Later packed profile', length: 3061, revision: 1, sha256: 'fixture-hash',
    screening: { descriptions: {}, supportsSpeedDial: true },
    sections: [{ key: 'rule-0', label: 'Screening rule 1', fields: [
      field('rule-0.pattern', 'Number pattern', '1020', 'text'),
      field('rule-0.pds', 'Enable PDS dialing', 'false', 'checkbox'),
      field('rule-0.spare.byte2', 'Spare byte 2', '50', 'number', true),
    ] }] }
  editor(92, layout)
  const pds = await screen.findByLabelText('Enable PDS dialing')
  expect(pds.closest('details')).toBeNull()
  fireEvent.click(pds)
  openedSpareFields()
  fireEvent.change(screen.getByLabelText('Spare byte 2'), { target: { value: '99' } })
  fireEvent.click(screen.getByRole('button', { name: 'Delete entry' }))
  expect(screen.queryByLabelText('Enable PDS dialing')).toBeNull()
  expect(screen.queryByText(/^Spare fields/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Undo screening action' }))
  expect(screen.getByLabelText('Enable PDS dialing')).toBeChecked()
  openedSpareFields()
  expect(screen.getByLabelText('Spare byte 2')).toHaveValue(99)
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect((await saved()).changes).toEqual({ 'rule-0.pds': 'true', 'rule-0.spare.byte2': '99' })
})

it('renders Western Quick Access Key text and retains its legacy message-key flag as a named control', async () => {
  const layout = { supported: true, editable: true, profile: 'Older packed profile', length: 571, revision: 1, sha256: 'fixture-hash',
    sections: [{ key: 'key-5', label: 'Quick-access key 6', fields: [
      { ...field('key-5.upper', 'Upper display line', 'Cafe', 'text'), displayGlyphs: true, maxLength: 20 },
      { ...field('key-5.lower', 'Lower display line', null, 'text'), displayGlyphs: true, maxLength: 20 },
      field('key-5.messageKey', 'Audio-Gram message key', 'false', 'checkbox'),
      field('key-5.spare.flags.bit7', 'Key flags spare bit 7 (0x80)', 'true', 'checkbox', true),
    ] }] }
  editor(55, layout)
  const upper = await screen.findByLabelText('Upper display line')
  expect(screen.getByText(/Stored characters are outside/)).toBeVisible()
  fireEvent.change(upper, { target: { value: 'Cafe\u0301' } })
  expect(screen.getByRole('img', { name: /Upper display line, Café.*Lower display line, unmapped glyphs/ })).toBeVisible()
  fireEvent.click(screen.getByLabelText('Audio-Gram message key'))
  openedSpareFields()
  fireEvent.click(screen.getByLabelText('Key flags spare bit 7 (0x80)'))
  fireEvent.click(screen.getByRole('button', { name: 'Clear key' }))
  expect(screen.queryByText(/^Spare fields/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Undo clear' }))
  expect(screen.getByLabelText('Audio-Gram message key')).toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect((await saved()).changes).toEqual({ 'key-5.upper': 'Cafe\u0301', 'key-5.messageKey': 'true', 'key-5.spare.flags.bit7': 'false' })
})
