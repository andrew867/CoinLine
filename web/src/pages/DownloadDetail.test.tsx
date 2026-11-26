import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { DownloadDetail } from './DownloadDetail'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostBare: vi.fn(), apiPostBareOk: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const base = { id: 'fixture-batch', terminalId: 'fixture-phone', nccSessionId: 'fixture-session', scheduleId: null,
  tableSetId: 'fixture-set', tableSetName: 'Fixture configuration', status: 2, scope: 0, retryCount: 0, lastError: null,
  diagnosticsJson: '{"phase":"prepared"}', createdAtUtc: '2026-01-01T12:00:00Z', completedAtUtc: '2026-01-01T12:00:20Z',
  sessionEvidence: { status: 1, startedAtUtc: '2026-01-01T12:00:00Z', endedAtUtc: '2026-01-01T12:00:22Z',
    terminalClearObservedAtUtc: '2026-01-01T12:00:21Z', terminalClearSequence: 611, releaseOutcome: 'peer_eof', releaseElapsedMilliseconds: 206.5 },
  timeline: [26, 33].map((number, stepIndex) => ({ id: `step-${stepIndex}`, stepIndex, tableNumber: number, tableDefinitionName: `Fixture ${number}`,
    itemStatus: 3, hostDownloadPhase: 9, hostDownloadPhaseName: 'Completed', lastAckStatus: 'ack', succeeded: true, errorDetail: null,
    tableVersionId: `version-${number}`, payloadSha256Hex: 'a'.repeat(64) })) }

function show(permissions = { downloads: 1, phones: 1, 'ncc-sessions': 1 }) {
  consoleSession.save('fixture', '', undefined, 'Operator', permissions)
  render(<MemoryRouter initialEntries={['/downloads/fixture-batch']}><Routes><Route path="/downloads/:id" element={<DownloadDetail />} /></Routes></MemoryRouter>)
}

it('distinguishes all record ACKs and recorded peer release from actual terminal service', async () => {
  vi.mocked(apiFetch).mockResolvedValue(Response.json(base)); show()
  const host = await screen.findByRole('region', { name: 'Host transfer evidence' })
  expect(within(host).getByText('2 of 2 records acknowledged')).toBeVisible()
  expect(within(host).getByText(/Observed, frame 611/)).toBeVisible()
  expect(within(host).getByText(/Peer closed the transport after terminal clear.*206.5 ms host time/)).toBeVisible()
  const service = screen.getByRole('region', { name: 'Terminal service verification' })
  expect(within(service).getByText('Not verified by this download')).toBeVisible()
  expect(within(service).getByRole('link')).toHaveAttribute('href', '/terminals/fixture-phone')
  expect(screen.getByRole('link', { name: 'fixture-session' })).toHaveAttribute('href', '/ncc-sessions?sessionId=fixture-session')
  expect(screen.getByText('MT33', { exact: true })).toBeVisible()
  expect(screen.getByText('Host diagnostic metadata').closest('details')).not.toHaveAttribute('open')
})

it('does not count an unproven completed row or make service claims when session metadata is missing', async () => {
  const incomplete = { ...base, sessionEvidence: null, timeline: [base.timeline[0], { ...base.timeline[1], succeeded: false, itemStatus: 4, lastAckStatus: 'missing_table_ack' }] }
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json(incomplete)).mockResolvedValueOnce(Response.json({ ...incomplete, status: 3, lastError: 'MT33 acknowledgement missing' }))
  show({ downloads: 1, phones: 0, 'ncc-sessions': 0 })
  const host = await screen.findByRole('region', { name: 'Host transfer evidence' })
  expect(within(host).getByText('1 of 2 records acknowledged')).toBeVisible()
  expect(within(host).getByText(/retained timeline does not establish acknowledgement/)).toBeVisible()
  expect(within(host).getByText('No linked session evidence is available in this response.')).toBeVisible()
  expect(screen.queryByRole('link', { name: 'fixture-session' })).toBeNull()
  expect(screen.queryByRole('link', { name: /phone's reported status/ })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Refresh status' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('MT33 acknowledgement missing')
  expect(screen.queryByRole('button', { name: 'Retry (new batch)' })).toBeNull()
})
