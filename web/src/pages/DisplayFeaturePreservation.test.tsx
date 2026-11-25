import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

it.each([false, true])('edits display flashing independently of other display options and language count, later=%s', async later => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const flag = (key: string, label: string, value: string, isSpare = false) => ({ key, label, value, editor: 'checkbox', min: 0,
    max: 1, maxLength: null, choices: null, help: null, isSpare })
  const count = { key: 'languageCount', label: 'Number of languages', value: '2', editor: 'number', min: 1, max: 4,
    maxLength: null, choices: null, help: 'Other imported bits are retained.' }
  let revision = 1
  let fields = [flag('displayNumber', 'Display called number', 'true'), flag('flashServiceLevel', 'Flash degraded-service status messages', 'false'),
    flag('hideCalling', 'Suppress the calling prompt', 'true'), flag('spare.display.bit3', 'Display prompt control spare bit 3 (0x08)', 'false', true),
    flag('spare.advertising.bit6', 'Advertising and date display spare bit 6 (0x40)', 'false', true), ...(later ? [count] : [])]
  const saves: Record<string, string>[] = []
  vi.mocked(apiFetch).mockImplementation(async (_, init) => {
    if (init?.method === 'PUT') {
      const body = JSON.parse(init.body as string); saves.push(body.changes)
      fields = fields.map(field => field.key in body.changes ? { ...field, value: body.changes[field.key] } : field); revision++
      return new Response(null, { status: 204 })
    }
    return Response.json({ supported: true, editable: true, profile: later ? 'Later packed profile' : 'MTR 1.9 packed', length: 72,
      revision, sha256: `fixture-${revision}`, sections: [{ key: 'display', label: 'Display and advertising', fields }] })
  })
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={26} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
  const flash = await screen.findByLabelText('Flash degraded-service status messages')
  expect(flash).not.toBeChecked()
  expect(screen.getByLabelText('Display called number')).toBeChecked()
  expect(screen.getByLabelText('Suppress the calling prompt')).toBeChecked()
  expect(screen.getByText(/^Spare fields/).closest('details')).not.toHaveAttribute('open')
  fireEvent.click(flash)
  if (later) fireEvent.change(screen.getByRole('spinbutton', { name: /^Number of languages/ }), { target: { value: '1' } })
  else expect(screen.queryByRole('spinbutton', { name: /^Number of languages/ })).toBeNull()
  screen.getByText(/^Spare fields/).closest('details')!.open = true
  fireEvent.click(screen.getByLabelText('Display prompt control spare bit 3 (0x08)'))
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(saves).toEqual([{ flashServiceLevel: 'true', 'spare.display.bit3': 'true', ...(later ? { languageCount: '1' } : {}) }]))
  await screen.findByText(/72 bytes · Revision 2/)
  expect(screen.getByLabelText('Flash degraded-service status messages')).toBeChecked()
  expect(screen.getByLabelText('Display called number')).toBeChecked()
  expect(screen.getByLabelText('Suppress the calling prompt')).toBeChecked()
  if (later) expect(screen.getByRole('spinbutton', { name: /^Number of languages/ })).toHaveValue(1)
})
