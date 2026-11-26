import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

it.each([true, false])('shows data port spare flags separately and saves only deliberate changes, later=%s', async later => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const flag = (key: string, label: string, value: string, isSpare = false) => ({ key, label, value, editor: 'checkbox', min: 0, max: 1,
    maxLength: null, choices: null, help: null, isSpare })
  let fields = [
    flag('dataPortEnabled', 'Enable data port calls', 'true'), flag('dataPortMute', 'Mute handset transmitter immediately', 'true'),
    flag('dataPortFreeCalls', 'Allow free local calls on the data port', 'false'),
    flag('dataPortDirectoryAssistance', 'Allow directory assistance on the data port', 'false'),
    flag('dataPortDisableKeypad', 'Disable keypad during connected data calls', 'false'),
    flag('spare.dataPort.bit5', 'Data port control spare bit 5 (0x20)', 'true', true),
    flag('spare.dataPort.bit6', 'Data port control spare bit 6 (0x40)', 'false', true),
    flag('spare.dataPort.bit7', 'Data port control spare bit 7 (0x80)', 'true', true),
  ]
  let revision = 1
  const saves: Record<string, string>[] = []
  vi.mocked(apiFetch).mockImplementation(async (_, init) => {
    if (init?.method === 'PUT') {
      const body = JSON.parse(init.body as string); saves.push(body.changes)
      fields = fields.map(field => field.key in body.changes ? { ...field, value: body.changes[field.key] } : field); revision++
      return new Response(null, { status: 204 })
    }
    return Response.json({ supported: true, editable: true, profile: later ? 'Later packed profile' : 'MTR 1.9 packed', length: 72,
      revision, sha256: `fixture-${revision}`, sections: [{ key: 'dataPort', label: 'Data port', fields }] })
  })
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={26} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
  expect(await screen.findByLabelText('Enable data port calls')).toBeChecked()
  expect(screen.getByLabelText('Allow free local calls on the data port')).not.toBeChecked()
  expect(screen.getByText(/^Spare fields/).closest('details')).not.toHaveAttribute('open')
  fireEvent.click(screen.getByLabelText('Allow directory assistance on the data port'))
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(saves).toEqual([{ dataPortDirectoryAssistance: 'true' }]))
  await screen.findByText(/72 bytes · Revision 2/)
  screen.getByText(/^Spare fields/).closest('details')!.open = true
  expect(screen.getByLabelText('Data port control spare bit 5 (0x20)')).toBeChecked()
  expect(screen.getByLabelText('Data port control spare bit 7 (0x80)')).toBeChecked()
  fireEvent.click(screen.getByLabelText('Data port control spare bit 6 (0x40)'))
  fireEvent.click(screen.getByLabelText('Data port control spare bit 7 (0x80)'))
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(saves).toEqual([{ dataPortDirectoryAssistance: 'true' }, { 'spare.dataPort.bit6': 'true', 'spare.dataPort.bit7': 'false' }]))
  await screen.findByText(/72 bytes · Revision 3/)
  expect(screen.getByLabelText('Allow directory assistance on the data port')).toBeChecked()
  expect(screen.getByLabelText('Mute handset transmitter immediately')).toBeChecked()
  screen.getByText(/^Spare fields/).closest('details')!.open = true
  expect(screen.getByLabelText('Data port control spare bit 5 (0x20)')).toBeChecked()
  expect(screen.getByLabelText('Data port control spare bit 6 (0x40)')).toBeChecked()
  expect(screen.getByLabelText('Data port control spare bit 7 (0x80)')).not.toBeChecked()
})
