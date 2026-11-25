import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet, apiPost } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareVersionsPage } from './FirmwareVersionsPage'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const id = 'msr17-desk-compact-uncompressed'
const label = 'MSR 1.7 Desk, compact screening and uncompressed LCD'
it('distinguishes an acknowledged subset from a complete native catalogue when registering a firmware version', async () => {
  vi.mocked(apiGet).mockImplementation(async url => url === '/api/firmware/versions' ? [] : [{
    id, label, verifiedLengths: { 24: 721, 26: 72 }, terminalAcknowledgedRecords: [24, 26],
    completeCatalogue: false, observedLengthsOnly: true, evidenceNote: 'The retained lab set was acknowledged; physical hardware remains unverified.',
  }] as never)
  vi.mocked(apiPost).mockResolvedValue({ id: 'fixture-version' } as never)
  render(<FirmwareVersionsPage />)
  await screen.findByRole('option', { name: label })
  fireEvent.change(screen.getByLabelText('Table layout'), { target: { value: id } })
  expect(screen.getByText(/Observed subset only; ambiguous native layouts require explicit selection/)).toBeVisible()
  expect(screen.queryByText(/Complete image catalogue/)).toBeNull()
  fireEvent.change(screen.getByLabelText('Firmware label'), { target: { value: 'Synthetic desk build' } })
  fireEvent.click(screen.getByRole('button', { name: 'Register firmware' }))
  await waitFor(() => expect(apiPost).toHaveBeenCalledWith('/api/firmware/versions', {
    label: 'Synthetic desk build', buildId: null, tableProfileId: id,
  }))
})

it('does not silently choose either same-size native layout on an observed target', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const profiles = ['MTR 1.9 packed', 'Later packed profile']
  vi.mocked(apiFetch).mockImplementation(async (url, init) => {
    if (init?.method === 'PUT') return new Response(null, { status: 204 })
    if (!String(url).includes('profile=')) return Response.json({
      supported: false, profiles, observedLengthsOnly: true, reason: 'A record length alone does not determine the layout.',
    })
    return Response.json({
      supported: true, editable: true, profile: profiles[0], profiles, observedLengthsOnly: true,
      length: 72, revision: 1, sha256: 'fixture', sections: [{ key: 'display', label: 'Display',
        fields: [{ key: 'displayNumber', label: 'Display called number', editor: 'checkbox', value: 'false',
          min: 0, max: 1, maxLength: null, choices: null, help: null }] }],
    })
  })
  render(<NativeTableEditor setId="fixture" number={26} revision={1} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
  const selector = await screen.findByLabelText('Firmware profile')
  expect(selector).toHaveValue('')
  expect(screen.queryByLabelText('Display called number')).toBeNull()
  fireEvent.change(selector, { target: { value: profiles[0] } })
  const display = await screen.findByLabelText('Display called number')
  expect(screen.getByText(/Observed firmware profile: this editor is a candidate/)).toBeVisible()
  fireEvent.click(display)
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  const body = vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')![1]!.body as string
  expect(JSON.parse(body)).toMatchObject({ profile: profiles[0], changes: { displayNumber: 'true' } })
})
