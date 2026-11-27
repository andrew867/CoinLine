import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { ManagementAccessDraftForm } from './ManagementAccessDraftForm'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })
const earlier = { profile: 'MTR 1.9 packed', length: 18, managerDigits: 12, supportsAdsi: false }
const later = { profile: 'Later packed profile', length: 48, managerDigits: 20, supportsAdsi: true }

it('requires actual site values, preserves them on a failed create and opens the saved native editor', async () => {
  consoleSession.save('fixture-token', '', 'Fixture administrator', 'Admin')
  const saved = vi.fn(), dirty = vi.fn()
  let created = false
  vi.mocked(apiFetch).mockImplementation(async () => Response.json(created ? {
    supported: true, editable: true, profile: later.profile, length: 48, revision: 1, sha256: 'fixture-hash',
    sections: [{ key: 'access', label: 'Management access', fields: [{ key: 'terminalNumber', label: 'Terminal wire ID', editor: 'text',
      value: '2025550100', min: 0, max: 10, maxLength: 10, choices: null, help: null }] }],
  } : { supported: false, reason: 'Create site-specific access', managementAccessTemplates: [later] }))
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ error: 'Fixture conflict' }, { status: 409 }))
    .mockImplementationOnce(async () => { created = true; return new Response(null, { status: 204 }) })
  render(<NativeTableEditor setId="fixture" number={21} revision={0} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={saved} onDirty={dirty} />)
  const submit = await screen.findByRole('button', { name: 'Create MT21 record' })
  expect(submit).toBeDisabled()
  expect(screen.getByLabelText('Terminal wire ID (ten digits)')).toHaveValue('')
  expect(screen.getByLabelText('Primary manager number')).toHaveValue('')
  fireEvent.change(screen.getByLabelText('Terminal wire ID (ten digits)'), { target: { value: '2025550100' } })
  fireEvent.change(screen.getByLabelText('Primary manager number'), { target: { value: '2025550101' } })
  fireEvent.change(screen.getByLabelText('ADSI CAD identifier (optional)'), { target: { value: 'bad' } })
  expect(submit).toBeDisabled()
  fireEvent.change(screen.getByLabelText('ADSI CAD identifier (optional)'), { target: { value: '01020304' } })
  expect(submit).toBeEnabled()
  expect(dirty).toHaveBeenLastCalledWith(true)
  fireEvent.click(submit)
  expect(await screen.findByText('Fixture conflict')).toBeVisible()
  expect(screen.getByLabelText('Primary manager number')).toHaveValue('2025550101')
  fireEvent.click(submit)
  expect(await screen.findByLabelText('Terminal wire ID')).toHaveValue('2025550100')
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/fixture/records/21/fields', {
    profile: later.profile, length: 48, managementAccess: { terminalNumber: '2025550100', primary: '2025550101', alternate: '', cadId: '01020304', cpeId: '' },
  })
  await waitFor(() => expect(saved).toHaveBeenCalledTimes(1))
  expect(dirty).toHaveBeenLastCalledWith(false)
})

it('requires an explicit layout for an unbound set and avoids sending later fields to the shorter format', () => {
  const create = vi.fn()
  render(<ManagementAccessDraftForm templates={[earlier, later]} busy={false} onCreate={create} onDirty={vi.fn()} />)
  expect(screen.getByRole('button', { name: 'Create MT21 record' })).toBeDisabled()
  expect(screen.queryByLabelText('Terminal wire ID (ten digits)')).toBeNull()
  fireEvent.change(screen.getByLabelText('Management-access layout'), { target: { value: '18' } })
  expect(screen.queryByLabelText('ADSI CAD identifier (optional)')).toBeNull()
  fireEvent.change(screen.getByLabelText('Terminal wire ID (ten digits)'), { target: { value: '2025550100' } })
  fireEvent.change(screen.getByLabelText('Primary manager number'), { target: { value: '1234567890123' } })
  expect(screen.getByRole('button', { name: 'Create MT21 record' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Primary manager number'), { target: { value: '2025550101' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create MT21 record' }))
  expect(create).toHaveBeenCalledWith({ profile: earlier.profile, length: 18,
    managementAccess: { terminalNumber: '2025550100', primary: '2025550101', alternate: '' } })
})

it('does not offer authoring to a read-only operator even when a known template exists', async () => {
  consoleSession.save('fixture-token', '', 'Fixture reader', 'Operator', { 'table-records': 1 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ supported: false, reason: 'Create site-specific access', managementAccessTemplates: [earlier] }))
  render(<NativeTableEditor setId="fixture" number={21} revision={0} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
  expect(await screen.findByText('Create site-specific access')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create MT21 record' })).toBeNull()
})
