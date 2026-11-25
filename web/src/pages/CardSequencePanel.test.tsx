import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { CardSequencePanel } from './CardSequencePanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const endpoint = '/api/tables/sets/example/records/134/card-sequence'
const sequence = { available: true, editable: true, cards: { revision: 2, sha256: 'cards' },
  carriers: { revision: 4, sha256: 'carriers' }, carrierNumber: 135,
  slots: [{ number: 1, blank: false, movable: true }, { number: 2, blank: false, movable: false },
    { number: 3, blank: true, movable: false }, { number: 4, blank: false, movable: true }] }

function props() {
  return { setId: 'example', number: 134, revision: 2, sha256: 'cards', editable: true, dirty: false, busy: false,
    labels: { 1: '000001 to 000002' }, onBusyChange: vi.fn(), onMoved: vi.fn(), onReload: vi.fn() }
}

it('offers only configured sources and blank destinations and sends both observed record stamps', async () => {
  const callbacks = props()
  vi.mocked(apiFetch).mockImplementation(async (_, init) => init?.method === 'POST'
    ? Response.json({ carrierChanged: true }) : Response.json(sequence))
  render(<CardSequencePanel {...callbacks} />)
  const source = await screen.findByLabelText('Move from')
  const destination = screen.getByLabelText('Move to')
  expect(screen.getByRole('option', { name: 'Position 1 · 000001 to 000002' })).toBeVisible()
  expect(screen.getByRole('option', { name: 'Position 3 · Blank' })).toBeVisible()
  expect(screen.queryByRole('option', { name: 'Position 2' })).toBeNull()
  expect(screen.queryByRole('option', { name: 'Position 4 · Blank' })).toBeNull()
  const button = screen.getByRole('button', { name: 'Move card entry' })
  expect(button).toBeDisabled()
  fireEvent.change(source, { target: { value: '1' } })
  fireEvent.change(destination, { target: { value: '3' } })
  fireEvent.click(button)
  await waitFor(() => expect(callbacks.onMoved).toHaveBeenCalledWith(3, true))
  const request = vi.mocked(apiFetch).mock.calls.find(([url, init]) => url === endpoint && init?.method === 'POST')
  expect(JSON.parse(String(request?.[1]?.body))).toEqual({ from: 1, to: 3, cards: sequence.cards, carriers: sequence.carriers })
  expect(callbacks.onBusyChange.mock.calls).toEqual([[true], [false]])
})

it('does not allow a move while editing or with a stale companion response', async () => {
  const callbacks = props()
  vi.mocked(apiFetch).mockImplementation(async (_, init) => init?.method === 'POST'
    ? Response.json({ error: 'The carrier record changed.' }, { status: 409 }) : Response.json(sequence))
  const view = render(<CardSequencePanel {...callbacks} dirty />)
  expect(await screen.findByLabelText('Move from')).toBeDisabled()
  expect(screen.getByText('Save the current field edits before moving a card entry.')).toBeVisible()
  view.rerender(<CardSequencePanel {...callbacks} />)
  fireEvent.change(screen.getByLabelText('Move from'), { target: { value: '1' } })
  fireEvent.change(screen.getByLabelText('Move to'), { target: { value: '3' } })
  fireEvent.click(screen.getByRole('button', { name: 'Move card entry' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('The carrier record changed.')
  expect(callbacks.onMoved).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Reload card and carrier records' }))
  expect(callbacks.onReload).toHaveBeenCalledOnce()
})

it('explains a missing companion and renders no write controls for read-only users', async () => {
  const callbacks = props()
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ available: false, reason: 'Include MT135 before moving card positions.' }))
  const first = render(<CardSequencePanel {...callbacks} />)
  expect(await screen.findByText('Include MT135 before moving card positions.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Move card entry' })).toBeNull()
  first.unmount()
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json(sequence))
  render(<CardSequencePanel {...callbacks} editable={false} />)
  expect(await screen.findByText('4 positions; 2 configured; 1 blank.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Move card entry' })).toBeNull()
})
