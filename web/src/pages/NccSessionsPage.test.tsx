import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet } from '../api/client'
import { NccSessionsPage } from './NccSessionsPage'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiDownload: vi.fn(), apiPostBareOk: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const row = { id: 'fixture-session', terminalId: null, correlationId: 'fixture-contact', status: 1,
  startedAtUtc: '2028-01-01T01:00:00Z', endedAtUtc: '2028-01-01T01:01:00Z', lastFrameSampleHex: null }

async function inspect(detail: object) {
  vi.mocked(apiGet).mockResolvedValueOnce([row]).mockResolvedValueOnce(detail)
  render(<MemoryRouter><NccSessionsPage /></MemoryRouter>)
  fireEvent.click(await screen.findByRole('button', { name: 'Inspect upload records' }))
  return screen.findByRole('region', { name: 'Call-upload observations' })
}

it('shows the terminal cashbox status and collection in frame order without claiming settlement', async () => {
  const section = await inspect({ callUploadFrames: [
    { sequence: 6, parsed: true, endsDialogue: true, records: [
      { messageType: 38, byteCount: 57 }, { messageType: 51, byteCount: 71 }, { messageType: 13, byteCount: 1 },
    ] },
    { sequence: 9, parsed: false, endsDialogue: null, records: null },
  ] })
  const table = await within(section).findByRole('table', { name: 'Received upload frames' })
  const rows = within(table).getAllByRole('row')
  expect(within(rows[1]).getAllByRole('listitem').map(item => item.textContent)).toEqual([
    'Cashbox status (MT38), 57 bytes', 'Cashbox collection (MT51), 71 bytes', 'End of data (MT13), 1 byte',
  ])
  expect(within(rows[1]).getByText('Present')).toBeVisible()
  expect(within(rows[2]).getByText(/Unrecognized or incomplete upload frame/)).toBeVisible()
  expect(within(rows[2]).getByText('Unknown')).toBeVisible()
  expect(within(section).getByText(/not proof of accounting or terminal completion/)).toBeVisible()
  expect(apiGet).toHaveBeenLastCalledWith('/api/ncc/sessions/fixture-session/upload-observations')
})

it('distinguishes an older session with no retained metadata from a proven empty upload', async () => {
  const section = await inspect({ frames: [] })
  expect(await within(section).findByText(/does not establish that the phone sent no call or cashbox records/)).toBeVisible()
  expect(within(section).queryByRole('table')).not.toBeInTheDocument()
})
