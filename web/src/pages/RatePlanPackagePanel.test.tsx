import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { RatePlanPackagePanel } from './RatePlanPackagePanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

const pack = { formatVersion: 1, name: 'Fixture regional scheme', mode: 1, rules: [{ pattern: '411' }],
  tariffs: [], destinationPrefixes: [], timeBands: [] }
const preview = { name: pack.name, rules: 1, tariffs: 0, destinationPrefixes: 0, timeBands: 0,
  explicitPaidServiceOverrides: 1, notice: 'Independent draft only.' }

it('loads the starter and requests an explicit preview before an independent import', async () => {
  vi.mocked(apiGet).mockResolvedValue(pack)
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(preview))
    .mockResolvedValueOnce(Response.json(preview)).mockResolvedValueOnce(Response.json({ id: 'new-plan' }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  const onImported = vi.fn()
  render(<MemoryRouter><RatePlanPackagePanel onImported={onImported} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: /Load Newfoundland/ }))
  expect(await screen.findByText('709 ten-digit: $0.25 for the whole call.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create imported rate plan draft' })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Preview rate plan package' }))
  expect(await screen.findByText(/1 explicitly priced service/)).toBeVisible()
  fireEvent.change(screen.getByLabelText('Imported plan name'), { target: { value: 'Second fixture' } })
  expect(screen.queryByRole('button', { name: 'Create imported rate plan draft' })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Preview rate plan package' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Create imported rate plan draft' }))
  await waitFor(() => expect(onImported).toHaveBeenCalledTimes(1))
  expect(screen.getByRole('link', { name: 'Open imported rate plan' })).toHaveAttribute('href', '/rate-plans/new-plan')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/rate-plans/packages/import', { package: pack, name: 'Second fixture' })
})

it('reports invalid package errors without creating a plan', async () => {
  vi.mocked(apiGet).mockResolvedValue(pack)
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ error: 'The package format is unsupported.' }, { status: 400 }))
  render(<MemoryRouter><RatePlanPackagePanel onImported={vi.fn()} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: /Load Newfoundland/ }))
  fireEvent.click(await screen.findByRole('button', { name: 'Preview rate plan package' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('The package format is unsupported.')
  expect(screen.queryByRole('button', { name: 'Create imported rate plan draft' })).toBeNull()
})

it('chooses an existing published version for explicit local free exceptions only', async () => {
  vi.mocked(apiGet).mockResolvedValue(pack)
  render(<MemoryRouter><RatePlanPackagePanel onImported={vi.fn()} sources={[
    { id: 'old', name: 'Existing published demo', publishedVersionId: 'v3' },
    { id: 'draft', name: 'Existing draft', publishedVersionId: null },
  ]} /></MemoryRouter>)
  expect(screen.queryByRole('option', { name: 'Existing draft' })).toBeNull()
  fireEvent.change(screen.getByLabelText('Retain existing exact local free routes'), { target: { value: 'old' } })
  fireEvent.click(screen.getByRole('button', { name: /Load Newfoundland/ }))
  expect(await screen.findByText(/Exact existing four-digit and 709 free-call exceptions/)).toBeVisible()
  expect(apiGet).toHaveBeenCalledWith('/api/rate-plans/packages/regional-starter?sourcePlanId=old&sourceVersionId=v3')
})
