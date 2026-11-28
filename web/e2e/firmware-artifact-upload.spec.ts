import { createHash } from 'node:crypto'
import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('uploads a matching artifact and approves the exact scheduled job without transferring code', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-artifact-admin', packageId = 'fixture-package', artifactId = 'fixture-artifact', phoneId = 'fixture-phone'
  const bytes = Buffer.from(Array.from({ length: 4096 }, (_, index) => index % 251)), digest = createHash('sha256').update(bytes).digest('hex')
  const endpoint = `/api/firmware/packages/${packageId}`
  const jobEndpoint = '/api/firmware/jobs/fixture-job'
  let uploaded = false, prepared = false, simulated = false, approved = false, liveRequests = 0
  const pkg = { id: packageId, name: 'Fixture package', versionLabel: 'A001', artifactChecksumHex: digest, artifactSizeBytes: bytes.length,
    metadataJson: '{}', hardwareValidationNotice: 'Fixture policy', targets: [], rules: [] }
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login') return route.fulfill({ json: { token, role: 'Admin', displayName: 'Fixture admin' } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/firmware/execution-policy') return route.fulfill({ json: { allowLiveFlashing: true, liveDlaEnabled: true,
      transportMode: 'simulation', liveTransportConfigured: false, incomingCodeServerListening: true, hardwareValidationNotice: 'Fixture policy' } })
    if (url.pathname === endpoint) return route.fulfill({ json: { ...pkg, primaryArtifactId: uploaded ? artifactId : null,
      artifacts: uploaded ? [{ id: artifactId, kind: 'primary', sha256Hex: digest, sizeBytes: bytes.length, storageRef: 'firmware/fixture.bin', payloadPresent: true }] : [] } })
    if (url.pathname === `${endpoint}/artifacts/upload`) {
      expect(request.method()).toBe('POST')
      expect(request.headers()['content-type']).toBe('application/octet-stream')
      expect(request.postDataBuffer()).toEqual(bytes); uploaded = true
      return route.fulfill({ status: 201, json: { id: artifactId, sha256Hex: digest, sizeBytes: bytes.length, payloadPresent: true } })
    }
    if (url.pathname === '/api/firmware/packages') return route.fulfill({ json: [pkg] })
    if (url.pathname === '/api/terminals') return route.fulfill({ json: [{ id: phoneId, displayName: 'Fixture phone' }] })
    if (url.pathname === '/api/firmware/jobs') return route.fulfill({ json: prepared ? [{ id: 'fixture-job', terminalId: phoneId, firmwarePackageId: packageId,
      firmwareArtifactId: artifactId, simulationMode: false, status: 2, approvedAtUtc: null }] : [] })
    if (url.pathname === `/api/terminals/${phoneId}/firmware-jobs` && request.method() === 'POST') {
      expect(request.postDataJSON()).toEqual({ firmwarePackageId: packageId, firmwareArtifactId: artifactId, simulationMode: false })
      prepared = true; return route.fulfill({ status: 201, json: { id: 'fixture-job' } })
    }
    if (url.pathname === jobEndpoint && request.method() === 'GET') return route.fulfill({ json: {
      id: 'fixture-job', terminalId: phoneId, firmwarePackageId: packageId, firmwareArtifactId: artifactId,
      status: simulated ? 4 : 2, simulationMode: false, liveExecutionConfigured: false, incomingCodeServerListening: true, safetyStateJson: '{}',
      approvedAtUtc: approved ? '2026-01-01T00:00:00Z' : null, approvedByOperatorId: approved ? 'fixture' : '',
      codeTransitionId: approved ? 'fixture-transition' : null, codeScheduleId: approved ? 'fixture-schedule' : null,
      cancelReason: '', createdAtUtc: '2026-01-01T00:00:00Z', steps: [], safetyChecks: [], rollBackPlan: null,
      hardwareValidationNotice: 'Fixture policy' } })
    if (url.pathname === `${jobEndpoint}/simulate`) {
      expect(request.method()).toBe('POST'); simulated = true
      return route.fulfill({ json: { status: 4 } })
    }
    if (url.pathname === `${jobEndpoint}/code-transitions`) return route.fulfill({ json: [
      { id: 'fixture-transition', codeScheduleId: 'fixture-schedule', sourceControllerRom: 'LABOLD',
        sourceControllerSoftware: 'A001', targetControllerRom: 'LABNEW', targetControllerSoftware: 'A002' }] })
    if (url.pathname === `${jobEndpoint}/approve`) {
      expect(request.method()).toBe('POST')
      expect(request.postDataJSON()).toMatchObject({ codeTransitionId: 'fixture-transition', rollbackNotes: expect.any(String) })
      approved = true; return route.fulfill({ json: { approved: true } })
    }
    if (url.pathname.endsWith('/execute-live')) liveRequests++
    if (request.method() === 'GET') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/firmware/packages/${packageId}`)
  await page.getByLabel('Primary artifact file').setInputFiles({ name: 'fixture.bin', mimeType: 'application/octet-stream', buffer: bytes })
  await page.getByRole('button', { name: 'Upload primary artifact' }).click()
  await expect(page.getByText('File present · Primary')).toBeVisible()
  expect(uploaded).toBe(true)
  await page.goto('/firmware/jobs')
  const prepare = page.getByRole('checkbox', { name: /Prepare for live execution/ })
  await expect(prepare).not.toBeChecked()
  await prepare.check()
  await expect(page.getByText(/incoming code server is listening for approved scheduled callbacks/)).toBeVisible()
  await expect(page.getByLabel('Registered artifact')).toHaveValue(artifactId)
  await page.getByRole('button', { name: 'Prepare live-capable job' }).click()
  await expect(page.getByRole('button', { name: 'Queue simulation job' })).toBeVisible()
  expect(prepared).toBe(true)
  await page.goto('/firmware/jobs/fixture-job')
  await page.getByRole('button', { name: 'Run simulation' }).click()
  await page.getByLabel('Delivery configuration').selectOption('fixture-transition')
  await page.getByRole('button', { name: 'Approve (after simulation)' }).click()
  await expect(page.getByText(/Approval bound to terminal-initiated delivery/)).toContainText('fixture-schedule')
  await expect(page.getByText(/Waiting for the scheduled terminal callback/)).toBeVisible()
  await expect(page.getByRole('button', { name: /Execute live/ })).toHaveCount(0)
  expect(simulated).toBe(true); expect(approved).toBe(true); expect(liveRequests).toBe(0)
})
