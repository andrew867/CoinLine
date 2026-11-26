import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

it('edits ordinary card-present alert timing separately from the fraud-control option', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const timings = [
    ['onHookAlarmDelay', 'On-hook card-present audio alarm delay', '500'],
    ['postOnHookAlarmDelay', 'Post-call card-present audio alarm delay', '200'],
    ['cardAlarmDuration', 'Card-present audio alarm timing', '1000'],
    ['cardAlarmOn', 'Audio alarm cadence on timer', '50'],
    ['cardAlarmOff', 'Audio alarm cadence off timer', '50'],
    ['blockedReaderAlarm', 'Card-reader blocked alarm delay', '300'],
  ]
  let fields = timings.map(([key, label, value]) => ({ key, label, value, editor: 'number', min: 0, max: 65535,
    maxLength: null, choices: null, help: 'Stored timing value; the time unit is not yet verified.' }))
  let revision = 1
  const saves: Record<string, string>[] = []
  vi.mocked(apiFetch).mockImplementation(async (_, init) => {
    if (init?.method === 'PUT') {
      const body = JSON.parse(init.body as string); saves.push(body.changes)
      fields = fields.map(field => field.key in body.changes ? { ...field, value: body.changes[field.key] } : field); revision++
      return new Response(null, { status: 204 })
    }
    return Response.json({ supported: true, editable: true, profile: 'Later packed profile', length: 72,
      revision, sha256: `fixture-${revision}`, sections: [
        { key: 'reporting', label: 'Reporting and supervision', fields: [{ key: 'zeroValueCardAlarm', label: 'Apply the fraud-control alarm to zero-value cards',
          value: 'true', editor: 'checkbox', min: 0, max: 1, maxLength: null, choices: null, help: null }] },
        { key: 'cardAlert', label: 'Card-present audio alert', fields },
      ] })
  })
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={26} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
  expect(await screen.findByLabelText('Apply the fraud-control alarm to zero-value cards')).toBeChecked()
  const selector = screen.getByLabelText('Section or entry')
  fireEvent.change(selector, { target: { value: 'cardAlert' } })
  expect(screen.queryByLabelText('Apply the fraud-control alarm to zero-value cards')).toBeNull()
  for (const [, label, value] of timings) expect(screen.getByRole('spinbutton', { name: new RegExp(`^${label}`) })).toHaveValue(Number(value))
  expect(screen.getAllByText('Stored timing value; the time unit is not yet verified.')).toHaveLength(6)
  fireEvent.change(screen.getByRole('spinbutton', { name: /^On-hook card-present/ }), { target: { value: '600' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(saves).toEqual([{ onHookAlarmDelay: '600' }]))
  await screen.findByText(/72 bytes · Revision 2/)
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'cardAlert' } })
  expect(screen.getByRole('spinbutton', { name: /^On-hook card-present/ })).toHaveValue(600)
  for (const [, label, value] of timings.slice(1)) expect(screen.getByRole('spinbutton', { name: new RegExp(`^${label}`) })).toHaveValue(Number(value))
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'reporting' } })
  expect(screen.getByLabelText('Apply the fraud-control alarm to zero-value cards')).toBeChecked()
})
