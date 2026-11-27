import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { FieldEventReference } from './FieldEventReference'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const fixture = {
  alarms: [{ code: 22, description: 'Memory lost', operatorDescription: null, severity: null, revision: 0 },
    { code: 31, description: 'Coin jam', operatorDescription: null, severity: null, revision: 0 }],
  maintenance: [{ code: 996, description: 'Technician entered maintenance mode', operatorDescription: null, severity: null, revision: 0 }],
  canEdit: false,
}
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status })
const success = () => json(fixture)

it('loads the canonical reference on demand and searches names and numbers', async () => {
  vi.mocked(apiFetch).mockResolvedValue(success())
  render(<FieldEventReference />)
  expect(apiFetch).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Show reference' }))
  expect(await screen.findByText('Memory lost')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Configure code' })).toBeNull()
  expect(apiFetch).toHaveBeenCalledWith('/api/field-events/reference', expect.objectContaining({ signal: expect.any(AbortSignal) }))
  expect(within(screen.getByRole('region', { name: 'Maintenance opcodes' })).getByText('996')).toBeVisible()
  fireEvent.change(screen.getByRole('searchbox'), { target: { value: '22' } })
  expect(screen.getByText('Memory lost')).toBeVisible()
  expect(screen.queryByText('Coin jam')).toBeNull()
  fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'TECHNICIAN' } })
  expect(screen.queryByText('Memory lost')).toBeNull()
  expect(screen.getByText('Technician entered maintenance mode')).toBeVisible()
  fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'unlisted' } })
  expect(screen.getAllByText('No matching codes.')).toHaveLength(2)
})

it('edits severity and description without replacing the original label', async () => {
  const row = { ...fixture.alarms[0], operatorDescription: 'Inspect backup supply', severity: 2, revision: 1 }
  vi.mocked(apiFetch).mockResolvedValueOnce(json({ ...fixture, canEdit: true })).mockResolvedValueOnce(json(row))
  render(<FieldEventReference />)
  fireEvent.click(screen.getByRole('button', { name: 'Show reference' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Edit alarm 22' }))
  fireEvent.change(screen.getByLabelText('Operator description'), { target: { value: row.operatorDescription } })
  fireEvent.change(screen.getByLabelText('Severity'), { target: { value: '2' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save classification' }))
  expect(await screen.findByText('Saved alarm code 22.')).toBeVisible()
  expect(screen.getByText('Memory lost')).toBeVisible()
  expect(screen.getByText('Inspect backup supply')).toBeVisible()
  expect(screen.getByText('Major')).toBeVisible()
  const call = vi.mocked(apiFetch).mock.calls[1]
  expect(call[0]).toBe('/api/field-events/reference/alarm/22')
  expect(call[1]?.method).toBe('PUT')
  expect(JSON.parse(String(call[1]?.body))).toEqual({ operatorDescription: 'Inspect backup supply', severity: 2, revision: 0 })
})

it('retains an unsaved classification after conflict and allows reload', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(json({ ...fixture, canEdit: true }))
    .mockResolvedValueOnce(json({ error: 'This code was changed by another operator.' }, 409))
    .mockResolvedValueOnce(json({ ...fixture, canEdit: true }))
  render(<FieldEventReference />)
  fireEvent.click(screen.getByRole('button', { name: 'Show reference' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Edit alarm 22' }))
  fireEvent.change(screen.getByLabelText('Operator description'), { target: { value: 'Unsaved' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save classification' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('changed by another operator')
  expect(screen.getByLabelText('Operator description')).toHaveValue('Unsaved')
  fireEvent.click(screen.getByRole('button', { name: 'Reload catalogue' }))
  expect(screen.queryByRole('region', { name: 'Configure event code' })).toBeNull()
})

it('shows a failed request instead of an empty reference, and retries', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response('', { status: 500 })).mockResolvedValueOnce(success())
  render(<FieldEventReference />)
  fireEvent.click(screen.getByRole('button', { name: 'Show reference' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load the reference.')
  expect(screen.queryByText('No matching codes.')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
  expect(await screen.findByText('Memory lost')).toBeVisible()
  expect(screen.queryByRole('alert')).toBeNull()
})

it('imports only missing example maintenance descriptions and preserves configured choices', async () => {
  const examples = [{ code: 15, description: 'Contractor cleaned booth', configured: true }, { code: 106, description: 'Booth cleaned', configured: false }]
  const existing = { code: 15, description: null, operatorDescription: 'Custom cleaning', severity: null, revision: 2 }
  const before = { ...fixture, canEdit: true, maintenanceExamples: examples, maintenance: [...fixture.maintenance, existing] }
  const after = { ...before, maintenanceExamples: examples.map(row => ({ ...row, configured: true })), maintenance: [...before.maintenance,
    { code: 106, description: null, operatorDescription: 'Booth cleaned', severity: null, revision: 1 }] }
  vi.mocked(apiFetch).mockResolvedValueOnce(json(before)).mockResolvedValueOnce(json({ added: 1, retained: 1 })).mockResolvedValueOnce(json(after))
  render(<FieldEventReference />)
  fireEvent.click(screen.getByRole('button', { name: 'Show reference' }))
  const region = await screen.findByRole('region', { name: 'Historical maintenance examples' })
  fireEvent.click(within(region).getByText('Review 2 example codes'))
  expect(within(region).getByText('Existing setting retained')).toBeVisible()
  expect(within(region).getByText('Not imported')).toBeVisible()
  fireEvent.click(within(region).getByRole('button', { name: 'Import 1 missing example descriptions' }))
  expect(await screen.findByText('Imported 1 example descriptions; 1 existing settings retained.')).toBeVisible()
  expect(apiFetch).toHaveBeenCalledWith('/api/field-events/reference/maintenance/examples', { method: 'POST' })
  const catalogue = screen.getByRole('region', { name: 'Maintenance opcodes' })
  expect(await within(catalogue).findByText('Booth cleaned')).toBeVisible()
  expect(within(catalogue).getByText('Custom cleaning')).toBeVisible()
  expect(within(region).getByRole('button', { name: 'Import 0 missing example descriptions' })).toBeDisabled()
})
