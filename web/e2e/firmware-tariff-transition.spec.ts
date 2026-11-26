import { test, expect } from '@playwright/test'
import { signIn } from './auth'

test('reviews price tuples and creates only the explicitly reviewed unassigned tariff draft', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture needs a local base URL.')
  const origin = new URL(baseURL).origin
  const phone = '00000000-0000-4000-8000-000000004301', firmware = '00000000-0000-4000-8000-000000004302'
  const candidate = '00000000-0000-4000-8000-000000004303', draft = '00000000-0000-4000-8000-000000004304'
  const session = 'fixture-tariff-operator'
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
      sourceProfileId: 'mtr20-early-multipay-200-single-compressed', targetProfileId: 'mtr211-multipay-180-double-compressed',
      candidateSetId: candidate, candidateSetName: 'Target template', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], records: [
        { tableNumber: 73, sourceBytes: 1192, targetExpectedBytes: 1192, candidateBytes: 1192, action: 'review-native-layout',
          detail: 'Review the target tariff rows.', targetAcknowledged: true, requiresLayoutReview: true },
        { tableNumber: 92, sourceBytes: 3401, targetExpectedBytes: 3061, candidateBytes: null, action: 'replace-required',
          detail: 'Prepare screening.', targetAcknowledged: true, requiresLayoutReview: false }] } })
    if (url.pathname === '/api/firmware/transition-tariffs/preview') {
      const body = request.postDataJSON(), targetIndex = body.choices['0'] ?? 7
      return route.fulfill({ json: { sourceRows: 128, targetRows: 128, recordCount: 4, changedPositions: [targetIndex], canCreate: true,
        groups: [{ sourceIndex: 0, sourceIndexes: [0, 2], classification: 2, initialPeriod: -1, initialCharge: 25, additionalPeriod: 0,
          additionalCharge: 0, targetOptions: [7, 8, 9], targetIndex, action: 'allocate', replacedClassification: 4 },
        { sourceIndex: 1, sourceIndexes: [1, ...Array.from({ length: 125 }, (_, i) => i + 3)], classification: 4, initialPeriod: 0, initialCharge: 0,
          additionalPeriod: 0, additionalCharge: 0, targetOptions: [0, 1, 2, 3], targetIndex: 0, action: 'reuse', replacedClassification: null }],
        candidateReview: { protectedPositions: [{ index: 4, recordNumbers: [92] }], reviewedRecords: [73, 92], missingReferenceRecords: [55], unreviewedRecords: [] },
        findings: [{ blocking: false, sourceIndex: null, message: 'Review subsequently added reference records.' }],
        previewToken: targetIndex === 9 ? 'reviewed-rate' : 'suggested-rate' } })
    }
    if (request.method() !== 'GET') writes.push({ path: url.pathname, body: request.postDataJSON() })
    if (url.pathname === '/api/firmware/transition-tariffs' && request.method() === 'POST') {
      const body = request.postDataJSON()
      return body.previewToken === 'reviewed-rate' && body.choices['0'] === 9
        ? route.fulfill({ status: 201, json: { id: draft, changedPositions: [9] } }) : route.fulfill({ status: 409 })
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
  const region = page.getByRole('region', { name: 'Carry forward terminal tariffs' })
  await region.getByLabel('New tariff draft name').fill('Mapped tariff draft')
  await region.getByRole('button', { name: 'Preview tariff carry-forward' }).click()
  await expect(region.getByText('128 source rows, 2 distinct tariff definitions.')).toBeVisible()
  const fixed = region.getByRole('row').filter({ hasText: 'Fixed local charge' })
  await expect(fixed.getByText('25¢ / Unlimited')).toBeVisible()
  await expect(fixed.getByText('Allocate empty Invalid number')).toBeVisible()
  await region.getByText('Protected target indexes (1)', { exact: true }).click()
  await expect(region.getByText('Rate 4: MT92')).toBeVisible()
  const selection = region.getByLabel('Target tariff for source 0')
  await expect(selection).toHaveValue('7'); await expect(selection.getByRole('option', { name: '4', exact: true })).toHaveCount(0)
  await selection.selectOption('9')
  await expect(region.getByRole('button', { name: 'Create tariff target draft' })).toHaveCount(0)
  await region.getByRole('button', { name: 'Preview tariff carry-forward' }).click()
  await expect(region.getByRole('button', { name: 'Create tariff target draft' })).toBeEnabled()
  await page.screenshot({ path: test.info().outputPath('review.png') })
  page.once('dialog', dialog => dialog.accept())
  await region.getByRole('button', { name: 'Create tariff target draft' }).click()
  await expect(region.getByRole('link', { name: 'Open tariff draft' })).toHaveAttribute('href', `/table-sets/${draft}`)
  expect(writes).toEqual([{ path: '/api/firmware/transition-tariffs', body: { terminalId: phone, targetFirmwareVersionId: firmware,
    candidateSetId: candidate, name: 'Mapped tariff draft', choices: { '0': 9 }, previewToken: 'reviewed-rate' } }])
  await expect(page.getByText('Remote upgrade is not qualified.', { exact: false })).toBeVisible()
  await region.getByRole('button', { name: 'Use as target candidate for further review' }).click()
  await expect(page.getByLabel('Prepared target configuration')).toHaveValue(draft)
})
