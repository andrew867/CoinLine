import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiPostRaw, apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const source = 'source-fixture'
const endpoint = `/api/tables/sets/${source}/records/92/screening-bulk`
const revision = 3, sha256 = 'a'.repeat(64), profile = 'MTR 1.9 packed'
function layout(editable = true) { return { supported: true, editable, profile, length: 3401, revision, sha256,
  screening: { descriptions: { 2: 'Fixture entry' }, supportsSpeedDial: false }, sections: [0, 1].map(index => ({ key: `rule-${index}`,
    label: `Screening rule ${index + 1}`, fields: [{ key: `rule-${index}.pattern`, label: 'Number pattern', editor: 'text', value: index === 1 ? '3335550123' : '',
      min: 0, max: 0, maxLength: 16, choices: null, help: null }] })) } }
const targets = [
  { setId: 'draft', name: 'Vacant draft', status: 'update-draft', published: false, inUse: false },
  { setId: 'published', name: 'Published template', status: 'replacement-draft', published: true, inUse: true, assignedPhones: 4 },
]
const preview = (blocked: boolean) => ({ previewToken: blocked ? 'blocked' : 'ready', canApply: !blocked, source: 'Source configuration',
  sourcePattern: '3335550123', slot: 2, changedCount: 2, blockedCount: blocked ? 1 : 0, updatedDrafts: 1, replacementDrafts: 1,
  targets: blocked ? [...targets, { setId: 'occupied', name: 'Occupied target', status: 'blocked', reason: 'The destination slot is occupied.',
    published: false, inUse: false }] : targets })

function renderEditor(editable = true, permissions = { 'table-records': 3, 'table-sets': 7 }) {
  consoleSession.save('fixture', '', undefined, 'Operator', permissions)
  vi.mocked(apiFetch).mockImplementation(async () => Response.json(layout(editable)))
  const onSaved = vi.fn()
  render(<MemoryRouter><NativeTableEditor setId={source} number={92} revision={revision} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={onSaved} onDirty={vi.fn()} /></MemoryRouter>)
  return onSaved
}

it('previews the selected saved entry, requires every blocker resolved, and handles a stale confirmation', async () => {
  const onSaved = renderEditor()
  const calls: { path: string; body: Record<string, unknown> }[] = []
  let attempts = 0
  vi.mocked(apiPostRaw).mockImplementation(async (path, body) => {
    const data = body as Record<string, unknown>; calls.push({ path, body: data })
    if (path.endsWith('/preview')) return Response.json(preview(data.customerScope === 'all'))
    if (++attempts === 1) return Response.json({ error: 'The source, target configurations or their use changed. Preview again.' }, { status: 409 })
    return Response.json({ affectedCount: 2, configurations: [{ sourceSetId: 'draft', setId: 'draft', name: 'Vacant draft', revision: 4, replacement: false },
      { sourceSetId: 'published', setId: 'replacement', name: 'Replacement template', revision: 4, replacement: true }] })
  })
  fireEvent.change(await screen.findByLabelText('Section or entry'), { target: { value: 'rule-1' } })
  fireEvent.click(screen.getByText('Change screening slot 2 across configurations'))
  const button = screen.getByRole('button', { name: 'Preview bulk screening change' })
  fireEvent.change(screen.getByLabelText('Number pattern'), { target: { value: '3335550124' } })
  expect(button).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Number pattern'), { target: { value: '3335550123' } })
  expect(button).toBeEnabled()
  fireEvent.change(screen.getByLabelText('Ownership scope'), { target: { value: 'all' } })
  fireEvent.click(button)
  expect(await screen.findByText('The destination slot is occupied.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Apply bulk screening change' })).not.toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Ownership scope'), { target: { value: 'source' } })
  fireEvent.click(button)
  const apply = await screen.findByRole('button', { name: 'Apply bulk screening change' })
  expect(apply).toBeDisabled()
  fireEvent.click(screen.getByLabelText('I reviewed all 2 changes.'))
  fireEvent.click(apply)
  expect(await screen.findByText(/Preview again/)).toBeVisible()
  expect(screen.queryByRole('region', { name: 'Bulk screening preview' })).not.toBeInTheDocument()
  fireEvent.click(button)
  fireEvent.click(await screen.findByLabelText('I reviewed all 2 changes.'))
  fireEvent.click(screen.getByRole('button', { name: 'Apply bulk screening change' }))
  const result = await screen.findByRole('region', { name: 'Bulk screening result' })
  expect(within(result).getByText(/No phones or downloads were changed/)).toBeVisible()
  expect(within(result).getByRole('link', { name: 'Replacement template' })).toHaveAttribute('href', '/table-sets/replacement?record=92')
  expect(calls.filter(row => row.path === endpoint)).toHaveLength(2)
  expect(calls.at(-1)?.body).toEqual({ revision, sha256, profile, slot: 2, action: 'add', includePublished: true, customerScope: 'source', previewToken: 'ready' })
  expect(onSaved).not.toHaveBeenCalled()
})

it('opens the bulk panel on a published source without enabling its individual field editor', async () => {
  renderEditor(false)
  await screen.findByLabelText('Number pattern')
  expect(screen.getByLabelText('Number pattern')).toBeDisabled()
  fireEvent.click(screen.getByText('Change screening slot 1 across configurations'))
  expect(screen.getByRole('button', { name: 'Preview bulk screening change' })).toBeEnabled()
})

it('does not offer a mutation panel to a read-only operator', async () => {
  renderEditor(false, { 'table-records': 1, 'table-sets': 1 })
  await screen.findByLabelText('Number pattern')
  expect(screen.queryByText(/Change screening slot/)).not.toBeInTheDocument()
})
