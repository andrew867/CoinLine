import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { languageOrderPlan, swapActiveLanguages } from './languageOrderPlan'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const values = (changes: Record<string, string | null> = {}) => {
  const data: Record<string, string | null> = { languageCount: '2', languageFirst: '2', languageSecond: '1',
    languageThird: '3', languageFourth: '15', ...changes }
  return (key: string) => data[key]
}

it('distinguishes scrolling position and the installed two-language prompt order', () => {
  const value = values()
  const plan = languageOrderPlan(value)
  expect(plan.rows.map(row => [row.name, row.active, row.promptRecord])).toEqual([
    ['French', true, 86], ['English', true, 85], ['Spanish', false, null], ['Stored code 15', false, null],
  ])
  expect(plan.invalid).toEqual([])
  expect(swapActiveLanguages(value, 2, -1)).toEqual({ languageFirst: '1', languageSecond: '2' })
  expect(swapActiveLanguages(value, 2, 1)).toBeNull()
  expect(swapActiveLanguages(value, 3, -1)).toBeNull()
  expect(languageOrderPlan(values({ languageCount: '1', languageFirst: '4' })).rows[0].promptRecord).toBe(85)
})

it('does not invent prompt mappings for excessive, repeated or unrecognised language orders', () => {
  const excessive = languageOrderPlan(values({ languageCount: '3' }))
  expect(excessive.exceedsInstalledPair).toBe(true)
  expect(excessive.rows.every(row => row.promptRecord === null)).toBe(true)
  const unknown = languageOrderPlan(values({ languageSecond: '0' }))
  expect(unknown.invalid).toEqual([2])
  expect(swapActiveLanguages(values({ languageSecond: '0' }), 1, 1)).toBeNull()
  const duplicate = languageOrderPlan(values({ languageSecond: '2' }))
  expect(duplicate.duplicate).toBe(true)
  expect(duplicate.rows.every(row => row.promptRecord === null)).toBe(true)
  expect(swapActiveLanguages(values({ languageSecond: '2' }), 1, 1)).toBeNull()
  expect(languageOrderPlan(values({ languageCount: '0' })).validCount).toBe(false)
  expect(languageOrderPlan(values({ languageCount: null })).validCount).toBe(false)
})

const field = (key: string, label: string, value: string, editor = 'number') => ({
  key, label, value, editor, min: key === 'languageCount' ? 1 : 0, max: key === 'languageCount' ? 4 : 15,
  maxLength: null, help: null, choices: editor === 'select' ? [
    { value: 0, label: 'Unused' }, { value: 1, label: 'English' }, { value: 2, label: 'French' },
    { value: 3, label: 'Spanish' }, { value: 4, label: 'Japanese' },
  ] : null,
})
function show(editable = true) {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': editable ? 3 : 1 })
  vi.mocked(apiFetch).mockImplementation(async (_, init) => init?.method === 'PUT'
    ? new Response(null, { status: 204 })
    : Response.json({
      supported: true, editable, profile: 'Later packed profile', length: 72, revision: 1, sha256: 'fixture-hash',
      sections: [{ key: 'display', label: 'Display and advertising', fields: [
        field('languageCount', 'Number of languages', '2'),
        field('languageFirst', 'Default language', '2', 'select'),
        field('languageSecond', 'Second language', '1', 'select'),
        field('languageThird', 'Third language', '3', 'select'),
        field('languageFourth', 'Fourth language', '15', 'select'),
        field('flashServiceLevel', 'Flash degraded-service status messages', 'false', 'checkbox'),
      ] }],
    }))
  render(<NativeTableEditor setId="fixture" number={26} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
}

it('moves active entries using the existing draft fields without touching the count or inactive positions', async () => {
  show()
  const panel = await screen.findByRole('region', { name: 'Language scrolling order' })
  const first = () => within(panel).getByRole('rowheader', { name: 'Slot 1' }).closest('tr')!
  expect(within(first()).getByRole('cell', { name: 'French' })).toBeVisible()
  expect(within(first()).getByRole('cell', { name: 'MT86' })).toBeVisible()
  fireEvent.click(screen.getByLabelText('Flash degraded-service status messages'))
  fireEvent.click(within(panel).getByRole('button', { name: 'Move slot 2 earlier' }))
  expect(within(first()).getByRole('cell', { name: 'English' })).toBeVisible()
  expect(within(first()).getByRole('cell', { name: 'MT85' })).toBeVisible()
  expect(screen.getByLabelText('Default language')).toHaveValue('1')
  expect(screen.getByLabelText('Second language')).toHaveValue('2')
  expect(screen.getByLabelText('Third language')).toHaveValue('3')
  expect(screen.getByLabelText('Fourth language')).toHaveValue('15')
  expect(screen.getByLabelText('Number of languages')).toHaveValue(2)
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  const payload = vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')![1]!.body as string
  expect(JSON.parse(payload).changes).toEqual({ flashServiceLevel: 'true', languageFirst: '1', languageSecond: '2' })
})

it('shows the configured order without exposing edit controls to a reader', async () => {
  show(false)
  const panel = await screen.findByRole('region', { name: 'Language scrolling order' })
  expect(panel).toHaveTextContent('French')
  expect(within(panel).queryByRole('button')).toBeNull()
})
