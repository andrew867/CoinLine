import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { InmateAssignmentPanel } from './InmateAssignmentPanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn() }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

const path = '/api/terminals/fixture-phone/inmate-configuration'
const feature = { id: 'fixture-feature', name: 'FTR001' }
const initial = {
  eligibleFamily: true, featureConfigurationId: null, featureConfigurationName: null,
  setupConfigurationId: null, revision: null, featureConfigurations: [feature], wireFormatVerified: false,
}
const assigned = {
  ...initial, featureConfigurationId: feature.id, featureConfigurationName: feature.name,
  setupConfigurationId: 'LAB01', revision: 1,
}

function renderPanel(family = 'msr17-inmate') {
  render(<MemoryRouter><InmateAssignmentPanel terminalId="fixture-phone" family={family} /></MemoryRouter>)
}

it('saves the selected feature and external setup reference without suggesting a download', async () => {
  vi.mocked(apiGet).mockResolvedValue(initial)
  vi.mocked(apiFetch).mockResolvedValue(Response.json(assigned))
  renderPanel()
  await screen.findByText(/packed terminal record formats are not verified/)
  fireEvent.change(screen.getByLabelText('Inmate Feature configuration'), { target: { value: feature.id } })
  fireEvent.change(screen.getByLabelText('External Inmate Setup Configuration ID'), { target: { value: 'lab01' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save inmate configuration' }))
  expect(await screen.findByText(/No table download was created/)).toBeVisible()
  expect(apiFetch).toHaveBeenCalledWith(path, expect.objectContaining({
    method: 'PUT', body: JSON.stringify({ featureConfigurationId: feature.id, setupConfigurationId: 'lab01', revision: null }),
  }))
  expect(screen.getByLabelText('External Inmate Setup Configuration ID')).toHaveValue('LAB01')
  expect(screen.getByRole('button', { name: 'Save inmate configuration' })).toBeDisabled()
})

it('allows viewing named configuration references without granting feature authoring or assignment changes', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'inmate-assignments': 1 })
  vi.mocked(apiGet).mockResolvedValue(assigned)
  renderPanel()
  expect(await screen.findByLabelText('Inmate Feature configuration')).toBeDisabled()
  expect(screen.getByLabelText('External Inmate Setup Configuration ID')).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Save inmate configuration' })).toBeDisabled()
  expect(screen.queryByRole('link', { name: 'Manage Inmate Feature configurations' })).toBeNull()
})

it('retains and permits explicitly clearing inactive references after a terminal family change', async () => {
  vi.mocked(apiGet).mockResolvedValue({ ...assigned, eligibleFamily: false, featureConfigurations: [] })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ ...initial, eligibleFamily: false, revision: 2, featureConfigurations: [] }))
  renderPanel('mtr19-multipay')
  expect(await screen.findByText(/retained but inactive/)).toBeVisible()
  expect(screen.getByText(feature.name)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Clear retained references' }))
  await screen.findByText(/No table download was created/)
  expect(apiFetch).toHaveBeenCalledWith(path, expect.objectContaining({
    method: 'PUT', body: JSON.stringify({ featureConfigurationId: null, setupConfigurationId: null, revision: 1 }),
  }))
  expect(screen.queryByText(feature.name)).toBeNull()
})
