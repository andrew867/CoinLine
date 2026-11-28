import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet, apiPost } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwarePackageDetail } from './FirmwarePackageDetail'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })
const endpoint = '/api/firmware/packages/fixture-package'
const base = { id: 'fixture-package', name: 'Fixture package', versionLabel: 'A001', artifactChecksumHex: 'a'.repeat(64), artifactSizeBytes: 4,
  metadataJson: '{}', primaryArtifactId: null, hardwareValidationNotice: 'Fixture notice', artifacts: [], targets: [], rules: [] }
const stored = { ...base, primaryArtifactId: 'fixture-artifact', artifacts: [{ id: 'fixture-artifact', kind: 'primary', sha256Hex: 'a'.repeat(64), sizeBytes: 4, storageRef: 'firmware/fixture.bin', payloadPresent: true }] }
function open(role = 'Admin') {
  consoleSession.save('fixture', '', undefined, role)
  render(<MemoryRouter initialEntries={['/firmware/packages/fixture-package']}><Routes><Route path="/firmware/packages/:id" element={<FirmwarePackageDetail />} /></Routes></MemoryRouter>)
}

it('uploads the actual matching file through the same-origin API and shows stored primary state', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce(base).mockResolvedValueOnce(stored)
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ id: 'fixture-artifact', payloadPresent: true }, { status: 201 }))
  open()
  const file = new File([new Uint8Array([1, 2, 3, 4])], 'fixture.bin', { type: 'application/octet-stream' })
  fireEvent.change(await screen.findByLabelText('Primary artifact file'), { target: { files: [file] } })
  fireEvent.click(screen.getByRole('button', { name: 'Upload primary artifact' }))
  await waitFor(() => expect(apiFetch).toHaveBeenCalledWith(`${endpoint}/artifacts/upload`, {
    method: 'POST', headers: { 'Content-Type': 'application/octet-stream' }, body: file,
  }))
  expect(await screen.findByText('File present · Primary')).toBeVisible()
  expect(screen.getByText(/Primary artifact stored and verified/)).toBeVisible()
  expect(apiPost).not.toHaveBeenCalled()
})

it('does not send a wrong-sized file and retains the package on a server digest rejection', async () => {
  vi.mocked(apiGet).mockResolvedValue(base)
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ error: 'Uploaded artifact SHA-256 does not match the registered package.' }, { status: 409 }))
  open()
  const input = await screen.findByLabelText('Primary artifact file')
  fireEvent.change(input, { target: { files: [new File(['bad'], 'fixture.bin')] } })
  fireEvent.click(screen.getByRole('button', { name: 'Upload primary artifact' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('exactly the registered package size')
  expect(apiFetch).not.toHaveBeenCalled()
  fireEvent.change(input, { target: { files: [new File(['four'], 'fixture.bin')] } })
  fireEvent.click(screen.getByRole('button', { name: 'Upload primary artifact' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('SHA-256 does not match')
  expect(screen.getByRole('heading', { name: 'Fixture package' })).toBeVisible()
  expect(screen.getByRole('button', { name: 'Upload primary artifact' })).toBeEnabled()
})

it('does not expose the binary upload action to a non-admin', async () => {
  vi.mocked(apiGet).mockResolvedValue(base)
  open('Operator')
  expect(await screen.findByRole('heading', { name: 'Fixture package' })).toBeVisible()
  expect(screen.queryByLabelText('Primary artifact file')).not.toBeInTheDocument()
})
