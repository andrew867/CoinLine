import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

const field = (key: string, label: string, value: string | null, editor = 'number', isSpare = false) =>
  ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null, choices: null, help: null })
const icon = (key: string) => [
  field(`${key}.icon`, 'Packed icon byte', '195', 'screen-icon'),
  { ...field(`${key}.iconId`, 'Icon ID', '3', 'select'), max: 63,
    choices: Array.from({ length: 64 }, (_, value) => ({ value, label: String(value) })) },
  field(`${key}.spare.icon.bit6`, 'Icon flags spare bit 6 (0x40)', 'true', 'checkbox', true),
  field(`${key}.spare.icon.bit7`, 'Icon flags spare bit 7 (0x80)', 'true', 'checkbox', true),
]
const layout = (sections: unknown[]) => ({ supported: true, editable: true, profile: 'Later packed profile',
  length: 9224, revision: 1, sha256: 'fixture-hash', sections })
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

it('explains the shared rotation control and edits its reserved header independently', async () => {
  editor(132, layout([{ key: 'rotation', label: 'Graphical screens and rotation', fields: [
    field('onHook', 'Display while on hook', 'true', 'checkbox'),
    field('spare.control.bit7', 'Graphical control spare bit 7 (0x80)', 'true', 'checkbox', true),
    field('spare.byte1', 'Header spare byte', '165', 'number', true),
  ] }]))
  expect(await screen.findByText(/MT131 to control rotation for all 24 screens/)).toBeVisible()
  openSpares()
  fireEvent.click(screen.getByLabelText('Graphical control spare bit 7 (0x80)'))
  fireEvent.change(screen.getByLabelText('Header spare byte'), { target: { value: '129' } })
  expect(screen.getByLabelText('Display while on hook')).toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'spare.control.bit7': 'false', 'spare.byte1': '129' })
})

it('uses the masked softkey icon selector and independent spare flags without submitting the packed alias', async () => {
  editor(133, layout([{ key: 'button-0', label: 'Screen 17, key 1', graphicalScreenIndex: 0, fields: icon('button-0') }]))
  const selector = await screen.findByLabelText('Icon ID')
  expect(selector).toHaveValue('3')
  expect(screen.queryByLabelText('Packed icon byte')).toBeNull()
  openSpares()
  fireEvent.click(screen.getByLabelText('Icon flags spare bit 7 (0x80)'))
  fireEvent.change(selector, { target: { value: '5' } })
  expect(screen.getByLabelText('Icon flags spare bit 6 (0x40)')).toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'button-0.spare.icon.bit7': 'false', 'button-0.iconId': '5' })
})

it('retains compatibility with an older backend that exposes only the packed icon field', async () => {
  editor(131, layout([{ key: 'button-0', label: 'Screen 1, key 1', fields: [
    field('button-0.icon', 'Icon ID', '195', 'screen-icon'),
  ] }]))
  const selector = await screen.findByLabelText('Icon ID')
  expect(selector).toHaveValue('3')
  fireEvent.change(selector, { target: { value: '5' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'button-0.icon': '197' })
})

it('retains the mixed panel preview while editing its text and independent reserved bytes', async () => {
  const key = 'screen-0.panel-3'
  editor(133, layout([{ key, label: 'Screen 17, Panel 4', graphicalScreenIndex: 0, showPanelPreview: true, fields: [
    ...Array.from({ length: 4 }, (_, index) => ({ ...field(`${key}.line-${index}`, `Text line ${index + 1}`, index === 0 ? 'Menu' : '', 'text'), maxLength: 19 })),
    ...Array.from({ length: 6 }, (_, index) => ({ ...field(`${key}.label-${index}`, `Key ${index + 1} label`, index === 0 ? 'Start' : '', 'text'), maxLength: 9 })),
    field(`${key}.spare.byte23`, 'Panel control spare byte 23', '108', 'number', true),
  ] }]))
  await screen.findByText('Mixed panel preview')
  const preview = screen.getByLabelText('Mixed panel display')
  expect(within(preview).getByText(/Menu/)).toBeVisible()
  openSpares()
  fireEvent.change(screen.getByLabelText('Panel control spare byte 23'), { target: { value: '129' } })
  fireEvent.change(screen.getByLabelText('Text line 1'), { target: { value: 'Info' } })
  expect(within(preview).getByText(/Info/)).toBeVisible()
  expect(within(preview).getByText(/Start/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ [`${key}.spare.byte23`]: '129', [`${key}.line-0`]: 'Info' })
})

it('allows read-only inspection and clears obsolete pending body edits when replacing a screen type', async () => {
  const data = layout([{ key: 'screen-0', label: 'Screen 1', graphicalScreenIndex: 0, fields: [
    { ...field('screen-0.kind', 'Screen type', '84', 'screen-kind'), choices: [
      { value: 66, label: 'Bitmap' }, { value: 84, label: 'Text' }] },
    field('screen-0.text', 'Screen text', 'Menu', 'screen-text'),
    field('screen-0.spare.text.byte18', 'Text body spare byte 18', '42', 'number', true),
  ] }])
  editor(131, data, 1)
  await screen.findByText('Read-only access')
  openSpares()
  expect(screen.getByLabelText('Text body spare byte 18')).toBeDisabled()
  expect(screen.queryByRole('button', { name: 'Save record' })).toBeNull()
  cleanup()
  vi.clearAllMocks()
  const confirmation = vi.spyOn(window, 'confirm').mockReturnValue(true)
  editor(131, data)
  await screen.findByRole('textbox', { name: /^Screen text/ })
  openSpares()
  fireEvent.change(screen.getByLabelText('Text body spare byte 18'), { target: { value: '99' } })
  fireEvent.change(screen.getByLabelText('Screen type'), { target: { value: '66' } })
  expect(confirmation).toHaveBeenCalledOnce()
  expect(screen.getByText('Save the screen type to load its editor.')).toBeVisible()
  expect(screen.queryByLabelText('Text body spare byte 18')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(await savedChanges()).toEqual({ 'screen-0.kind': '66' })
})
