import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('reviews shared controls without automatically selecting validator hardware or changing a phone', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local console fixture origin is required.')
  const origin = new URL(baseURL).origin
  const phone = '00000000-0000-4000-8000-000000004801', firmware = '00000000-0000-4000-8000-000000004802'
  const candidate = '00000000-0000-4000-8000-000000004803', draft = '00000000-0000-4000-8000-000000004804'
  const session = 'fixture-common-operator'
  const writes: { path: string; body: unknown }[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login') return route.fulfill({ json: { token: session, displayName: 'Fixture operator', role: 'Operator',
      permissions: { overview: 1, 'firmware-jobs': 1, 'firmware-registry': 1, phones: 1, 'table-sets': 5, 'table-records': 1, 'table-assignments': 1 } } })
    if (request.headers().authorization !== `Bearer ${session}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals') return route.fulfill({ json: [{ id: phone, displayName: 'Fixture payphone' }] })
    if (url.pathname === '/api/firmware/jobs' || url.pathname === '/api/firmware/packages') return route.fulfill({ json: [] })
    if (url.pathname === '/api/firmware/execution-policy') return route.fulfill({ json: { allowLiveFlashing: false, liveDlaEnabled: false } })
    if (url.pathname === '/api/firmware/versions') return route.fulfill({ json: [{ id: firmware, label: 'Later target layout', tableProfileId: 'mtr211-multipay-180-double-compressed' }] })
    if (url.pathname === '/api/tables/sets') return route.fulfill({ json: [{ id: candidate, name: 'Target template', status: 0, targetFirmwareVersionId: firmware }] })
    if (url.pathname === '/api/firmware/transition-review') return route.fulfill({ json: {
      sourceFirmwareLabel: 'Earlier installed layout', targetFirmwareLabel: 'Later target layout', sourceSetId: 'fixture-source', sourceSetName: 'Installed assignment',
      sourceProfileId: 'mtr20-early-multipay-200-single-compressed', targetProfileId: 'mtr211-multipay-180-double-compressed',
      candidateSetId: candidate, candidateSetName: 'Target template', candidatePublished: false, activeSessions: 0, outstandingDownloads: 0,
      configurationReadyForReview: false, upgradeQualified: false, findings: [], records: [
        { tableNumber: 50, sourceBytes: 105, targetExpectedBytes: 105, candidateBytes: 105, action: 'candidate-review', detail: 'Review coin controls.' },
      ] } })
    if (url.pathname === '/api/firmware/transition-common-controls/preview') {
      const body = request.postDataJSON()
      const selected = JSON.stringify(body.selections) === JSON.stringify(['coin-denominations'])
      const ready = selected && body.reviewed
      return route.fulfill({ json: { sourceSetId: 'fixture-source', candidateSetId: candidate, recordCount: 1, canCreate: ready,
        changedBytes: selected ? 2 : 0, previewToken: ready ? 'reviewed-common' : 'initial-common',
        findings: ready ? [] : [{ blocking: true, number: 0, message: 'Choose and review controls.' }], records: [
          { number: 50, status: 'available', changedBytes: selected ? 2 : 0, groups: [
            { group: { id: 'coin-denominations', number: 50, label: 'Coin values and acceptance', note: 'Independent denomination control.' },
              selected, fields: [{ key: 'coin', label: 'Coin value', source: '25', target: '5', status: 'different' }] },
            { group: { id: 'coin-hardware', number: 50, label: 'Validator windows and physical capacities', note: 'Retain hardware.' },
              selected: false, fields: [{ key: 'window', label: 'Validator calibration', source: '18', target: '12', status: 'different' }] },
          ] },
        ] } })
    }
    if (request.method() !== 'GET') writes.push({ path: url.pathname, body: request.postDataJSON() })
    if (url.pathname === '/api/firmware/transition-common-controls' && request.method() === 'POST')
      return request.postDataJSON().previewToken === 'reviewed-common' ? route.fulfill({ status: 201, json: { id: draft } }) : route.fulfill({ status: 409 })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto('/firmware/jobs')
  await page.getByRole('button', { name: 'Review a firmware transition' }).click()
  await page.getByLabel('Source phone').selectOption(phone)
  await page.getByLabel('Target firmware').selectOption(firmware)
  await page.getByLabel('Prepared target configuration').selectOption(candidate)
  await page.getByRole('button', { name: 'Compare configurations' }).click()
  const region = page.getByRole('region', { name: 'Carry forward common configuration controls' })
  await region.getByRole('button', { name: 'Preview common configuration carry-forward' }).click()
  await expect(region.getByText('0 groups selected.')).toBeVisible()
  await region.getByLabel('Coin values and acceptance').check()
  await expect(region.getByLabel('Validator windows and physical capacities')).not.toBeChecked()
  await region.getByLabel('I reviewed the selected controls and target hardware').check()
  await expect(region.getByRole('button', { name: 'Create common configuration target draft' })).toHaveCount(0)
  await region.getByRole('button', { name: 'Preview common configuration carry-forward' }).click()
  await expect(region.getByText('2 record bytes would change.', { exact: false })).toBeVisible()
  expect(writes).toEqual([])
  page.once('dialog', dialog => dialog.accept())
  await region.getByRole('button', { name: 'Create common configuration target draft' }).click()
  await expect(region.getByRole('link', { name: 'Open common configuration draft' })).toHaveAttribute('href', `/table-sets/${draft}`)
  expect(writes).toEqual([{ path: '/api/firmware/transition-common-controls', body: { terminalId: phone, targetFirmwareVersionId: firmware,
    candidateSetId: candidate, name: 'Reviewed common configuration transition', selections: ['coin-denominations'], reviewed: true, previewToken: 'reviewed-common' } }])
  await expect(page.getByText('Remote upgrade is not qualified.', { exact: false })).toBeVisible()
})
