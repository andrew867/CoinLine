import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TableSets } from './TableSets'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn() }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

it('creates a table set using only the narrow firmware lookup with Add permission', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 4 })
  vi.mocked(apiGet).mockResolvedValue([{ id: 'firmware-fixture', label: 'Fixture firmware', buildId: null }])
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ id: 'set-fixture' }, { status: 201 }))
  render(<MemoryRouter><TableSets /></MemoryRouter>)
  expect(await screen.findByRole('option', { name: 'Fixture firmware' })).toBeVisible()
  expect(screen.queryByText('Loading…')).toBeNull()
  expect(apiGet).toHaveBeenCalledWith('/api/tables/firmware-options')
  expect(apiFetch).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('New set name'), { target: { value: 'Fixture set' } })
  fireEvent.change(screen.getByLabelText('Target firmware'), { target: { value: 'firmware-fixture' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create table set' }))
  expect(await screen.findByText('Created table set Fixture set.')).toBeVisible()
  expect(apiFetch).toHaveBeenCalledTimes(1)
  expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets', expect.objectContaining({ method: 'POST',
    body: JSON.stringify({ name: 'Fixture set', customerId: null, isDefault: false, targetFirmwareVersionId: 'firmware-fixture' }) }))
})

it('lists sets without authoring actions or firmware lookups with Read permission', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 1 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json([{
    id: 'set-fixture', name: 'Fixture set', customerId: null, isDefault: false, status: 0,
    publishedAtUtc: null, publishGeneration: 0, targetFirmware: null,
  }]))
  render(<MemoryRouter><TableSets /></MemoryRouter>)
  expect(await screen.findByRole('link', { name: 'Fixture set' })).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create table set' })).toBeNull()
  expect(apiGet).not.toHaveBeenCalled()
  expect(apiFetch).toHaveBeenCalledTimes(1)
})
