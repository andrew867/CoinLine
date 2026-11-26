import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { useState } from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiPostRaw } from '../api/client'
import { NumberingPlanOverlayPanel } from './NumberingPlanOverlayPanel'

vi.mock('../api/client', () => ({ apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const endpoint = '/api/tables/sets/fixture/records/62/fields'
const profile = 'Later packed profile', revision = 3, sha256 = 'a'.repeat(64)
const overlay = { message_type: 62, record_length: 353, segments: [{ offset: 1, hex: '00'.repeat(18) },
  { offset: 32, hex: '01'.repeat(321) }] }

function show(dirty = false) {
  const applied = vi.fn()
  function Fixture() {
    const [busy, setBusy] = useState(false)
    return <NumberingPlanOverlayPanel endpoint={endpoint} revision={revision} sha256={sha256} profile={profile}
      dirty={dirty} busy={busy} onBusyChange={setBusy} onApplied={applied} />
  }
  render(<Fixture />)
  screen.getByText('Import ordinary numbering and dialing rules').closest('details')!.open = true
  return applied
}

async function upload(data: unknown) {
  fireEvent.change(screen.getByLabelText('Numbering overlay file'), { target: { files: [new File([JSON.stringify(data)], 'routing.json', { type: 'application/json' })] } })
  await waitFor(() => expect(screen.getByLabelText('Overlay source firmware layout')).toBeEnabled())
}

it('requires a matching source profile and current non-blocking preview before applying', async () => {
  const applied = show()
  const result = { changedBytes: 8, resultSha256: 'b'.repeat(64), findings: [], canApply: true, previewToken: 'fixture-preview' }
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ ...result, canApply: false, findings: [{ blocking: true, message: 'Invalid rule link' }] }))
    .mockResolvedValueOnce(Response.json(result))
    .mockResolvedValueOnce(Response.json({ error: 'The source or destination changed. Preview the overlay again.' }, { status: 409 }))
    .mockResolvedValueOnce(Response.json(result)).mockResolvedValueOnce(new Response(null, { status: 204 }))
  await upload({ ...overlay, preserve: 'Site header remains unchanged.', irrelevant: 'not sent' })
  const preview = screen.getByRole('button', { name: 'Preview numbering overlay' })
  expect(preview).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Overlay source firmware layout'), { target: { value: 'MTR 1.9 packed' } })
  expect(preview).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Overlay source firmware layout'), { target: { value: profile } })
  fireEvent.click(preview)
  expect(await screen.findByText('Blocking: Invalid rule link')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Apply numbering overlay' })).toBeNull()
  fireEvent.click(preview)
  const region = await screen.findByRole('region', { name: 'Numbering overlay preview' })
  expect(within(region).getByText(/8 of 353 bytes/)).toBeVisible()
  fireEvent.click(within(region).getByRole('button', { name: 'Apply numbering overlay' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Preview the overlay again.')
  expect(applied).not.toHaveBeenCalled()
  fireEvent.click(preview)
  fireEvent.click(await screen.findByRole('button', { name: 'Apply numbering overlay' }))
  await waitFor(() => expect(applied).toHaveBeenCalledOnce())
  expect(vi.mocked(apiPostRaw).mock.calls[0]).toEqual([`${endpoint}/numbering-overlay/preview`,
    { revision, sha256, profile, sourceProfile: profile, overlay }])
  expect(vi.mocked(apiPostRaw).mock.calls.at(-1)).toEqual([`${endpoint}/numbering-overlay/apply`,
    { revision, sha256, profile, sourceProfile: profile, overlay, previewToken: 'fixture-preview' }])
})

it('rejects a file that contains protected regions before sending a request', async () => {
  show()
  fireEvent.change(screen.getByLabelText('Numbering overlay file'), { target: { files: [new File([JSON.stringify({ ...overlay,
    segments: [overlay.segments[0], { ...overlay.segments[1], offset: 19 }] })], 'invalid.json')] } })
  expect(await screen.findByRole('alert')).toHaveTextContent('two permitted ordinary-routing segments')
  expect(screen.getByRole('button', { name: 'Preview numbering overlay' })).toBeDisabled()
  expect(apiPostRaw).not.toHaveBeenCalled()
})

it('does not allow importing while other native changes are unsaved', () => {
  show(true)
  expect(screen.getByText(/Save or discard pending record edits/)).toBeVisible()
  expect(screen.getByLabelText('Numbering overlay file')).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Preview numbering overlay' })).toBeDisabled()
})
