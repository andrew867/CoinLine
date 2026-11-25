import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { InterfaceTimingPanel, interfaceSecondsToTicks, interfaceTicksToSeconds } from './InterfaceTimingPanel'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

it.each([['0', '0'], ['.5', '50'], ['4.50', '450'], ['327.67', '32767'], [' 0.01 ', '1']])('converts %s seconds to exact ticks', (seconds, ticks) => {
  expect(interfaceSecondsToTicks(seconds)).toBe(ticks)
  expect(interfaceTicksToSeconds(ticks)).toBe((Number(ticks) / 100).toFixed(2))
})

it.each(['', '-0.01', '327.68', '1.001', '10e1', 'NaN', '4,50'])('rejects a nonrepresentable duration: %s', seconds => {
  expect(interfaceSecondsToTicks(seconds)).toBeNull()
})

it('does not present negative or unavailable stored values as durations or allow editing read-only data', () => {
  const onChange = vi.fn()
  render(<InterfaceTimingPanel timers={[{ key: 'scrollSpeed', label: 'Display scroll timing', value: '-1', changed: false },
    { key: 'transient', label: 'Transient message time', value: null, changed: false }]} editable={false} onChange={onChange} />)
  expect(screen.getAllByText('Outside supported range')).toHaveLength(2)
  expect(screen.queryByLabelText('New duration (seconds)')).toBeNull()
  expect(onChange).not.toHaveBeenCalled()
})

it('stages one exact duration, preserves other fields and reopens the edited value', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const field = (key: string, label: string, value: string) => ({ key, label: `${label} (10 ms ticks)`, value, editor: 'number', min: 0,
    max: 32767, maxLength: null, choices: null, help: null })
  let revision = 1
  let scroll = '10'
  const savedChanges: Record<string, string>[] = []
  vi.mocked(apiFetch).mockImplementation(async (_, init) => {
    if (init?.method === 'PUT') {
      const body = JSON.parse(init.body as string)
      savedChanges.push(body.changes); scroll = body.changes.scrollSpeed ?? scroll; revision++
      return new Response(null, { status: 204 })
    }
    return Response.json({ supported: true, editable: true, profile: 'MTR 1.9 packed', length: 68, revision,
      sha256: `fixture-${revision}`, sections: [{ key: 'prompts', label: 'Prompt and input timing', fields: [
        field('clearDigits', 'Clear digits after', '450'), field('scrollSpeed', 'Display scroll timing', scroll),
      ] }, { key: 'calls', label: 'Other timing', fields: [field('manualHold', 'Manual dialing hold', '450')] }] })
  })
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={30} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
  const panel = await screen.findByRole('region', { name: 'Interface timing worksheet' })
  expect(within(panel).getByText('0.10 s')).toBeVisible()
  expect(within(panel).getByText('4.50 s')).toBeVisible()
  expect(within(panel).queryByText('Manual dialing hold')).toBeNull()
  expect(screen.getByRole('button', { name: 'Save record' })).toBeDisabled()
  fireEvent.change(within(panel).getByLabelText('Timing field'), { target: { value: 'clearDigits' } })
  fireEvent.change(within(panel).getByLabelText('New duration (seconds)'), { target: { value: '4.50' } })
  fireEvent.click(within(panel).getByRole('button', { name: 'Stage exact ticks' }))
  expect(screen.getByRole('button', { name: 'Save record' })).toBeDisabled()
  fireEvent.change(within(panel).getByLabelText('Timing field'), { target: { value: 'scrollSpeed' } })
  fireEvent.change(within(panel).getByLabelText('New duration (seconds)'), { target: { value: '0.251' } })
  expect(within(panel).getByRole('button', { name: 'Stage exact ticks' })).toBeDisabled()
  fireEvent.change(within(panel).getByLabelText('New duration (seconds)'), { target: { value: '0.25' } })
  expect(within(panel).getByRole('status')).toHaveTextContent('25 ticks')
  fireEvent.click(within(panel).getByRole('button', { name: 'Stage exact ticks' }))
  expect(within(panel).getByText('0.25 s')).toBeVisible()
  expect(screen.getByLabelText('Display scroll timing (10 ms ticks)')).toHaveValue(25)
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'calls' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(savedChanges).toEqual([{ scrollSpeed: '25' }]))
  await waitFor(() => {
    const reopenedPanel = screen.getByRole('region', { name: 'Interface timing worksheet' })
    expect(within(reopenedPanel).getByText('0.25 s')).toBeVisible()
    expect(within(reopenedPanel).getByText('4.50 s')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Save record' })).toBeDisabled()
  })
})
