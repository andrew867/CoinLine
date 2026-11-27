import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiPostRaw } from '../api/client'
import { FirmwareQuickAccessStagingPanel } from './FirmwareQuickAccessStagingPanel'

vi.mock('../api/client', () => ({ apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks() })

const preview = { recordCount: 4, canCreate: false, sourceActiveSlots: [6,7,8,9,10], targetActiveSlots: [1,2,3,4,5,6,7,8,9,10],
  changedSlots: [6], clearedSlots: [7], retainedTargetSlots: [1,2,3,4,5],
  references: [{ kind: 'carrier', sourceIndex: 7, slots: [6], options: [9,10], targetIndex: null }],
  findings: [{ blocking: true, slot: null, message: 'Choose target carrier and review.' }], previewToken: 'initial' }

function mount(canAdd = true) {
  const onDraftCreated = vi.fn()
  render(<MemoryRouter><FirmwareQuickAccessStagingPanel phoneId="phone" targetId="target" candidateId="candidate" parentBusy={false}
    onBusyChange={vi.fn()} onDraftCreated={onDraftCreated} canAdd={canAdd} /></MemoryRouter>)
  return onDraftCreated
}
function layouts() {
  fireEvent.change(screen.getByLabelText('Source physical key layout'), { target: { value: '5' } })
  fireEvent.change(screen.getByLabelText('Target physical key layout'), { target: { value: '10' } })
}

it('requires physical layouts and reviewed references, then creates only an unassigned target draft', async () => {
  const callback = mount()
  const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
  expect(screen.getByRole('button', { name: 'Preview quick-access carry-forward' })).toBeDisabled()
  layouts()
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(preview))
  fireEvent.click(screen.getByRole('button', { name: 'Preview quick-access carry-forward' }))
  expect(await screen.findByText('Source positions 6, 7, 8, 9, 10; target positions 1, 2, 3, 4, 5, 6, 7, 8, 9, 10.')).toBeVisible()
  expect(screen.getByText('Candidate keys being cleared: 7.')).toBeVisible()
  expect(screen.getByText('Untouched target positions: 1, 2, 3, 4, 5.')).toBeVisible()
  fireEvent.change(screen.getByLabelText('Quick-access target carrier for 7'), { target: { value: '9' } })
  fireEvent.click(screen.getByLabelText('I reviewed the physical key layouts and target references'))
  expect(screen.queryByRole('button', { name: 'Create quick-access target draft' })).toBeNull()
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ ...preview, canCreate: true, findings: [], previewToken: 'reviewed',
    references: [{...preview.references[0], targetIndex: 9}] }))
  fireEvent.click(screen.getByRole('button', { name: 'Preview quick-access carry-forward' }))
  expect(await screen.findByRole('button', { name: 'Create quick-access target draft' })).toBeVisible()
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ id: 'new-draft' }, { status: 201 }))
  fireEvent.click(screen.getByRole('button', { name: 'Create quick-access target draft' }))
  expect(await screen.findByRole('link', { name: 'Open quick-access draft' })).toHaveAttribute('href', '/table-sets/new-draft')
  expect(confirm).toHaveBeenCalled()
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/firmware/transition-quick-access', expect.objectContaining({ sourceKeys: 5, targetKeys: 10,
    carrierChoices: { 7: 9 }, reviewed: true, previewToken: 'reviewed' }))
  fireEvent.click(screen.getByRole('button', { name: 'Use as target candidate for further review' }))
  expect(callback).toHaveBeenCalledWith('new-draft', 'Reviewed quick-access transition')
})

it('hides draft creation from a read-only operator', async () => {
  mount(false); layouts()
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ ...preview, canCreate: true, findings: [] }))
  fireEvent.click(screen.getByRole('button', { name: 'Preview quick-access carry-forward' }))
  expect(await screen.findByText('Table set creation permission is required to save the draft.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create quick-access target draft' })).toBeNull()
})
