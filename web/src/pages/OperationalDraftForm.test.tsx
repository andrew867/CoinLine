import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { OperationalDraftForm, type OperationalSpec } from './OperationalDraftForm'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })
const numeric = (key: string, label: string) => ({ key, label, editor: 'number' as const, min: 0, max: 32767, choices: null, help: null })
const flag = (key: string, label: string) => ({ key, label, editor: 'checkbox' as const, min: 0, max: 255, choices: null, help: null })
const timing: OperationalSpec = { number: 30, name: 'User interface parameters', profile: 'MTR 1.9 packed', length: 68,
  sections: [{ key: 'prompts', label: 'Prompt and input timing', fields: [numeric('clearDigits', 'Clear digits after')] },
    { key: 'display', label: 'Display timing', fields: [numeric('scrollSpeed', 'Display scroll timing')] }] }
const modem = (later: boolean): OperationalSpec => ({ number: 33, name: 'Modem parameters', profile: later ? 'Later packed profile' : 'MTR 1.9 packed', length: 36,
  sections: [{ key: 'modem', label: 'Modem line timing', fields: [flag('carrierLossTimeout', 'Enable carrier-loss timeout'),
    flag('answerToneDetection', 'Enable answer tone detection'), ...(later ? [numeric('answerRemovalDelay', 'Delay after answer tone removal')] : [])] }] })

it('authors a missing record through named settings and manual entry, preserving failed-create values', async () => {
  consoleSession.save('fixture-token', '', 'Fixture administrator', 'Admin')
  let created = false; const saved = vi.fn(), dirty = vi.fn()
  vi.mocked(apiFetch).mockImplementation(async url => Response.json(String(url).includes('operational-authoring') ? { templates: [timing] } : created ? {
    supported: true, editable: true, profile: timing.profile, length: timing.length, revision: 1, sha256: 'fixture-hash',
    sections: [{ key: 'prompts', label: 'Prompt and input timing', fields: [{ ...numeric('clearDigits', 'Clear digits after'), value: '100', maxLength: null }] }],
  } : { supported: false, reason: 'Import a complete record to open its editor.', templates: [] }))
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ error: 'Fixture conflict' }, { status: 409 }))
    .mockImplementationOnce(async () => { created = true; return new Response(null, { status: 204 }) })
  render(<NativeTableEditor setId="fixture" number={30} revision={0} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={saved} onDirty={dirty} />)
  const submit = await screen.findByRole('button', { name: 'Create operational record' })
  expect(submit).toBeDisabled()
  expect(screen.getByLabelText(/Clear digits after/)).toHaveValue(null)
  screen.getByText('Import reviewed named settings').closest('details')!.open = true
  fireEvent.change(screen.getByLabelText('Named settings JSON'), { target: { value: '{"unknown":1}' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review named settings' }))
  expect(screen.getByRole('alert')).toHaveTextContent('Unknown setting')
  fireEvent.change(screen.getByLabelText('Named settings JSON'), { target: { value: '{"clearDigits":100}' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review named settings' }))
  expect(screen.getByRole('region', { name: 'Named settings review' })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Apply reviewed settings' }))
  expect(screen.getByLabelText(/Clear digits after/)).toHaveValue(100)
  expect(submit).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Settings section'), { target: { value: 'display' } })
  fireEvent.change(screen.getByLabelText(/Display scroll timing/), { target: { value: '40000' } })
  expect(submit).toBeDisabled()
  fireEvent.change(screen.getByLabelText(/Display scroll timing/), { target: { value: '25' } })
  expect(submit).toBeEnabled()
  expect(dirty).toHaveBeenLastCalledWith(true)
  fireEvent.click(submit)
  expect(await screen.findByText('Fixture conflict')).toBeVisible()
  expect(screen.getByLabelText(/Display scroll timing/)).toHaveValue(25)
  fireEvent.click(submit)
  await waitFor(() => expect(saved).toHaveBeenCalledTimes(1))
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/fixture/records/30/operational-authoring', {
    profile: timing.profile, length: 68, fields: { clearDigits: '100', scrollSpeed: '25' },
  })
  expect(dirty).toHaveBeenLastCalledWith(false)
  await waitFor(() => expect(screen.queryByRole('form', { name: 'Create operational record' })).toBeNull())
})

it('keeps unbound modem profiles independent and sets only unentered switches off when requested', () => {
  const create = vi.fn()
  render(<OperationalDraftForm templates={[modem(false), modem(true)]} busy={false} onCreate={create} onDirty={vi.fn()} />)
  expect(screen.getByRole('button', { name: 'Create operational record' })).toBeDisabled()
  const profile = screen.getByLabelText('Operational layout')
  fireEvent.change(profile, { target: { value: 'MTR 1.9 packed' } })
  fireEvent.change(screen.getByLabelText(/Enable carrier-loss timeout/), { target: { value: 'true' } })
  fireEvent.click(screen.getByRole('button', { name: 'Set unentered switches to Off' }))
  expect(screen.getByLabelText(/Enable carrier-loss timeout/)).toHaveValue('true')
  expect(screen.getByLabelText(/Enable answer tone detection/)).toHaveValue('false')
  expect(screen.queryByLabelText(/Delay after answer tone removal/)).toBeNull()
  fireEvent.change(profile, { target: { value: 'Later packed profile' } })
  expect(screen.getByLabelText(/Enable carrier-loss timeout/)).toHaveValue('')
  expect(screen.getByLabelText(/Delay after answer tone removal/)).toBeVisible()
  fireEvent.change(profile, { target: { value: 'MTR 1.9 packed' } })
  expect(screen.getByLabelText(/Enable carrier-loss timeout/)).toHaveValue('true')
  fireEvent.click(screen.getByRole('button', { name: 'Create operational record' }))
  expect(create).toHaveBeenCalledWith({ profile: 'MTR 1.9 packed', length: 36, fields: { carrierLossTimeout: 'true', answerToneDetection: 'false' } })
})

it('does not load authoring choices for a read-only operator', async () => {
  consoleSession.save('fixture-token', '', 'Fixture reader', 'Operator', { 'table-records': 1 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ supported: false, reason: 'Import a complete record to open its editor.', templates: [] }))
  render(<NativeTableEditor setId="fixture" number={30} revision={0} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
  expect(await screen.findByText('Import a complete record to open its editor.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create operational record' })).toBeNull()
  expect(apiFetch).toHaveBeenCalledTimes(1)
})

it('explains that an observed-length-only target still needs an independently reviewed import', async () => {
  consoleSession.save('fixture-token', '', 'Fixture administrator', 'Admin')
  vi.mocked(apiFetch).mockImplementation(async url => Response.json(String(url).includes('operational-authoring')
    ? { templates: [] } : { supported: false, reason: 'Import a complete record to open its editor.', templates: [], observedLengthsOnly: true }))
  render(<NativeTableEditor setId="fixture" number={26} revision={0} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
  expect(await screen.findByText(/No confirmed authoring layout is available/)).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create operational record' })).toBeNull()
})
