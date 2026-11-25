import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { NumberingPlanEditor, type NumberingPlan } from './NumberingPlanEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const field = (key: string, label: string, value: string, isSpare = false) =>
  ({ key, label, value, editor: 'number', isSpare, min: 0, max: 255, maxLength: null, choices: null, help: null })
const rule = { id: 'row-0', noMatch: null, match: null, control: 4, tag: 0, callType: 0,
  digits: [0x51], conversion: null, maximumDigits: null }
const plan = (later: boolean): NumberingPlan => ({
  dial: [], format: [{ ...rule, noMatch: later ? 'row-132' : 'row-12' },
    { ...rule, id: later ? 'row-132' : 'row-12', digits: later ? [0x91, 0x51, 0x91] : [0x91] }],
})

it.each([false, true])('uses firmware-specific format space and sends only chosen rule and spare edits, later=%s', async later => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const data = { supported: true, editable: true, profile: later ? 'Later packed profile' : 'MTR 1.9 packed',
    length: 353, revision: 1, sha256: 'fixture-hash', numberingFormatCapacity: later ? 140 : 132,
    numberingPlan: plan(later), sections: [
      { key: 'numbering', label: 'Digit collection and routing', fields: [field('spare.byte1', 'Spare byte 1', '182', true)] },
      ...(!later ? [{ key: 'number-display', label: 'Number display and currency', fields: [field('currencyWidth', 'Minimum display width', '5')] }] : []),
    ] }
  vi.mocked(apiFetch).mockImplementation(async (_, init) => init?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(data))
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={62} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
  await screen.findByRole('button', { name: 'Numbering rules' })
  if (!later) {
    fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'number-display' } })
    expect(screen.getByLabelText('Minimum display width')).toHaveValue(5)
    fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'numbering' } })
  }
  else expect(screen.queryByText('Number display and currency')).toBeNull()
  screen.getByText(/^Spare fields/).closest('details')!.open = true
  fireEvent.change(screen.getByLabelText('Spare byte 1'), { target: { value: '129' } })
  fireEvent.click(screen.getByRole('button', { name: 'Numbering rules' }))
  fireEvent.change(screen.getByLabelText('Rule set'), { target: { value: 'format' } })
  expect(screen.getByText(later ? '14 of 140 rule bytes used' : '12 of 132 rule bytes used')).toBeVisible()
  fireEvent.change(screen.getByLabelText('Rule', { exact: true }), { target: { value: later ? 'row-132' : 'row-12' } })
  const digit = screen.getByLabelText(later ? 'Digit 3' : 'Digit 1').closest('.numbering-digit')! as HTMLElement
  fireEvent.click(within(digit).getByLabelText('Separator after'))
  fireEvent.click(within(digit).getByLabelText('Separator before'))
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  const body = JSON.parse(vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')![1]!.body as string)
  expect(body.changes).toEqual({ 'spare.byte1': '129' })
  expect(body.numberingPlan).toEqual({ format: [plan(later).format![0], { ...plan(later).format![1], digits: later ? [0x91, 0x51, 0x51] : [0x51] }] })
})

it('reports the exceeded capacity using the selected firmware rather than a shared constant', () => {
  const oversized: NumberingPlan = { dial: [], format: [{ ...rule, digits: Array.from({ length: 136 }, () => 17) }] }
  render(<NumberingPlanEditor plan={oversized} changes={{}} editable={true} formatCapacity={140} onChange={vi.fn()} />)
  fireEvent.change(screen.getByLabelText('Rule set'), { target: { value: 'format' } })
  expect(screen.getByText('141 of 140 rule bytes used')).toBeVisible()
  expect(screen.getByRole('alert')).toHaveTextContent('exceed this firmware')
})
