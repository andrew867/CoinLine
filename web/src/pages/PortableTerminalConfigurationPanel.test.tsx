import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiDownload, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { PortableTerminalConfigurationPanel } from './PortableTerminalConfigurationPanel'

vi.mock('../api/client', () => ({ apiDownload: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

it('exports a package, reviews a local import and explicitly creates a separate draft', async () => {
  const created = vi.fn()
  const fixture = { schema: 'coinline-terminal-configuration-v1', profileId: 'fixture', records: [], omittedNumbers: [21, 93] }
  const selectedFile = new File([JSON.stringify(fixture)], 'configuration.json', { type: 'application/json' })
  Object.defineProperty(selectedFile, 'text', { value: () => Promise.resolve(JSON.stringify(fixture)) })
  vi.mocked(apiDownload).mockResolvedValue()
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ previewToken: 'reviewed', canCreate: true, targetFirmware: 'Fixture target',
    records: [{ number: 31, action: 'replace', length: 37, retainedLocalFields: true }],
    retainedNumbers: [21], missingOmittedNumbers: [93], findings: [{ tableNumber: 93, blocking: true, message: 'Prepare locally.' }] }))
    .mockResolvedValueOnce(Response.json({ id: 'new-draft' }, { status: 201 }))
  render(<PortableTerminalConfigurationPanel setId="base-set" supported onCreated={created} />)
  fireEvent.click(screen.getByRole('button', { name: 'Show configuration packages' }))
  expect(screen.getByText(/Phone identity, protected payment and service records/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Export configuration JSON' }))
  await waitFor(() => expect(apiDownload).toHaveBeenCalledWith('/api/tables/sets/base-set/portable-package', 'coinline-terminal-configuration.json'))
  await waitFor(() => expect(screen.getByLabelText('Configuration package file')).toBeEnabled())
  fireEvent.change(screen.getByLabelText('Configuration package file'), { target: { files: [selectedFile] } })
  expect(await screen.findByText('Selected package: configuration.json')).toBeVisible()
  fireEvent.change(screen.getByLabelText('New draft name'), { target: { value: 'Shared settings' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview configuration import' }))
  const review = await screen.findByRole('region', { name: 'Configuration import preview' })
  expect(within(review).getByText('Retained from local base')).toBeVisible()
  expect(within(review).getByText(/Omitted package records missing from this base: MT93/)).toBeVisible()
  const confirm = within(review).getByRole('button', { name: 'Create imported draft' })
  expect(confirm).toBeDisabled()
  expect(apiPostRaw).toHaveBeenCalledTimes(1)
  fireEvent.click(within(review).getByRole('checkbox', { name: /reviewed the included/ }))
  fireEvent.click(confirm)
  await waitFor(() => expect(created).toHaveBeenCalledWith('new-draft'))
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/base-set/portable-package/import', {
    name: 'Shared settings', package: fixture, previewToken: 'reviewed', confirm: true,
  })
})

it('allows read-only exports but requires a verified profile and separate import permissions', () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 1, 'table-records': 1 })
  render(<PortableTerminalConfigurationPanel setId="base-set" supported={false} onCreated={vi.fn()} />)
  fireEvent.click(screen.getByRole('button', { name: 'Show configuration packages' }))
  expect(screen.getByRole('button', { name: 'Export configuration JSON' })).toBeDisabled()
  expect(screen.queryByLabelText('Configuration package file')).not.toBeInTheDocument()
})
