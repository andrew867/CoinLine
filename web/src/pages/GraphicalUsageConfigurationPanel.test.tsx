import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { GraphicalUsageConfigurationPanel, previewGraphicalUsage, type GraphicalUsageConfiguration } from './GraphicalUsageConfigurationPanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const usage: GraphicalUsageConfiguration = { collectionDurationDays: 4, screens: [
  { recordNumber: 131, screenNumber: 1, kindCode: 66, screenOwnerCode: 123, keyOwnerCodes: [456, 0, 0, 0, 0, 0] },
  { recordNumber: 131, screenNumber: 2, kindCode: 0, screenOwnerCode: 999, keyOwnerCodes: [0, 0, 0, 0, 0, 0] },
  { recordNumber: 132, screenNumber: 9, kindCode: 84, screenOwnerCode: 0, keyOwnerCodes: [25, 0, 0, 0, 0, 0] },
  { recordNumber: 133, screenNumber: 21, kindCode: 77, screenOwnerCode: 1024, keyOwnerCodes: [2048, 0, 0, 0, 0, 0] },
] }
const included = [131, 132, 133].map(number => ({ number, availability: 'included' }))

it('uses the main mixed-screen owners and does not take a saved companion header as the reporting duration', () => {
  const projected = previewGraphicalUsage(usage, 133, {
    statDuration: '99', 'screen-4.panel-0.owner': '3000', 'screen-4.panel-0.button-0.owner': '4000', 'button-24.owner': '42',
  })
  expect(projected.duration).toBe(4)
  expect(projected.durationStaged).toBe(false)
  expect(projected.screens[3]).toMatchObject({ screenOwnerCode: 1024, keyOwnerCodes: [42, 0, 0, 0, 0, 0], staged: true })
  expect(projected.screens[0]).toMatchObject({ screenOwnerCode: 123, staged: false })
  expect(previewGraphicalUsage(usage, 131, { statDuration: '65536', 'screen-0.owner': '-1' })).toMatchObject({ duration: null, durationStaged: true })
  expect(previewGraphicalUsage(usage, 131, { 'screen-0.owner': '-1' }).screens[0].screenOwnerCode).toBeNull()
})

it('distinguishes an unavailable MT131 from disabled collection and does not count undefined or ownerless screens', () => {
  const open = vi.fn()
  render(<GraphicalUsageConfigurationPanel usage={{ ...usage, collectionDurationDays: null, screens: usage.screens.slice(2) }}
    records={[{ number: 131, availability: 'missing' }, ...included.slice(1)]} number={133} edits={{ statDuration: '1' }} onOpenSection={open} />)
  screen.getByText('Review graphical statistics configuration').closest('details')!.open = true
  const panel = screen.getByRole('region', { name: 'Graphical statistics configuration' })
  expect(panel).toHaveTextContent('Unavailable in this set')
  expect(within(panel).getByRole('status')).toHaveTextContent('Collection eligibility cannot be established')
  expect(panel).toHaveTextContent('No screen owner code')
  expect(panel).toHaveTextContent('Collection period unavailable')
  expect(panel).toHaveTextContent('MT131 (missing)')
  fireEvent.click(within(panel).getByRole('button', { name: 'Open Screen 21 settings' }))
  expect(open).toHaveBeenCalledWith('screen-4')
})

it('updates cross-record coverage from staged editor values and saves only the intended current record fields', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const field = (key: string, label: string, value: string) => ({ key, label, value, editor: 'number', min: 0, max: 65535,
    maxLength: null, choices: null, help: null })
  const sections = [
    { key: 'rotation', label: 'Graphical screens and rotation', fields: [field('statDuration', 'Statistics collection duration (days)', '4')] },
    { key: 'screen-0', label: 'Screen 1', graphicalScreenIndex: 0, fields: [field('screen-0.owner', 'Screen owner code', '123')] },
    { key: 'button-0', label: 'Screen 1, key 1', graphicalScreenIndex: 0, fields: [field('button-0.owner', 'Key owner code', '456')] },
  ]
  const saves: Record<string, string>[] = []
  vi.mocked(apiFetch).mockImplementation(async (_, init) => {
    if (init?.method === 'PUT') { saves.push(JSON.parse(init.body as string).changes); return new Response(null, { status: 204 }) }
    return Response.json({ supported: true, editable: true, profile: 'Later packed profile', length: 9224, revision: 1,
      sha256: 'fixture-sha', sections, graphicalLinks: { records: included, references: [], usage } })
  })
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={131} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
  const details = (await screen.findByText('Review graphical statistics configuration')).closest('details')!
  details.open = true
  const panel = screen.getByRole('region', { name: 'Graphical statistics configuration' })
  expect(within(panel).getByRole('status')).toHaveTextContent('2 screens configured for collection across 3 included records.')
  expect(panel).not.toHaveTextContent('Screen 2MT131')
  fireEvent.change(within(panel).getByLabelText('Screen coverage'), { target: { value: 'all' } })
  expect(within(panel).getByText('Undefined screen')).toBeVisible()
  fireEvent.click(within(panel).getByRole('button', { name: 'Open Screen 1 settings' }))
  fireEvent.change(screen.getByLabelText('Screen owner code'), { target: { value: '0' } })
  expect(within(panel).getByRole('status')).toHaveTextContent('1 screen configured for collection')
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'rotation' } })
  fireEvent.change(screen.getByLabelText('Statistics collection duration (days)'), { target: { value: '0' } })
  expect(panel).toHaveTextContent('Disabled (zero days)')
  expect(within(panel).getByRole('status')).toHaveTextContent('0 screens configured for collection')
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(saves).toEqual([{ 'screen-0.owner': '0', statDuration: '0' }]))
})
