import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { ScreeningSlotReferencesPanel } from './ScreeningSlotReferencesPanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('shows saved configured, conditional, retained and fallback references and missing records without writing edits', async () => {
  vi.mocked(apiFetch).mockResolvedValue(new Response(JSON.stringify({ slot: 1, setRevision: 5, profile: 'Later profile', review: {
    supported: true, capacity: 180, references: [
      { recordNumber: 26, label: 'Operator route', use: 'configured', savedEntry: 1 },
      { recordNumber: 26, label: 'Alternate route', use: 'retained', savedEntry: 1 },
      { recordNumber: 72, label: 'IOPR access', use: 'conditional', savedEntry: 1 },
      { recordNumber: 134, label: 'Stored-value range 1', use: 'fallback', savedEntry: 255 },
    ], reviewedRecords: [26, 72, 92, 134], missingRecords: [135], unreviewedRecords: [] } }), { status: 200 }))
  render(<MemoryRouter><ScreeningSlotReferencesPanel setId="fixture-set" slot={1} dirty /></MemoryRouter>)
  expect(screen.getByRole('note')).toHaveTextContent('Unsaved edits')
  fireEvent.click(screen.getByRole('button', { name: 'Review screening references' }))
  const result = await screen.findByRole('region', { name: 'Saved screening reference review' })
  expect(result).toHaveTextContent('Configured reference')
  expect(result).toHaveTextContent('Retained, not enabled')
  expect(result).toHaveTextContent('Conditional service reference')
  expect(result).toHaveTextContent('Fallback to entry 1 (saved entry 255)')
  expect(result).toHaveTextContent('Not present here: MT135')
  expect(within(result).getByRole('link', { name: 'MT134' })).toHaveAttribute('href', '/table-sets/fixture-set?record=134')
  expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/fixture-set/screening-slots/1/references', expect.objectContaining({ signal: expect.any(AbortSignal) }))
  expect(apiFetch).toHaveBeenCalledTimes(1)
})

it('keeps unsupported layouts and failed requests distinct from unused entries', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response(JSON.stringify({ review: { supported: false, reason: 'Unverified layout' } }), { status: 200 }))
    .mockResolvedValueOnce(new Response('', { status: 403 }))
  render(<MemoryRouter><ScreeningSlotReferencesPanel setId="fixture-set" slot={180} dirty={false} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review screening references' }))
  expect(await screen.findByText('Unverified layout')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Refresh screening references' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('HTTP 403')
  expect(screen.queryByText(/No recognized references/)).toBeNull()
})
