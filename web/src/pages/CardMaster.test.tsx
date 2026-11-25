import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { CardMasterPage } from './CardMasterPage'
import type { CardMasterField } from './cardMasterApi'
import { CardMasterConfigurationPanel } from './CardMasterConfigurationPanel'
import { CardMasterRolloutPanel } from './CardMasterRolloutPanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

const entry = { id: 'card-fixture', code: 'LAB', name: 'Fixture card', tableNumber: 22, revision: 2, archived: false,
  standard: '1', low: '000001', high: '000003' }
const field = (key: string, label: string, value: string, extra: Partial<CardMasterField> = {}): CardMasterField =>
  ({ key, label, value, editor: 'text', min: 0, max: 255, maxLength: 6, choices: null, help: null, targetMapping: false, ...extra })
const sections = [{ key: 'card-0', label: 'Card range 1', fields: [
  field('card-0.format', 'Card number standard', '1', { editor: 'card-standard', choices: [{ value: 1, label: 'Mod 10' }, { value: 10, label: 'Stored-value' }] }),
  field('card-0.start', 'Lowest six-digit prefix', '000001'), field('card-0.end', 'Highest six-digit prefix', '000003'),
  field('card-0.carrier', 'Carrier reference', '7', { editor: 'number', targetMapping: true }),
] }]

it('creates a named master definition separately from card accounts and shows its assigned usage', async () => {
  consoleSession.save('fixture', '', undefined, 'Admin')
  vi.mocked(apiFetch).mockImplementation(async (url, init) => {
    if (init?.method === 'POST') return Response.json({ entry, sections }, { status: 201 })
    if (String(url).endsWith('/usage')) return Response.json({ assignedPhones: 2, outdatedAssignedPhones: 1,
      configurations: [{ setId: 'set-one', name: 'Fixture configuration', status: 'Published', assignedPhones: 2,
        positions: [{ position: 3, appliedRevision: 1, outdated: true }] }] })
    if (String(url).includes('/schema')) return Response.json({ sections })
    if (String(url).endsWith('/card-fixture')) return Response.json({ entry, sections })
    return Response.json([entry])
  })
  render(<MemoryRouter><CardMasterPage /></MemoryRouter>)
  fireEvent.change(await screen.findByLabelText('Card type ID'), { target: { value: 'LAB' } })
  fireEvent.change(screen.getByLabelText('Card name'), { target: { value: 'Fixture card' } })
  fireEvent.change(screen.getByLabelText('Lowest six-digit prefix'), { target: { value: '000002' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save card definition' }))
  await waitFor(() => expect(apiFetch).toHaveBeenCalledWith('/api/card-master', expect.objectContaining({ method: 'POST' })))
  const call = vi.mocked(apiFetch).mock.calls.find(([url, init]) => url === '/api/card-master' && init?.method === 'POST')
  expect(JSON.parse(String(call?.[1]?.body))).toEqual({ code: 'LAB', name: 'Fixture card', tableNumber: 22,
    changes: { 'card-0.start': '000002', 'card-0.format': '1' } })
  expect(await screen.findByText(/Assigned phones: 2. Assigned phones with an older definition revision: 1/)).toBeVisible()
  expect(screen.getByRole('link', { name: 'Fixture configuration' })).toHaveAttribute('href', '/table-sets/set-one')
  expect(screen.getByText('3: revision 1 (older)')).toBeVisible()
})

function panelProps() {
  return { endpoint: '/api/tables/sets/set-one/records/22/card-sequence', setId: 'set-one', number: 22,
    cards: { revision: 2, sha256: 'cards' }, carriers: { revision: 3, sha256: 'carriers' },
    slots: [{ number: 1, blank: false, movable: true, master: { definitionId: entry.id, revision: 1, code: entry.code },
      mapping: { 'card-0.carrier': '8' }, acceptedCarriers: [8] }, { number: 2, blank: true, movable: false }],
    carrierReferences: [8, 9], labels: { 1: '000001 to 000003' }, editable: true, busy: false, dirty: false,
    onBusyChange: vi.fn(), onCompleted: vi.fn() }
}
function mockPanel() {
  vi.mocked(apiFetch).mockImplementation(async (url, init) => {
    if (init?.method === 'POST') return Response.json({ position: String(url).endsWith('/add') ? 2 : 1, carrierChanged: true })
    if (String(url).endsWith('/card-fixture')) return Response.json({ entry, sections })
    return Response.json([entry])
  })
}

it('requires destination mapping and acceptance review before adding at the first blank position', async () => {
  consoleSession.save('fixture', '', undefined, 'Admin'); mockPanel()
  const props = panelProps()
  render(<MemoryRouter><CardMasterConfigurationPanel {...props} /></MemoryRouter>)
  expect(screen.getByText('The next blank position is 2.')).toBeVisible()
  await screen.findByRole('option', { name: 'LAB · Fixture card · revision 2' })
  fireEvent.change(screen.getByLabelText('Master card definition'), { target: { value: entry.id } })
  const primary = await screen.findByLabelText('Primary carrier reference')
  expect(primary).toHaveValue('7')
  const button = screen.getByRole('button', { name: 'Add master card' })
  expect(button).toBeDisabled()
  fireEvent.change(primary, { target: { value: '8' } })
  fireEvent.click(screen.getByLabelText('Carrier 8'))
  expect(button).toBeDisabled()
  fireEvent.click(screen.getByLabelText('I have reviewed the references against the destination terminal configuration.'))
  expect(button).toBeEnabled(); fireEvent.click(button)
  await waitFor(() => expect(props.onCompleted).toHaveBeenCalledWith('add', 2, true))
  const request = vi.mocked(apiFetch).mock.calls.find(([url, init]) => String(url).endsWith('/add') && init?.method === 'POST')
  expect(JSON.parse(String(request?.[1]?.body))).toEqual({ cards: props.cards, carriers: props.carriers, definitionId: entry.id,
    definitionRevision: 2, mapping: { 'card-0.carrier': '8' }, acceptedCarriers: [8], confirm: false })
})

it('refreshes the linked position using its existing mapping and removes a position independently of its master', async () => {
  consoleSession.save('fixture', '', undefined, 'Admin'); mockPanel()
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  const props = panelProps()
  render(<MemoryRouter><CardMasterConfigurationPanel {...props} /></MemoryRouter>)
  fireEvent.change(screen.getByLabelText('Configured card position'), { target: { value: '1' } })
  fireEvent.change(screen.getByLabelText('Master list action'), { target: { value: 'refresh' } })
  await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh linked card' })).toBeEnabled())
  fireEvent.click(screen.getByRole('button', { name: 'Refresh linked card' }))
  await waitFor(() => expect(props.onCompleted).toHaveBeenCalledWith('refresh', 1, true))
  const refresh = vi.mocked(apiFetch).mock.calls.find(([url, init]) => String(url).endsWith('/refresh') && init?.method === 'POST')
  expect(JSON.parse(String(refresh?.[1]?.body))).toEqual({ cards: props.cards, carriers: props.carriers, definitionRevision: 2, position: 1, confirm: false })
  fireEvent.click(screen.getByRole('button', { name: 'Remove card from configuration' }))
  await waitFor(() => expect(props.onCompleted).toHaveBeenCalledWith('remove', 1, true))
  expect(vi.mocked(apiFetch).mock.calls.some(([url, init]) => String(url).includes('/api/card-master/') && init?.method === 'DELETE')).toBe(false)
})

it('previews affected phones, skips ineligible work and confirms the exact rollout token', async () => {
  consoleSession.save('fixture', '', undefined, 'Admin')
  const applied = vi.fn()
  vi.mocked(apiFetch).mockImplementation(async url => String(url).endsWith('/preview')
    ? Response.json({ previewToken: 'review-token', eligiblePhones: 2, scheduledPhones: 1, metadataOnlyPhones: 1, skippedPhones: 1,
      configurations: [{ setId: 'set-one', name: 'Fixture configuration', positions: [2, 9], records: [22], phones: 1, reason: null },
        { setId: 'set-two', name: 'Other fixture', positions: [1], records: [], phones: 1, reason: null },
        { setId: 'set-three', name: 'Conflicting fixture', positions: [3], records: [22], phones: 1, reason: 'Phone already has a pending or active download.' }] })
    : Response.json({ affectedPhones: 2, scheduledPhones: 1, metadataOnlyPhones: 1, skippedPhones: 1,
      sets: [{ id: 'new-set', name: 'Updated fixture' }] }))
  render(<MemoryRouter><CardMasterRolloutPanel entry={entry} dirty={false} onApplied={applied} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Preview affected phones' }))
  expect(await screen.findByText('Eligible phones: 2. Downloads to prepare: 1. Host metadata only: 1. Skipped: 1.')).toBeVisible()
  expect(screen.getByText('Phone already has a pending or active download.')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Confirm update and prepare downloads' }))
  expect(await screen.findByText('Updated 2 phone assignments and prepared 1 downloads. 1 skipped.')).toBeVisible()
  expect(applied).toHaveBeenCalledOnce()
  const request = vi.mocked(apiFetch).mock.calls.find(([url]) => String(url).endsWith('/rollout'))
  expect(JSON.parse(String(request?.[1]?.body))).toEqual({ revision: 2, previewToken: 'review-token' })
})
