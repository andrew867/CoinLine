import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiGetRaw, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TerminalConfigurationWorksheet } from './TerminalConfigurationWorksheet'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiGetRaw: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })
const endpoint = '/api/terminals/fixture-phone/configuration-worksheet'
const assigned = (configurationId: string, number: number) => ({ id: `base-${number}`, setId: 'base', setName: 'Current configuration',
  configurationId, revision: 1, length: 100, sha256: 'a'.repeat(64), validationPassed: true })
const option = (number: number, configurationId: string, issue: string | null = null) => ({ id: `source-${number}`, tableSetId: `set-${number}`,
  setName: `Published MT${number}`, configurationId, revision: 4, length: 100, sha256: 'b'.repeat(64), targetFirmware: 'Fixture target', issue })
const records = [
  { number: 21, name: 'Access', family: 'Dialing', familyRequired: true, canChoose: true, canClear: false, ambiguous: false, assigned: null },
  { number: 29, name: 'Advertising', family: 'Display', familyRequired: false, canChoose: true, canClear: true, ambiguous: false, assigned: assigned('ADV001', 29) },
  { number: 50, name: 'Coin validation', family: 'Coins', familyRequired: true, canChoose: true, canClear: false, ambiguous: false, assigned: assigned('COIN01', 50) },
  { number: 32, name: 'Communications statistics', family: 'Reporting', familyRequired: false, canChoose: true, canClear: true, ambiguous: false, assigned: assigned('STAT01', 32) },
  { number: 31, name: 'Servicing', family: 'Service', familyRequired: true, canChoose: true, canClear: false, ambiguous: false, assigned: assigned('SERV01', 31) },
]
const family = { familyLabel: 'Fixture family', documented: true, tables: [{ name: 'Dialing', required: true, status: 'missing' }],
  operatingMode: { expectedCode: 2, expectedLabel: 'Multi-pay', configuredCode: 1, configuredLabel: 'Card only', status: 'differs',
    message: 'Review whether the payment-mode difference is intentional for this phone.' } }
const receipt = { tableSetId: 'result-set', tableSetName: 'Updated configuration', changedNumbers: [29, 50], removedNumbers: [32] }
function setup(canUpdate = true, initial = false, onApplied = vi.fn()) {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-assignments': canUpdate ? 3 : 1 })
  vi.mocked(apiGet).mockImplementation(async path => {
    const url = new URL(String(path), 'http://localhost')
    if (url.pathname === endpoint) return { id: 'fixture-phone', displayName: 'Phone', terminalIdHex: '0000000001', firmware: 'Fixture target',
      baseSetId: initial ? null : 'base', baseSetName: initial ? null : 'Current configuration', assignmentRevision: initial ? null : 7, canUpdate, family,
      records: initial ? records.map(row => ({ ...row, assigned: null, canClear: false })) : records } as never
    if (url.pathname === endpoint + '/initial-sources') return { available: true, total: 2, page: Number(url.searchParams.get('page')), pageSize: 50,
      sources: [{ id: 'starter', name: 'Preferred starter', recordCount: 3, isDefault: true, hasAccess: true },
        { id: 'wrong', name: 'Invalid starter', recordCount: 1, isDefault: false, hasAccess: false }] } as never
    if (url.pathname === endpoint + '/options') {
      const number = Number(url.searchParams.get('number')), page = Number(url.searchParams.get('page'))
      const choices = number === 29 ? [option(29, 'ADV002'), { ...option(29, 'WRONG1', 'Requires another firmware'), id: 'other' }]
        : [option(number, number === 21 ? 'ACC001' : 'COIN02')]
      return { records: choices, total: number === 29 ? 52 : 1, page, pageSize: 50 } as never
    }
    throw new Error(`Unexpected read ${path}`)
  })
  render(<MemoryRouter><TerminalConfigurationWorksheet terminalId="fixture-phone" assignmentKey="base" terminalActive onApplied={onApplied} /></MemoryRouter>)
  return onApplied
}

it('looks up records from two sources, stages an explicit clear, and handles stale preview confirmation', async () => {
  const onApplied = setup()
  expect(apiGet).not.toHaveBeenCalled()
  const requests: { path: string; body: Record<string, unknown> }[] = []
  let applyAttempts = 0
  vi.mocked(apiPostRaw).mockImplementation(async (path, body) => {
    requests.push({ path, body: body as Record<string, unknown> })
    if (path.endsWith('/preview')) return Response.json({ previewToken: 'fixture-preview', canApply: true, initialAssignment: false, family,
      changes: [{ tableNumber: 26, action: 'keep', required: true, personalized: false, current: assigned('FEAT01', 26), result: assigned('FEAT01', 26) },
        { tableNumber: 29, action: 'replace', required: false, personalized: false, current: records[1].assigned, result: option(29, 'ADV002') },
        { tableNumber: 32, action: 'remove', required: false, personalized: false, current: records[3].assigned, result: null },
        { tableNumber: 50, action: 'replace', required: false, personalized: false, current: records[2].assigned, result: option(50, 'COIN02') }] })
    if (++applyAttempts === 1) return Response.json({ error: 'The phone, assignment or selected records changed. Preview again.' }, { status: 409 })
    return Response.json(receipt)
  })
  fireEvent.click(screen.getByText('Individual configuration assignments'))
  const table = await screen.findByRole('table', { name: 'Assigned configuration records' })
  expect(within(table).getByText('ADV001 · revision 1')).toBeVisible()
  expect(screen.getByText('Multi-pay (code 2)')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Look up MT29' }))
  const lookup = await screen.findByRole('region', { name: 'Configuration lookup for MT29' })
  expect(await within(lookup).findByRole('button', { name: 'Choose ADV002' })).toBeEnabled()
  expect(within(lookup).getByRole('button', { name: 'Choose WRONG1' })).toBeDisabled()
  fireEvent.change(within(lookup).getByLabelText('Configuration ID or set name'), { target: { value: 'ADV002' } })
  fireEvent.click(within(lookup).getByRole('button', { name: 'Find configurations' }))
  await waitFor(() => expect(apiGet).toHaveBeenCalledWith(`${endpoint}/options?number=29&query=ADV002&page=1`))
  fireEvent.click(await within(lookup).findByRole('button', { name: 'Next choices' }))
  await waitFor(() => expect(apiGet).toHaveBeenCalledWith(`${endpoint}/options?number=29&query=ADV002&page=2`))
  fireEvent.click(await within(lookup).findByRole('button', { name: 'Choose ADV002' }))
  expect(within(table).getByText('Assign ADV002 · revision 4')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Look up MT50' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Choose COIN02' }))
  fireEvent.click(screen.getByRole('button', { name: 'Clear MT32' }))
  expect(within(table).getByText('Clear optional assignment')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Clear MT31' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Preview configuration assignment' }))
  const preview = await screen.findByRole('region', { name: 'Configuration assignment preview' })
  expect(within(preview).getByText('Required dependency')).toBeVisible()
  expect(within(preview).getByText('Card only (code 1)')).toBeVisible()
  expect(within(preview).getByRole('button', { name: 'Apply configuration assignment' })).toBeDisabled()
  fireEvent.click(within(preview).getByLabelText('I reviewed the resulting host assignment.'))
  fireEvent.click(within(preview).getByRole('button', { name: 'Apply configuration assignment' }))
  expect(await screen.findByText(/Preview again/)).toBeVisible()
  expect(screen.queryByRole('region', { name: 'Configuration assignment preview' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Preview configuration assignment' }))
  fireEvent.click(await screen.findByLabelText('I reviewed the resulting host assignment.'))
  fireEvent.click(screen.getByRole('button', { name: 'Apply configuration assignment' }))
  expect(await screen.findByText(/Host assignment updated to Updated configuration/)).toBeVisible()
  expect(onApplied).toHaveBeenCalledWith(receipt)
  expect(requests.at(-1)?.body).toEqual({ baseSetId: 'base', assignmentRevision: 7, personalizeAccessIdentity: false, previewToken: 'fixture-preview', choices: [
    { number: 29, action: 'copy', sourceSetId: 'set-29', sourceVersionId: 'source-29' }, { number: 32, action: 'clear' },
    { number: 50, action: 'copy', sourceSetId: 'set-50', sourceVersionId: 'source-50' },
  ] })
})

it('permits read-only lookup without linking to restricted configuration editors or enabling changes', async () => {
  setup(false)
  fireEvent.click(screen.getByText('Individual configuration assignments'))
  await screen.findByRole('table', { name: 'Assigned configuration records' })
  expect(screen.queryByRole('link')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Look up MT29' }))
  expect(await screen.findByText('ADV002 · revision 4')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Choose ADV002' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Clear MT32' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Preview configuration assignment' })).not.toBeInTheDocument()
  expect(apiPostRaw).not.toHaveBeenCalled()
})

it('starts an initial assignment and makes MT21 personalization an explicit choice', async () => {
  setup(true, true)
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ previewToken: 'fixture', canApply: true, initialAssignment: true, family,
    changes: [{ tableNumber: 21, action: 'add', required: false, personalized: true, current: null, result: option(21, 'ACC001') }] }))
  fireEvent.click(screen.getByText('Individual configuration assignments'))
  expect(await screen.findByText(/No configuration is assigned/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Look up MT21' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Choose ACC001' }))
  const identity = screen.getByLabelText(/Use this phone’s registered wire ID/)
  expect(identity).not.toBeChecked()
  fireEvent.click(identity)
  fireEvent.click(screen.getByRole('button', { name: 'Preview configuration assignment' }))
  expect(await screen.findByText('Registered wire ID applied')).toBeVisible()
  expect(apiPostRaw).toHaveBeenCalledWith(`${endpoint}/preview`, expect.objectContaining({ baseSetId: null, assignmentRevision: null, personalizeAccessIdentity: true }))
})

it('stages a whole matching starter, retains individual edits and requires explicit identity and assignment review', async () => {
  const onApplied = setup(true, true)
  const starterRecords = [21, 29, 50].map(number => ({ ...option(number, `START${number}`), number, tableSetId: 'starter', setName: 'Preferred starter' }))
  vi.mocked(apiGetRaw).mockImplementation(async path => path.endsWith('/wrong') ? Response.json({ error: 'MT29 has an incompatible length.' }, { status: 400 })
    : Response.json({ id: 'starter', name: 'Preferred starter', records: starterRecords, requiresAccessPersonalization: true, family }))
  vi.mocked(apiPostRaw).mockImplementation(async (path, body) => path.endsWith('/preview')
    ? Response.json({ previewToken: 'starter-preview', canApply: true, initialAssignment: true, family,
      changes: starterRecords.filter(record => (body as { choices: { number: number }[] }).choices.some(choice => choice.number === record.number))
        .map(record => ({ tableNumber: record.number, action: 'add', required: false, personalized: record.number === 21, current: null, result: record })) })
    : Response.json(receipt))
  fireEvent.click(screen.getByText('Individual configuration assignments'))
  fireEvent.click(await screen.findByRole('button', { name: 'Choose a matching starter' }))
  const chooser = await screen.findByRole('region', { name: 'Initial configuration starter' })
  const select = await within(chooser).findByLabelText('Published starter')
  fireEvent.change(select, { target: { value: 'wrong' } })
  expect(await within(chooser).findByText('MT29 has an incompatible length.')).toBeVisible()
  expect(within(chooser).queryByRole('button', { name: /Stage .*starter records/ })).not.toBeInTheDocument()
  fireEvent.change(select, { target: { value: 'starter' } })
  expect(await within(chooser).findByText(/MT21 record belongs to a different line/)).toBeVisible()
  expect(within(chooser).getByText(/Required families needing review/)).toBeVisible()
  fireEvent.click(within(chooser).getByRole('button', { name: 'Stage 3 starter records' }))
  expect(await screen.findByText('3 staged changes')).toBeVisible()
  expect(screen.getByRole('button', { name: 'Choose a matching starter' })).toBeDisabled()
  expect(apiPostRaw).not.toHaveBeenCalled()
  const identity = screen.getByLabelText(/Use this phone’s registered wire ID/)
  expect(identity).not.toBeChecked()
  fireEvent.click(identity)
  fireEvent.click(screen.getByRole('button', { name: 'Keep MT50' }))
  expect(screen.getByText('2 staged changes')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Preview configuration assignment' }))
  const preview = await screen.findByRole('region', { name: 'Configuration assignment preview' })
  expect(within(preview).getByRole('button', { name: 'Apply configuration assignment' })).toBeDisabled()
  fireEvent.click(within(preview).getByLabelText('I reviewed the resulting host assignment.'))
  fireEvent.click(within(preview).getByRole('button', { name: 'Apply configuration assignment' }))
  await waitFor(() => expect(onApplied).toHaveBeenCalledWith(receipt))
  expect(apiPostRaw).toHaveBeenLastCalledWith(endpoint, expect.objectContaining({ baseSetId: null, assignmentRevision: null,
    personalizeAccessIdentity: true, previewToken: 'starter-preview', choices: [
      { number: 21, action: 'copy', sourceSetId: 'starter', sourceVersionId: 'source-21' },
      { number: 29, action: 'copy', sourceSetId: 'starter', sourceVersionId: 'source-29' },
    ] }))
})

it('does not offer a starter to an assignment read-only operator', async () => {
  setup(false, true)
  fireEvent.click(screen.getByText('Individual configuration assignments'))
  await screen.findByText(/No configuration is assigned/)
  expect(screen.queryByRole('region', { name: 'Initial configuration starter' })).not.toBeInTheDocument()
  expect(apiGetRaw).not.toHaveBeenCalled()
})
