import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TableDefinitions } from './TableDefinitions'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const profile = (profileId: string, label: string, behaviorObservation: string | null, observedLengthsOnly = false) => ({
  profileId, label, length: 3061, terminalAcknowledged: true, completeCatalogue: !observedLengthsOnly,
  observedLengthsOnly, editorStatus: observedLengthsOnly ? 'length-only' : 'template', editorProfile: observedLengthsOnly ? null : 'Fixture', behaviorObservation,
})

it('shows only matching behavior evidence and counts observations independently of acknowledgement', async () => {
  const catalogue = [{ number: 92, name: 'Call screening', kind: 'native_editor', layouts: [], registeredDefinitions: [],
    behaviorObservation: 'Unscoped screening observation', firmware: [profile('earlier', 'Earlier profile', 'Earlier free screening observed.'),
      profile('later', 'Graphical profile', 'Graphical paid and free controls observed.'), profile('observed', 'Observed-only variant', null, true)] },
  { number: 133, name: 'Graphical screen 3', kind: 'native_editor', layouts: [], registeredDefinitions: [], behaviorObservation: null,
    firmware: [profile('later', 'Graphical profile', null)] }]
  vi.mocked(apiGet).mockImplementation(async url => (url === '/api/tables/reference-records' ? catalogue : []) as never)
  render(<MemoryRouter><TableDefinitions /></MemoryRouter>)
  const selector = await screen.findByLabelText('Target profile')
  expect(await screen.findByText('2 profile-specific observation(s)')).toBeVisible()
  expect(screen.getByRole('columnheader', { name: 'Packed editor' })).toBeVisible()
  expect(screen.getByRole('columnheader', { name: 'Wire layout evidence' })).toBeVisible()
  expect(screen.getByRole('columnheader', { name: 'Terminal acknowledgement' })).toBeVisible()
  expect(screen.getByRole('columnheader', { name: 'Observed behavior' })).toBeVisible()
  fireEvent.change(selector, { target: { value: 'earlier' } })
  expect(screen.getByText('Earlier free screening observed.')).toBeVisible()
  expect(screen.queryByText('Graphical paid and free controls observed.')).toBeNull()
  expect(within(screen.getByLabelText('Selected profile coverage')).getByText(/emulator acknowledgements: 1; recorded behavior observations: 1/)).toBeVisible()
  fireEvent.change(selector, { target: { value: 'observed' } })
  expect(screen.getByText('No behavior observation recorded for this target profile.')).toBeVisible()
  expect(screen.getByText('Other-profile or unscoped reference observations are not evidence for this target.')).toBeVisible()
  expect(screen.queryByText('Earlier free screening observed.')).toBeNull()
  expect(screen.queryByText('Unscoped screening observation')).toBeNull()
  expect(within(screen.getByLabelText('Selected profile coverage')).getByText(/emulator acknowledgements: 1; recorded behavior observations: 0/)).toBeVisible()
  fireEvent.change(selector, { target: { value: 'later' } })
  expect(screen.getByText('Graphical paid and free controls observed.')).toBeVisible()
  expect(screen.getByText('No behavior observation recorded for this target profile.')).toBeVisible()
  expect(within(screen.getByLabelText('Selected profile coverage')).getByText(/emulator acknowledgements: 2; recorded behavior observations: 1/)).toBeVisible()
})
