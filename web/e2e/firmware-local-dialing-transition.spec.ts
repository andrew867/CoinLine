import { test, expect } from '@playwright/test'
import { signIn } from './auth'

test('reviews local rate equivalence and area replacement without assigning the prepared draft', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture needs a local base URL.')
  const origin = new URL(baseURL).origin
  const phone = 'fixture-phone', firmware = 'fixture-target', candidate = 'fixture-candidate', draft = 'fixture-local-draft'
  const session = 'fixture-local-operator'
  const writes: { path: string; body: unknown }[] = []
  const rate = { index: 5, classification: 2, initialPeriod: -1, initialCharge: 25, additionalPeriod: 0, additionalCharge: 0 }
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
    if (url.pathname === '/api/firmware/versions') return route.fulfill({ json: [{ id: firmware, label: 'Later layout', tableProfileId: 'later' }] })
    if (url.pathname === '/api/tables/sets') return route.fulfill({ json: [{ id: candidate, name: 'Target template', status: 0, targetFirmwareVersionId: firmware }] })
    if (url.pathname === '/api/firmware/transition-review') return route.fulfill({ json: {
      sourceFirmwareLabel: 'Earlier layout', targetFirmwareLabel: 'Later layout', sourceSetId: 'fixture-source', sourceSetName: 'Installed assignment',
      candidateSetId: candidate, candidateSetName: 'Target template', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], records: [
        { tableNumber: 73, sourceBytes: 1192, targetExpectedBytes: 1192, candidateBytes: 1192, action: 'review-native-layout', detail: 'Review local rates.', targetAcknowledged: true },
        { tableNumber: 101, sourceBytes: 403, targetExpectedBytes: null, candidateBytes: null, action: 'retire-from-target', detail: 'Convert the earlier segment.' },
        { tableNumber: 136, sourceBytes: null, targetExpectedBytes: 203, candidateBytes: 203, action: 'candidate-addition', detail: 'Prepare the target segment.' }] } })
    if (url.pathname === '/api/firmware/transition-local-dialing/preview') {
      const body = request.postDataJSON(), targetIndex = body.rateChoices['5'] ?? 1, targetRecord = body.areaChoices['101'] ?? 136
      return route.fulfill({ json: { recordCount: 5, canCreate: true, rates: [{ sourceIndex: 5, exchangeCount: 800, targetOptions: [1, 2], targetIndex, sourceTariff: rate }],
        targetTariffs: Array.from({ length: 4 }, (_, index) => ({ ...rate, index })),
        areas: [{ sourceRecord: 101, areaCode: '205', targetOptions: [136, 138], targetRecord, replacesExistingArea: false, ratesMatched: true, changedExchangeCodes: 800, changedRateTuples: 800 }],
        changedRecords: [targetRecord], preservedTargetRecords: targetRecord === 138 ? [136, 137] : [137, 138], findings: [],
        previewToken: targetIndex === 2 && targetRecord === 138 ? 'reviewed-local' : 'suggested-local' } })
    }
    if (request.method() !== 'GET') writes.push({ path: url.pathname, body: request.postDataJSON() })
    if (url.pathname === '/api/firmware/transition-local-dialing' && request.method() === 'POST') {
      const body = request.postDataJSON()
      return body.previewToken === 'reviewed-local' && body.rateChoices['5'] === 2 && body.areaChoices['101'] === 138
        ? route.fulfill({ status: 201, json: { id: draft, changedRecords: [138] } }) : route.fulfill({ status: 409 })
    }
    return route.fulfill({ status: 404 })
  })
  await signIn(page); await page.goto('/firmware/jobs')
  await page.getByRole('button', { name: 'Review a firmware transition' }).click()
  await page.getByLabel('Source phone').selectOption(phone); await page.getByLabel('Target firmware').selectOption(firmware)
  await page.getByLabel('Prepared target configuration').selectOption(candidate)
  await page.getByRole('button', { name: 'Compare configurations' }).click()
  const region = page.getByRole('region', { name: 'Carry forward local dialing' })
  await region.getByLabel('New local dialing draft name').fill('Mapped local draft')
  await region.getByRole('button', { name: 'Preview local dialing carry-forward' }).click()
  await expect(region.getByText('Fixed local charge: 25¢ / Unlimited, then 0¢ / 0 s', { exact: true })).toBeVisible()
  await expect(region.getByText('205', { exact: true })).toBeVisible(); await expect(region.getByText('800 / 800')).toBeVisible()
  await region.getByLabel('Local target rate for source 5').selectOption('2')
  await region.getByLabel('Target segment for MT101').selectOption('138')
  await expect(region.getByRole('button', { name: 'Create local dialing target draft' })).toHaveCount(0)
  await region.getByRole('button', { name: 'Preview local dialing carry-forward' }).click()
  await expect(region.getByText('Target local records retained unchanged: MT136, MT137.')).toBeVisible()
  await page.screenshot({ path: test.info().outputPath('review.png') })
  page.once('dialog', dialog => dialog.accept())
  await region.getByRole('button', { name: 'Create local dialing target draft' }).click()
  await expect(region.getByRole('link', { name: 'Open local dialing draft' })).toHaveAttribute('href', `/table-sets/${draft}`)
  expect(writes).toEqual([{ path: '/api/firmware/transition-local-dialing', body: { terminalId: phone, targetFirmwareVersionId: firmware,
    candidateSetId: candidate, name: 'Mapped local draft', rateChoices: { '5': 2 }, areaChoices: { '101': 138 }, previewToken: 'reviewed-local' } }])
  await expect(page.getByText('Remote upgrade is not qualified.', { exact: false })).toBeVisible()
  await region.getByRole('button', { name: 'Use as target candidate for further review' }).click()
  await expect(page.getByLabel('Prepared target configuration')).toHaveValue(draft)
})
