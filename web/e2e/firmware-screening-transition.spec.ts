import { test, expect } from '@playwright/test'
import { signIn } from './auth'

test('reviews target screening positions and stages a mapped draft without creating a job', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture needs a local base URL.')
  const origin = new URL(baseURL).origin
  const phone = '00000000-0000-4000-8000-000000004201'
  const firmware = '00000000-0000-4000-8000-000000004202'
  const candidate = '00000000-0000-4000-8000-000000004203'
  const draft = '00000000-0000-4000-8000-000000004204'
  const session = 'fixture-screening-operator'
  const writes: { path: string; body: unknown }[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login') return route.fulfill({ json: { token: session, displayName: 'Workshop operator', role: 'Operator',
      permissions: { overview: 1, 'firmware-jobs': 1, 'firmware-registry': 1, phones: 1, 'table-sets': 5, 'table-records': 1, 'table-assignments': 1 } } })
    if (request.headers().authorization !== `Bearer ${session}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals') return route.fulfill({ json: [{ id: phone, displayName: 'Fixture payphone' }] })
    if (url.pathname === '/api/firmware/jobs' || url.pathname === '/api/firmware/packages') return route.fulfill({ json: [] })
    if (url.pathname === '/api/firmware/execution-policy') return route.fulfill({ json: { allowLiveFlashing: false, liveDlaEnabled: false } })
    if (url.pathname === '/api/firmware/versions') return route.fulfill({ json: [{ id: firmware, label: 'Later target layout', tableProfileId: 'later' }] })
    if (url.pathname === '/api/tables/sets') return route.fulfill({ json: [{ id: candidate, name: 'Target template', status: 0, targetFirmwareVersionId: firmware }] })
    if (url.pathname === '/api/firmware/transition-review') return route.fulfill({ json: {
      sourceFirmwareLabel: 'Earlier installed layout', targetFirmwareLabel: 'Later target layout', sourceSetId: 'fixture-source', sourceSetName: 'Installed assignment',
      candidateSetId: candidate, candidateSetName: 'Target template', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false,
      records: [{ tableNumber: 92, sourceBytes: 3401, targetExpectedBytes: 3061, candidateBytes: 3061,
        action: 'candidate-replacement', detail: 'Review the smaller screening list.', targetAcknowledged: true, requiresLayoutReview: false }], findings: [],
    } })
    if (url.pathname === '/api/firmware/transition-screening/preview') {
      const body = request.postDataJSON()
      const resolved = body.tariffChoices['0'] === 7
      return route.fulfill({ json: { sourceSlots: 200, targetSlots: 180, sourceConfiguredSlots: 2, changedSlots: 3,
        sourceReservedSlots: 1, retainedLaterFlagsSlots: 0, descriptionCount: 1, recordCount: 4,
        changedPositions: [1, 2, 3], clearedTargetPositions: [3], canCreate: resolved,
        references: [{ kind: 'tariff', sourceIndex: 0, slots: [1, 2], options: [4, 7], targetIndex: resolved ? 7 : null }],
        findings: resolved ? [] : [{ blocking: true, slot: null, message: 'Choose the target tariff for source 0 before staging.' }],
        previewToken: resolved ? 'reviewed-reference' : 'incomplete-reference' } })
    }
    if (request.method() !== 'GET') writes.push({ path: url.pathname, body: request.postDataJSON() })
    if (url.pathname === '/api/firmware/transition-screening' && request.method() === 'POST') {
      const body = request.postDataJSON()
      return body.previewToken === 'reviewed-reference' && body.tariffChoices['0'] === 7
        ? route.fulfill({ status: 201, json: { id: draft, changedSlots: 3 } }) : route.fulfill({ status: 409 })
    }
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto('/firmware/jobs')
  await page.getByRole('button', { name: 'Review a firmware transition' }).click()
  await page.getByLabel('Source phone').selectOption(phone)
  await page.getByLabel('Target firmware').selectOption(firmware)
  await page.getByLabel('Prepared target configuration').selectOption(candidate)
  await page.getByRole('button', { name: 'Compare configurations' }).click()
  const region = page.getByRole('region', { name: 'Carry forward screening rules' })
  await region.getByLabel('New screening draft name').fill('Mapped target draft')
  await region.getByRole('button', { name: 'Preview screening carry-forward' }).click()
  await expect(region.getByText('200 source entries to 180 target entries.')).toBeVisible()
  await expect(region.getByText('Choose the target tariff for source 0 before staging.')).toBeVisible()
  await expect(region.getByText('Configured candidate positions becoming vacant (1):')).toBeVisible()
  await region.getByText('Changed target positions', { exact: true }).click()
  await expect(region.getByText('1, 2, 3', { exact: true })).toBeVisible()
  await expect(region.getByRole('button', { name: 'Create screening target draft' })).toHaveCount(0)
  await region.getByLabel('Target tariff for 0').selectOption('7')
  await expect(region.getByText('Preview again to confirm your changes.')).toBeVisible()
  await region.getByRole('button', { name: 'Preview screening carry-forward' }).click()
  await expect(region.getByRole('button', { name: 'Create screening target draft' })).toBeEnabled()
  await page.screenshot({ path: test.info().outputPath('review.png') })
  page.once('dialog', dialog => dialog.accept())
  await region.getByRole('button', { name: 'Create screening target draft' }).click()
  await expect(region.getByRole('link', { name: 'Open screening draft' })).toHaveAttribute('href', `/table-sets/${draft}`)
  expect(writes).toEqual([{ path: '/api/firmware/transition-screening', body: { terminalId: phone, targetFirmwareVersionId: firmware,
    candidateSetId: candidate, name: 'Mapped target draft', tariffChoices: { '0': 7 }, carrierChoices: {}, previewToken: 'reviewed-reference' } }])
  await expect(page.getByText('Remote upgrade is not qualified.', { exact: false })).toBeVisible()
  await region.getByRole('button', { name: 'Use as target candidate for further review' }).click()
  await expect(page.getByLabel('Prepared target configuration')).toHaveValue(draft)
})
