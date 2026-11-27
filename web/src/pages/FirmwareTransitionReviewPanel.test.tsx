import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareTransitionReviewPanel } from './FirmwareTransitionReviewPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

it('reviews the target family requirements without changing the registered phone type', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'MTR 2.11', tableProfileId: 'later' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target configuration', status: 1, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce([{ id: 'mtr20-desk', release: 'MTR 2.0', variant: 'Desk', maximizerTypeNumbers: [37] }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'MTR 2.0', targetFirmwareLabel: 'MTR 2.11', sourceSetId: 'source',
      candidateSetId: 'candidate', candidatePublished: true, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], registeredFamilyId: 'mtr20-multipay', records: [
        { tableNumber: 101, sourceBytes: 403, targetExpectedBytes: null, candidateBytes: null, action: 'candidate-local-equivalent',
          detail: 'All local tariff tuples match.', targetAcknowledged: false, requiresLayoutReview: false, replacementRecords: [136] }],
      targetFamilyReview: { familyId: 'mtr20-desk', familyLabel: 'MTR 2.0 Desk', documented: true, screeningEntries: 200, note: null, tables: [
        { name: 'Dialing rules', required: true, candidates: [62], records: [], status: 'missing' },
        { name: 'Call screening', required: true, candidates: [92], status: 'present', records: [{ number: 92, length: 3061, format: 'verifiedLength', screeningNote: '180 packed slots' }] },
        { name: 'Text advertising', required: false, candidates: [29], records: [], status: 'missing' },
      ] } })
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByLabelText('Review a different target terminal family'))
  await screen.findByRole('option', { name: 'MTR 2.0 Desk (37)' })
  fireEvent.change(screen.getByLabelText('Terminal family'), { target: { value: 'mtr20-desk' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const family = await screen.findByRole('region', { name: 'Target terminal family checklist' })
  expect(within(family).getByText('1 / 2')).toBeVisible()
  const dialing = within(family).getByRole('row', { name: /Dialing rules/ })
  expect(dialing).toHaveTextContent('Mandatory')
  expect(dialing).toHaveTextContent('Missing from set')
  expect(within(family).getByRole('row', { name: /Text advertising/ })).toHaveTextContent('Feature-specific')
  expect(within(family).getByRole('link', { name: 'MT92' })).toHaveAttribute('href', '/table-sets/candidate?record=92&family=mtr20-desk#config-heading')
  expect(within(family).getByText('180 packed slots')).toBeVisible()
  expect(screen.getByText('Equivalent local tariff tuples')).toBeVisible()
  expect(screen.getByRole('link', { name: 'Review MT136 replacement' })).toHaveAttribute('href', '/table-sets/candidate?record=136#config-heading')
  expect(apiGet).toHaveBeenLastCalledWith('/api/firmware/transition-review?terminalId=phone&targetFirmwareVersionId=target&candidateSetId=candidate&targetFamilyId=mtr20-desk')
  expect(apiPostRaw).not.toHaveBeenCalled()
})

it('reviews record size changes without offering an upgrade execution action', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'MTR 2.11', tableProfileId: 'later' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target draft', status: 0, targetFirmwareVersionId: 'target' },
      { id: 'other', name: 'Unrelated set', status: 1, targetFirmwareVersionId: 'other' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'MTR 2.0', targetFirmwareLabel: 'MTR 2.11', sourceSetId: 'source',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false,
      records: [{ tableNumber: 92, sourceBytes: 3401, targetExpectedBytes: 3061, candidateBytes: 3061,
        action: 'candidate-replacement', detail: 'Verify translated settings.', targetAcknowledged: true, requiresLayoutReview: false }],
      findings: [{ blocking: true, tableNumber: null, message: 'The target configuration is still a draft.' }] })
  render(<FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} />)
  expect(apiGet).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  expect(within(screen.getByLabelText('Prepared target configuration')).queryByText('Unrelated set')).not.toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  expect(await screen.findByText('MTR 2.0 → MTR 2.11')).toBeVisible()
  const row = screen.getByRole('row', { name: /MT92/ })
  expect(within(row).getByText('3401')).toBeVisible()
  expect(within(row).getAllByText('3061')).toHaveLength(2)
  expect(within(row).getByText('Replacement supplied')).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Remote upgrade is not qualified.')
  expect(screen.getByText(/still a draft/)).toBeVisible()
  expect(apiGet).toHaveBeenLastCalledWith('/api/firmware/transition-review?terminalId=phone&targetFirmwareVersionId=target&candidateSetId=candidate')
  expect(screen.queryByRole('button', { name: /execute|schedule|activate/i })).not.toBeInTheDocument()
})

it('previews management access only and creates a separate unassigned target draft with the reviewed token', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'MTR 2.11', tableProfileId: 'later' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target draft', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'MTR 2.0', targetFirmwareLabel: 'MTR 2.11', sourceSetId: 'source',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false,
      records: [{ tableNumber: 21, sourceBytes: 18, targetExpectedBytes: 48, candidateBytes: 48,
        action: 'candidate-replacement', detail: 'Review management access.', targetAcknowledged: true, requiresLayoutReview: false }], findings: [] })
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ sourceSetId: 'source', candidateSetId: 'candidate',
    sourceBytes: 18, targetBytes: 48, changedBytes: 16, recordCount: 12, previewToken: 'reviewed-token' }))
    .mockResolvedValueOnce(Response.json({ id: 'staged-draft', changedBytes: 16 }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward management access' })
  fireEvent.change(within(region).getByLabelText('New target draft name'), { target: { value: 'Prepared phone target' } })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview management-access carry-forward' }))
  expect(await within(region).findByText(/18 bytes to 48 bytes; 16 changed target bytes. 11 other records/)).toBeVisible()
  expect(apiPostRaw).toHaveBeenNthCalledWith(1, '/api/firmware/transition-access/preview',
    { terminalId: 'phone', targetFirmwareVersionId: 'target', candidateSetId: 'candidate', name: 'Prepared phone target', previewToken: undefined })
  fireEvent.click(within(region).getByRole('button', { name: 'Create target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open target draft and review remaining records' })).toHaveAttribute('href', '/table-sets/staged-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(2, '/api/firmware/transition-access',
    { terminalId: 'phone', targetFirmwareVersionId: 'target', candidateSetId: 'candidate', name: 'Prepared phone target', previewToken: 'reviewed-token' })
  expect(within(region).getByText(/Target draft created, not assigned/)).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Remote upgrade is not qualified.')
})

it('requires review of an ambiguous tariff and a fresh preview before creating a screening draft', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce([{ id: 'target', label: 'MTR 2.11', tableProfileId: 'later' }])
    .mockResolvedValueOnce([{ id: 'candidate', name: 'Target draft', status: 0, targetFirmwareVersionId: 'target' }])
    .mockResolvedValueOnce({ sourceFirmwareLabel: 'MTR 2.0', targetFirmwareLabel: 'MTR 2.11', sourceSetId: 'source',
      candidateSetId: 'candidate', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false,
      records: [{ tableNumber: 92, sourceBytes: 3401, targetExpectedBytes: 3061, candidateBytes: 3061,
        action: 'candidate-replacement', detail: 'Review screening capacity.', targetAcknowledged: true, requiresLayoutReview: false }], findings: [] })
  const initial = { sourceSlots: 200, targetSlots: 180, sourceConfiguredSlots: 8, changedSlots: 9,
    sourceReservedSlots: 2, retainedLaterFlagsSlots: 0, descriptionCount: 1, recordCount: 12, canCreate: false,
    changedPositions: [1, 2, 3, 4, 5, 6, 7, 8, 9], clearedTargetPositions: [9],
    findings: [{ blocking: true, slot: null, message: 'Choose the target tariff for source 0 before staging.' }],
    references: [{ kind: 'tariff', sourceIndex: 0, slots: [1, 2], options: [4, 7], targetIndex: null }], previewToken: 'blocked-token' }
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(initial)).mockResolvedValueOnce(Response.json({ ...initial,
    canCreate: true, references: [{ ...initial.references[0], targetIndex: 7 }], findings: [], previewToken: 'reviewed-token' }))
    .mockResolvedValueOnce(Response.json({ id: 'screening-draft', changedSlots: 9 }, { status: 201 }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<MemoryRouter><FirmwareTransitionReviewPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Review a firmware transition' }))
  fireEvent.change(await screen.findByLabelText('Target firmware'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Prepared target configuration'), { target: { value: 'candidate' } })
  fireEvent.click(screen.getByRole('button', { name: 'Compare configurations' }))
  const region = await screen.findByRole('region', { name: 'Carry forward screening rules' })
  fireEvent.change(within(region).getByLabelText('New screening draft name'), { target: { value: 'Prepared screening' } })
  fireEvent.click(within(region).getByRole('button', { name: 'Preview screening carry-forward' }))
  expect(await within(region).findByText('200 source entries to 180 target entries.')).toBeVisible()
  expect(within(region).getByText('Configured candidate positions becoming vacant (1):')).toBeVisible()
  expect(within(region).getByText(/Choose the target tariff/)).toBeVisible()
  expect(within(region).queryByRole('button', { name: 'Create screening target draft' })).not.toBeInTheDocument()
  fireEvent.change(within(region).getByLabelText('Target tariff for 0'), { target: { value: '7' } })
  expect(within(region).getByRole('status')).toHaveTextContent('Preview again')
  fireEvent.click(within(region).getByRole('button', { name: 'Preview screening carry-forward' }))
  expect(await within(region).findByRole('button', { name: 'Create screening target draft' })).toBeEnabled()
  expect(apiPostRaw).toHaveBeenNthCalledWith(2, '/api/firmware/transition-screening/preview', expect.objectContaining({ tariffChoices: { '0': 7 } }))
  fireEvent.click(within(region).getByRole('button', { name: 'Create screening target draft' }))
  expect(await within(region).findByRole('link', { name: 'Open screening draft' })).toHaveAttribute('href', '/table-sets/screening-draft')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/firmware/transition-screening', expect.objectContaining({ tariffChoices: { '0': 7 }, previewToken: 'reviewed-token' }))
  fireEvent.click(within(region).getByRole('button', { name: 'Use as target candidate for further review' }))
  expect(screen.getByLabelText('Prepared target configuration')).toHaveValue('screening-draft')
})
