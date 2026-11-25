import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('registers an observed Desk profile, chooses an ambiguous editor explicitly and saves one field', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-observed-profile'
  const profileId = 'msr17-desk-compact-uncompressed'
  const profileLabel = 'MSR 1.7 Desk, compact screening and uncompressed LCD'
  const firmwareId = '00000000-0000-4000-8000-000000000317'
  const setId = '00000000-0000-4000-8000-000000000318'
  const nativeProfiles = ['MTR 1.9 packed', 'Later packed profile']
  const evidence = 'The retained lab set was acknowledged during a successful emulated cold INSTALL. Other record lengths, native field meanings and physical hardware remain unverified.'
  let registered = false, revision = 1, display = 'false'
  const registrations: Record<string, unknown>[] = [], saves: Record<string, unknown>[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'firmware-registry': 5, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/firmware/table-profiles') return route.fulfill({ json: [{ id: profileId, label: profileLabel,
      observedLengthsOnly: true, completeCatalogue: false, evidenceNote: evidence,
      verifiedLengths: { 24: 721, 26: 72 }, terminalAcknowledgedRecords: [24, 26] }] })
    if (url.pathname === '/api/firmware/versions') {
      if (request.method() === 'POST') {
        registrations.push(request.postDataJSON()); registered = true
        return route.fulfill({ status: 201, json: { id: firmwareId } })
      }
      return route.fulfill({ json: registered ? [{ id: firmwareId, label: 'Synthetic Desk', buildId: null, tableProfileId: profileId }] : [] })
    }
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: {
      id: setId, name: 'Observed Desk draft', status: 0, revision: 1, customerId: null, isDefault: false,
      publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: firmwareId,
      targetFirmware: 'Synthetic Desk', tableProfile: profileLabel,
      tableProfileEvidence: { recordCount: 2, terminalAcknowledgedCount: 2, completeCatalogue: false, observedLengthsOnly: true, evidenceNote: evidence },
      firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-def', definitionName: 'Feature and call options',
        tableNumber: 26, tableRevision: revision, sortOrder: 1, validationPassed: true,
        payloadSha256Hex: String(revision).repeat(64), payloadLengthBytes: 72, warnings: [] }],
    } })
    if (url.pathname === `/api/tables/sets/${setId}/records/26/fields`) {
      if (request.method() === 'PUT') {
        const body = request.postDataJSON(); saves.push(body); display = body.changes.displayNumber; revision++
        return route.fulfill({ status: 204 })
      }
      if (!url.searchParams.has('profile')) return route.fulfill({ json: { supported: false,
        profiles: nativeProfiles, observedLengthsOnly: true, reason: 'A record length alone does not determine the layout.' } })
      return route.fulfill({ json: { supported: true, editable: true, profile: url.searchParams.get('profile'), profiles: nativeProfiles,
        observedLengthsOnly: true, length: 72, revision, sha256: String(revision).repeat(64),
        sections: [{ key: 'display', label: 'Display and advertising', fields: [{
          key: 'displayNumber', label: 'Display called number', value: display, editor: 'checkbox', min: 0,
          max: 1, maxLength: null, choices: null, help: null,
        }] }] } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto('/firmware/versions')
  await page.getByLabel('Firmware label').fill('Synthetic Desk')
  await page.getByLabel('Table layout').selectOption(profileId)
  await expect(page.getByText(/Observed subset only; ambiguous native layouts require explicit selection/)).toBeVisible()
  await page.getByRole('button', { name: 'Register firmware' }).click()
  await expect(page.getByRole('cell', { name: 'Synthetic Desk', exact: true })).toBeVisible()
  expect(registrations).toEqual([{ label: 'Synthetic Desk', buildId: null, tableProfileId: profileId }])

  await page.goto(`/table-sets/${setId}?record=26`)
  await expect(page.getByText(/Observed subset only; the full catalogue and ambiguous native layouts are not established/)).toBeVisible()
  await expect(page.getByLabel('Firmware profile')).toHaveValue('')
  await expect(page.getByLabel('Display called number')).toHaveCount(0)
  await page.getByLabel('Firmware profile').selectOption(nativeProfiles[0])
  await expect(page.getByText(/Observed firmware profile: this editor is a candidate/)).toBeVisible()
  await page.getByLabel('Display called number').check()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saves).toHaveLength(1)
  expect(saves[0]).toMatchObject({ profile: nativeProfiles[0], changes: { displayNumber: 'true' } })
  await page.reload()
  await expect(page.getByLabel('Firmware profile')).toHaveValue('')
  await page.getByLabel('Firmware profile').selectOption(nativeProfiles[0])
  await expect(page.getByLabel('Display called number')).toBeChecked()
})
