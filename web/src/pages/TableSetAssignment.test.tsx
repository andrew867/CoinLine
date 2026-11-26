import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet, apiGetRaw, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { readDownloadReviews, rememberDownloadReview } from '../api/downloadReviews'
import { TableSetAssignment } from './TableSetAssignment'
import { TableSetDetail } from './TableSetDetail'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiGetRaw: vi.fn(), apiPostRaw: vi.fn(), apiPostBare: vi.fn(),
  apiPost: vi.fn(), apiPostBareOk: vi.fn(), apiPut: vi.fn() }))
afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

it('opens an exact phone in full-download mode without scheduling until confirmed and resolves the matching review', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 1, 'table-assignments': 3, downloads: 4 })
  rememberDownloadReview({ terminalId: 'review-phone', tableSetId: 'review-set', reason: 'assignment' })
  rememberDownloadReview({ terminalId: 'other-phone', tableSetId: 'review-set', reason: 'inventory' })
  vi.mocked(apiGet).mockImplementation(async path => (path === '/api/tables/scope-options?targetType=terminal'
    ? [{ id: 'review-phone', displayName: 'Review phone' }] : []) as never)
  vi.mocked(apiGetRaw).mockResolvedValue(Response.json({ targets: [{ id: 'review-phone', displayName: 'Review phone', eligible: true, reason: null }] }))
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ scheduled: 1, alreadyScheduled: 0 }))
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ id: 'review-set', name: 'Review set', status: 1, revision: 1, optionalRecordRemovals: [], firmwareFindings: [], versions: [] }))
  render(<MemoryRouter initialEntries={['/table-sets/review-set?schedulePhone=review-phone#table-assignment-heading']}>
    <Routes><Route path="/table-sets/:id" element={<TableSetDetail />} /></Routes>
  </MemoryRouter>)
  await screen.findByRole('option', { name: 'Review phone' })
  expect(screen.getByLabelText('Phone')).toHaveValue('review-phone')
  expect(apiPostRaw).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  expect(await screen.findByRole('button', { name: 'Schedule for 1 phone' })).toBeVisible()
  expect(screen.getByLabelText('Delivery')).toHaveValue('now')
  expect(apiPostRaw).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Schedule for 1 phone' }))
  expect(await screen.findByText('Scheduled for 1 phone. Already scheduled for 0.')).toBeVisible()
  expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/review-set/schedules', expect.objectContaining({ targetType: 'terminal', targetId: 'review-phone', terminalIds: ['review-phone'], availableAtUtc: null }))
  expect(readDownloadReviews()).toEqual([{ terminalId: 'other-phone', tableSetId: 'review-set', reason: 'inventory' }])
})

it('preselects only a valid call-in record from an exact phone review', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 1, 'table-assignments': 3, downloads: 4 })
  vi.mocked(apiGet).mockImplementation(async path => (path === '/api/tables/scope-options?targetType=terminal'
    ? [{ id: 'review-phone', displayName: 'Review phone' }] : []) as never)
  vi.mocked(apiGetRaw).mockResolvedValue(Response.json({ targets: [{ id: 'review-phone', displayName: 'Review phone', eligible: true }], recordNumbers: [35] }))
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ scheduled: 1, alreadyScheduled: 0 }))
  const versions = [
    { id: 'version-29', tableNumber: 29, definitionName: 'Advertising', payloadLengthBytes: 200, validationPassed: true, configurationId: null },
    { id: 'version-35', tableNumber: 35, definitionName: 'Routine call-in', payloadLengthBytes: 21, validationPassed: true, configurationId: null },
  ]
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ id: 'review-set', name: 'Review set', status: 1, revision: 1, optionalRecordRemovals: [], firmwareFindings: [], versions }))
  render(<MemoryRouter initialEntries={['/table-sets/review-set?schedulePhone=review-phone&scheduleRecord=35']}>
    <Routes><Route path="/table-sets/:id" element={<TableSetDetail />} /></Routes>
  </MemoryRouter>)
  await screen.findByRole('option', { name: 'Review phone' })
  expect(screen.getByLabelText('Phone')).toHaveValue('review-phone')
  expect(screen.getByLabelText('Assignment action')).toHaveValue('schedule-records')
  expect(screen.getByLabelText(/MT35.*Routine call-in/)).toBeChecked()
  expect(screen.getByLabelText(/MT29.*Advertising/)).not.toBeChecked()
  expect(apiPostRaw).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Schedule for 1 phone' }))
  expect(await screen.findByText('Scheduled for 1 phone. Already scheduled for 0.')).toBeVisible()
  expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/review-set/schedules', expect.objectContaining({ terminalIds: ['review-phone'], recordNumbers: [35] }))
})

it('does not default to a full download when the requested call-in record is absent', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { downloads: 4 })
  vi.mocked(apiGet).mockResolvedValue([{ id: 'review-phone', displayName: 'Review phone' }] as never)
  render(<MemoryRouter><TableSetAssignment setId="review-set" revision={1} versions={[]} optionalRecords={[]}
    initialSchedulePhone="review-phone" initialScheduleRecord={35} /></MemoryRouter>)
  await screen.findByRole('option', { name: 'Review phone' })
  expect(screen.getByLabelText('Assignment action')).toHaveValue('schedule-records')
  expect(screen.getByRole('button', { name: 'Preview phones' })).toBeDisabled()
  expect(apiPostRaw).not.toHaveBeenCalled()
})

it('preselects the individual MT35 assignment, previews a random time and confirms exactly that seed', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 1, 'table-records': 3, 'table-assignments': 1 })
  vi.mocked(apiGet).mockResolvedValue([{ id: 'review-phone', displayName: 'Review phone' }] as never)
  const randomization = { mode: 'window', windowStart: '01:00:00', windowEnd: '04:00:00', seed: '11111111-2222-3333-4444-555555555555' }
  const preview = { previewToken: 'reviewed-times', changeCount: 1, setCount: 1, pendingCount: 0, rolloutId: null,
    callInRandomization: randomization, targets: [{ id: 'review-phone', displayName: 'Review phone', reason: null,
      baseSetName: 'Current set', callInTime: '02:34:56', changes: [{ tableNumber: 35, action: 'replace', required: false }] }] }
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json(preview)).mockResolvedValueOnce(Response.json(preview))
    .mockResolvedValueOnce(Response.json({ changed: 1, sets: [{ id: 'new-set', name: 'Randomized set', phoneDisplayName: 'Review phone' }] }))
  const versions = [{ tableNumber: 35, definitionName: 'Routine call-in', payloadLengthBytes: 21, validationPassed: true, configurationId: null }]
  render(<MemoryRouter><TableSetAssignment setId="review-set" revision={1} versions={versions} optionalRecords={[]}
    initialRandomizePhone="review-phone" /></MemoryRouter>)
  await screen.findByRole('option', { name: 'Review phone' })
  expect(screen.getByLabelText('Phone')).toHaveValue('review-phone')
  expect(screen.getByLabelText('Assignment action')).toHaveValue('records')
  expect(screen.getByLabelText(/MT35.*Routine call-in/)).toHaveValue('copy')
  expect(screen.getByLabelText("Randomize each phone's routine MT35 call-in time")).toBeChecked()
  fireEvent.change(screen.getByLabelText('Randomization range'), { target: { value: 'window' } })
  fireEvent.change(screen.getByLabelText('Earliest call-in (phone clock)'), { target: { value: '01:00:00' } })
  fireEvent.change(screen.getByLabelText('Latest call-in (phone clock)'), { target: { value: '04:00:00' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview record update' }))
  expect(await screen.findByText('02:34:56')).toBeVisible()
  expect(apiPostRaw).toHaveBeenNthCalledWith(1, '/api/tables/sets/review-set/record-updates/preview',
    expect.objectContaining({ targetId: 'review-phone', copyRecords: [35], callInRandomization: {
      mode: 'window', windowStart: '01:00:00', windowEnd: '04:00:00' } }))
  fireEvent.change(screen.getByLabelText('Latest call-in (phone clock)'), { target: { value: '03:00:00' } })
  expect(screen.queryByText('02:34:56')).toBeNull()
  expect(screen.queryByRole('button', { name: /Update 1 phone/ })).toBeNull()
  fireEvent.change(screen.getByLabelText('Latest call-in (phone clock)'), { target: { value: '04:00:00' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview record update' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Update 1 phone' }))
  expect(await screen.findByRole('link', { name: 'Randomized set · Review phone' })).toHaveAttribute('href', '/table-sets/new-set')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/tables/sets/review-set/record-updates',
    expect.objectContaining({ previewToken: 'reviewed-times', copyRecords: [35], callInRandomization: randomization }))
})

it('does not clear a full configuration review when only a selected record is scheduled', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { downloads: 4 })
  const review = { terminalId: 'review-phone', tableSetId: 'review-set', reason: 'assignment' as const }
  rememberDownloadReview(review)
  vi.mocked(apiGet).mockResolvedValue([{ id: review.terminalId, displayName: 'Review phone' }] as never)
  vi.mocked(apiGetRaw).mockResolvedValue(Response.json({ targets: [{ id: review.terminalId, displayName: 'Review phone', eligible: true }], recordNumbers: [29] }))
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ scheduled: 1, alreadyScheduled: 0 }))
  render(<MemoryRouter><TableSetAssignment setId={review.tableSetId} revision={1}
    versions={[{ tableNumber: 29, definitionName: 'Advertising', payloadLengthBytes: 200, validationPassed: true, configurationId: null }]}
    optionalRecords={[]} initialSchedulePhone={review.terminalId} /></MemoryRouter>)
  await screen.findByRole('option', { name: 'Review phone' })
  fireEvent.change(screen.getByLabelText('Assignment action'), { target: { value: 'schedule-records' } })
  fireEvent.click(screen.getByLabelText(/MT29.*Advertising/))
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Schedule for 1 phone' }))
  expect(await screen.findByText('Scheduled for 1 phone. Already scheduled for 0.')).toBeVisible()
  expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/review-set/schedules', expect.objectContaining({ recordNumbers: [29] }))
  expect(readDownloadReviews()).toEqual([review])
})

it('keeps existing firmware schedules by default and confirms the exact records to replace', async () => {
  vi.mocked(apiGet).mockImplementation(async path => (path === '/api/tables/scope-options?targetType=terminal'
    ? [{ id: 'phone-fixture', displayName: 'Fixture phone' }] : []) as never)
  const phone = { id: 'phone-fixture', displayName: 'Fixture phone', terminalFamilyLabel: null }
  vi.mocked(apiGetRaw)
    .mockResolvedValueOnce(Response.json({ targets: [{ ...phone, eligible: false, reason: 'Existing firmware schedule will be kept.' }] }))
    .mockResolvedValueOnce(Response.json({
      targets: [{ ...phone, eligible: true, reason: 'Replace the existing pending firmware schedule.', replacesFirmwareScheduleId: 'prior-fixture' }],
    }))
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ scheduled: 1, replaced: 1, skipped: 0 }))

  render(<MemoryRouter><TableSetAssignment setId="set-fixture" revision={1} versions={[]} optionalRecords={[]} codeOnly /></MemoryRouter>)
  await screen.findByRole('option', { name: 'Fixture phone' })
  fireEvent.change(screen.getByLabelText('Phone'), { target: { value: 'phone-fixture' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  expect(await screen.findByText(/Existing firmware schedule will be kept/)).toBeVisible()
  expect(screen.getByRole('button', { name: 'Schedule for 0 phones' })).toBeDisabled()
  expect(apiGetRaw).toHaveBeenLastCalledWith(expect.stringContaining('allowFirmwareReschedule=false'))
  fireEvent.click(screen.getByLabelText('Allow rescheduling existing firmware parameters'))
  expect(screen.queryByText(/Existing firmware schedule will be kept/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  expect(await screen.findByText('1 existing schedule to replace. 0 phones to skip.')).toBeVisible()
  expect(apiGetRaw).toHaveBeenLastCalledWith(expect.stringContaining('allowFirmwareReschedule=true'))
  fireEvent.click(screen.getByRole('button', { name: 'Schedule for 1 phone' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Replaced 1 existing schedule.')
  expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/set-fixture/schedules', expect.objectContaining({
    terminalIds: ['phone-fixture'], allowFirmwareReschedule: true, replacesFirmwareScheduleIds: ['prior-fixture'],
  }))
})

it('previews the daily firmware window and confirms its exact allocation', async () => {
  vi.mocked(apiGet).mockImplementation(async path => (path === '/api/tables/scope-options?targetType=terminal'
    ? [{ id: 'window-phone', displayName: 'Window phone' }] : []) as never)
  const start = '2028-02-28T02:00:00Z'
  const end = '2028-02-28T02:30:00Z'
  vi.mocked(apiGetRaw).mockResolvedValueOnce(Response.json({
    targets: [{ id: 'window-phone', displayName: 'Window phone', eligible: true, firmwareStartAtUtc: start,
      firmwareSlotEndAtUtc: end, firmwareServerSlot: 1 }],
    firmwareAllocation: { previewToken: 'window-token', selectedBlocks: 3, minimumTransferMinutes: 1,
      activeServers: 2, slotMinutes: 30, firstStartAtUtc: start, lastEndAtUtc: end,
      existingReservations: 2, fixedTimeSchedules: 1 },
  }))
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ scheduled: 1, replaced: 0, skipped: 0 }))

  render(<MemoryRouter><TableSetAssignment setId="window-fixture" revision={1} versions={[]} optionalRecords={[]} codeOnly
    firmwareSchedule={{ enabled: true, selectedBlocks: 3, minimumTransferMinutes: 1, startAtUtc: start }} /></MemoryRouter>)
  await screen.findByRole('option', { name: 'Window phone' })
  fireEvent.change(screen.getByLabelText('Phone'), { target: { value: 'window-phone' } })
  fireEvent.click(screen.getByLabelText('Allocate terminal starts across a daily download window'))
  fireEvent.change(screen.getByLabelText('First date'), { target: { value: '2028-02-28' } })
  fireEvent.change(screen.getByLabelText('Daily start'), { target: { value: '02:00' } })
  fireEvent.change(screen.getByLabelText('Daily end'), { target: { value: '02:30' } })
  fireEvent.change(screen.getByLabelText('Active servers'), { target: { value: '2' } })
  fireEvent.change(screen.getByLabelText('Minutes per phone'), { target: { value: '30' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  expect(await screen.findByText(/2028-02-28 02:00 UTC to 2028-02-28 02:30 UTC/)).toBeVisible()
  expect(screen.getByText(/2 existing reservations accounted for/)).toBeVisible()
  expect(screen.getByRole('note')).toHaveTextContent('1 fixed-time schedule has no reserved duration')
  expect(apiGetRaw).toHaveBeenLastCalledWith(expect.stringContaining('firmwareWindow.activeServers=2'))
  fireEvent.click(screen.getByRole('button', { name: 'Schedule for 1 phone' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Scheduled firmware parameters for 1 phone.')
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/window-fixture/schedules', expect.objectContaining({
    firmwareWindow: { startDate: '2028-02-28', dailyStart: '02:00:00', dailyEnd: '02:30:00', activeServers: 2, slotMinutes: 30 },
    firmwarePreviewToken: 'window-token', terminalIds: ['window-phone'],
  }))
})

it('allows a download-only operator to select one record and schedule it without assignment or registry access', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 1, downloads: 4 })
  vi.mocked(apiGet).mockImplementation(async path => {
    if (path === '/api/terminals/families') return [] as never
    if (path === '/api/tables/scope-options?targetType=terminal') return [{ id: 'phone-fixture', displayName: 'Fixture phone' }] as never
    if (path === '/api/tables/scope-options?targetType=group') return [{ id: 'group-fixture', name: 'Fixture group', code: 'LAB' }] as never
    if (path === '/api/tables/scope-options?targetType=region') return [{ id: 'east', name: 'East' }] as never
    throw new Error(`Unexpected read: ${path}`)
  })
  vi.mocked(apiGetRaw).mockResolvedValue(Response.json({ targets: [{ id: 'phone-fixture', displayName: 'Fixture phone', eligible: true }], recordNumbers: [29] }))
  vi.mocked(apiPostRaw).mockResolvedValue(Response.json({ scheduled: 1, alreadyScheduled: 0 }))
  const versions = [29, 73].map(tableNumber => ({ id: `record-${tableNumber}`, tableNumber, definitionName: `Fixture ${tableNumber}`,
    validationPassed: true, payloadLengthBytes: 100, configurationId: null, tableRevision: 1, payloadSha256Hex: 'fixture-hash', warnings: [] }))
  vi.mocked(apiFetch).mockImplementation(async path => {
    if (path !== '/api/tables/sets/set-fixture') throw new Error(`Unexpected read: ${path}`)
    return Response.json({ id: 'set-fixture', name: 'Fixture set', status: 1, revision: 1, optionalRecordRemovals: [], firmwareFindings: [], versions })
  })
  render(<MemoryRouter initialEntries={['/table-sets/set-fixture']}>
    <Routes><Route path="/table-sets/:id" element={<TableSetDetail />} /></Routes>
  </MemoryRouter>)
  expect(await screen.findByRole('option', { name: 'Fixture phone' })).toBeVisible()
  expect(screen.queryByRole('option', { name: 'Assign full set' })).toBeNull()
  expect(screen.queryByRole('option', { name: 'Update selected records' })).toBeNull()
  fireEvent.change(screen.getByLabelText('Assignment action'), { target: { value: 'schedule-records' } })
  fireEvent.change(screen.getByLabelText('Schedule by'), { target: { value: 'group' } })
  expect(await screen.findByRole('option', { name: 'Fixture group (LAB)' })).toBeVisible()
  fireEvent.change(screen.getByLabelText('Phone group'), { target: { value: 'group-fixture' } })
  fireEvent.change(screen.getByLabelText('Within region'), { target: { value: 'east' } })
  fireEvent.click(screen.getByLabelText(/MT29.*Fixture 29/))
  fireEvent.click(screen.getByRole('button', { name: 'Preview phones' }))
  expect(await screen.findByText('Records to send MT29')).toBeVisible()
  expect(screen.queryByRole('option', { name: 'Assign for explicit table requests' })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Schedule for 1 phone' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Scheduled for 1 phone.')
  expect(apiGetRaw).toHaveBeenCalledWith(expect.stringMatching(/targetType=group&targetId=group-fixture&regionCode=east&recordNumbers=29/))
  expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/set-fixture/schedules', expect.objectContaining({
    targetType: 'group', targetId: 'group-fixture', regionCode: 'east', terminalIds: ['phone-fixture'], recordNumbers: [29],
  }))
  expect(vi.mocked(apiGet).mock.calls.every(([path]) => path === '/api/terminals/families' || String(path).startsWith('/api/tables/scope-options?'))).toBe(true)
  expect(apiFetch).toHaveBeenCalledTimes(1)
})

it('personalizes MT21 only after an explicit choice and a fresh per-phone preview', async () => {
  consoleSession.save('fixture', '', undefined, 'Admin')
  vi.mocked(apiGet).mockImplementation(async path => path === '/api/tables/scope-options?targetType=group'
    ? [{ id: 'group-fixture', name: 'Fixture access group' }] as never : [] as never)
  const phone = { id: 'phone-fixture', displayName: 'Fixture phone', terminalIdHex: '0000000002', baseSetName: 'Existing set' }
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ previewToken: 'plain-token', changeCount: 0, setCount: 0, pendingCount: 0,
    targets: [{ ...phone, reason: 'This set carries another phone’s wire ID.', changes: [] }] }))
    .mockResolvedValueOnce(Response.json({ previewToken: 'personalized-token', changeCount: 1, setCount: 1, pendingCount: 0,
      targets: [{ ...phone, reason: null, changes: [{ tableNumber: 21, action: 'replace', required: false, personalized: true }] }] }))
    .mockResolvedValueOnce(Response.json({ changed: 1, sets: [{ id: 'personalized-set', name: 'Personalized set', phoneDisplayName: 'Fixture phone' }] }))
  render(<MemoryRouter><TableSetAssignment setId="source-fixture" revision={2} optionalRecords={[]}
    versions={[{ tableNumber: 21, definitionName: 'Access settings', payloadLengthBytes: 48, validationPassed: true, configurationId: null }]} /></MemoryRouter>)
  fireEvent.change(screen.getByLabelText('Assignment action'), { target: { value: 'records' } })
  fireEvent.change(screen.getByLabelText(/Assign by|Update by/), { target: { value: 'group' } })
  await screen.findByRole('option', { name: 'Fixture access group' })
  fireEvent.change(screen.getByLabelText('Phone group'), { target: { value: 'group-fixture' } })
  expect(screen.queryByLabelText(/Use each phone's registered wire ID/)).toBeNull()
  fireEvent.change(screen.getByLabelText(/MT21.*Access settings/), { target: { value: 'copy' } })
  expect(screen.getByLabelText(/Use each phone's registered wire ID/)).not.toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Preview record update' }))
  expect(await screen.findByText(/another phone’s wire ID/)).toBeVisible()
  expect(vi.mocked(apiPostRaw).mock.calls[0][1]).not.toHaveProperty('personalizeAccessIdentity')
  fireEvent.click(screen.getByLabelText(/Use each phone's registered wire ID/))
  expect(screen.queryByText(/another phone’s wire ID/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Preview record update' }))
  expect(await screen.findByText('Replace MT21 (registered phone ID)')).toBeVisible()
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/source-fixture/record-updates/preview',
    expect.objectContaining({ copyRecords: [21], personalizeAccessIdentity: true }))
  fireEvent.click(screen.getByRole('button', { name: 'Update 1 phone' }))
  expect(await screen.findByRole('link', { name: 'Personalized set · Fixture phone' })).toHaveAttribute('href', '/table-sets/personalized-set')
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/source-fixture/record-updates',
    expect.objectContaining({ previewToken: 'personalized-token', personalizeAccessIdentity: true, copyRecords: [21] }))
})
