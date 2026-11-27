import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'
import { FirmwareVisualPromptStagingPanel } from './FirmwareVisualPromptStagingPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

const preview = { sourceSetId: 'source', candidateSetId: 'candidate', recordCount: 3, canCreate: false, changedBytes: 0, previewToken: 'initial',
  pairs: [{ sourceNumber: 27, targetNumber: 85, status: 'available', changedBytes: 0, messages: [
    { sourceMessage: 0, targetMessage: null, sourceLabel: 'Messaging offer', targetLabel: null, sourceLines: ['Unmapped text', ''], targetLines: [], status: 'unmapped', eligible: false, selected: false, potentialChangedBytes: 0 },
    { sourceMessage: 28, targetMessage: 70, sourceLabel: 'Emergency service', targetLabel: 'Emergency service', sourceLines: ['Emergency only', ''], targetLines: ['Old wording', 'Retained lower'], status: 'different', eligible: true, selected: false, potentialChangedBytes: 18 },
  ] }, { sourceNumber: 28, targetNumber: 86, status: 'available', changedBytes: 0, messages: [
    { sourceMessage: 14, targetMessage: 14, sourceLabel: 'Dial number', targetLabel: 'Dial number', sourceLines: ['Dial now', ''], targetLines: ['Dial', ''], status: 'different', eligible: true, selected: false, potentialChangedBytes: 4 },
    { sourceMessage: 15, targetMessage: 15, sourceLabel: 'Correct digits', targetLabel: 'Correct digits', sourceLines: [null, ''], targetLines: ['Correct', ''], status: 'unknown-glyph', eligible: false, selected: false, potentialChangedBytes: 0 },
  ] }], findings: [{ blocking: true, message: 'Choose at least one mapped message.' }] }

it('reviews two language pairs, keeps unmatched positions disabled and requires a fresh selected preview before staging', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'Later layout', tableProfileId: 'mtr211-multipay-180-double-compressed' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target template', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'Earlier layout', targetFirmwareLabel: 'Later layout', sourceSetId: 'source',
      sourceProfileId: 'mtr20-early-multipay-200-single-compressed', targetProfileId: 'mtr211-multipay-180-double-compressed',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], records: [
        { tableNumber: 27, sourceBytes: 2801, targetExpectedBytes: null, candidateBytes: null },
        { tableNumber: 28, sourceBytes: 2801, targetExpectedBytes: null, candidateBytes: null },
        { tableNumber: 85, sourceBytes: null, targetExpectedBytes: 8001, candidateBytes: 8001 },
        { tableNumber: 86, sourceBytes: null, targetExpectedBytes: 8001, candidateBytes: 8001 },
      ] })
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(preview)).mockResolvedValueOnce(Response.json({ ...preview, canCreate: true,
    changedBytes: 22, previewToken: 'reviewed', findings: [] })).mockResolvedValueOnce(Response.json({ id: 'prompt-draft', changedBytes: 22 }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward visual prompt configuration' })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview visual prompt carry-forward' }))
  expect(await within(region).findByText('70 · Emergency service')).toBeVisible()
  expect(within(region).getByLabelText('Carry MT27 message 0')).toBeDisabled()
  expect(within(region).getByLabelText('Carry MT28 message 15')).toBeDisabled()
  expect(within(region).getAllByText('(blank)').length).toBeGreaterThan(0)
  fireEvent.click(within(region).getByLabelText('Carry MT27 message 28'))
  fireEvent.click(within(region).getByLabelText('Carry MT28 message 14'))
  fireEvent.click(within(region).getByLabelText('I reviewed the selected text and target message positions'))
  expect(within(region).queryByRole('button', { name: 'Create visual prompt target draft' })).not.toBeInTheDocument()
  fireEvent.click(within(region).getByRole('button', { name: 'Preview visual prompt carry-forward' }))
  expect(await within(region).findByText(/22 prompt cell bytes would change/)).toBeVisible()
  fireEvent.click(within(region).getByRole('button', { name: 'Create visual prompt target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open visual prompt draft' })).toHaveAttribute('href', '/table-sets/prompt-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/firmware/transition-prompts', expect.objectContaining({
    selections: [{ sourceNumber: 27, messageIds: [28] }, { sourceNumber: 28, messageIds: [14] }], reviewed: true, previewToken: 'reviewed',
  }))
  fireEvent.click(within(region).getByRole('button', { name: 'Use as target candidate for further review' }))
  expect(screen.getByLabelText('Prepared target configuration')).toHaveValue('prompt-draft')
})

it('reports missing language records and will not offer draft creation to a read-only operator', async () => {
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ ...preview, canCreate: true,
    pairs: [preview.pairs[0], { sourceNumber: 28, targetNumber: 86, status: 'missing-target', messages: [], changedBytes: 0 }] }))
  render(<MemoryRouter><FirmwareVisualPromptStagingPanel phoneId="phone" targetId="target" candidateId="candidate" parentBusy={false}
    onBusyChange={vi.fn()} onDraftCreated={vi.fn()} canAdd={false} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Preview visual prompt carry-forward' }))
  expect(await screen.findByText('Prepared target language record missing or incompatible.')).toBeVisible()
  expect(screen.getByText('Table set creation permission is required to save the draft.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create visual prompt target draft' })).toBeNull()
})
