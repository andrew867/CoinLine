import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('reviews mapped prompt text and creates only the selected unassigned target language draft', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local console fixture origin is required.')
  const origin = new URL(baseURL).origin
  const phone = '00000000-0000-4000-8000-000000004601', firmware = '00000000-0000-4000-8000-000000004602'
  const candidate = '00000000-0000-4000-8000-000000004603', draft = '00000000-0000-4000-8000-000000004604'
  const session = 'fixture-prompt-operator'
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
        { tableNumber: 27, sourceBytes: 2801, targetExpectedBytes: null, candidateBytes: null, action: 'candidate-family-replacement', detail: 'Review replacement.', replacementRecords: [85] },
        { tableNumber: 85, sourceBytes: null, targetExpectedBytes: 8001, candidateBytes: 8001, action: 'candidate-addition', detail: 'Prepared later language.' },
      ] } })
    if (url.pathname === '/api/firmware/transition-prompts/preview') {
      const body = request.postDataJSON()
      const selected = JSON.stringify(body.selections) === JSON.stringify([{ sourceNumber: 27, messageIds: [28] }])
      const ready = selected && body.reviewed
      return route.fulfill({ json: { sourceSetId: 'fixture-source', candidateSetId: candidate, recordCount: 2, canCreate: ready,
        changedBytes: selected ? 20 : 0, previewToken: ready ? 'reviewed-prompt' : 'initial-prompt',
        findings: ready ? [] : [{ blocking: true, message: 'Choose and review the selected messages.' }], pairs: [
          { sourceNumber: 27, targetNumber: 85, status: 'available', changedBytes: selected ? 20 : 0, messages: [
            { sourceMessage: 0, targetMessage: null, sourceLabel: 'Earlier message', targetLabel: null, sourceLines: ['Unmapped', ''], targetLines: [], status: 'unmapped', eligible: false, selected: false, potentialChangedBytes: 0 },
            { sourceMessage: 28, targetMessage: 70, sourceLabel: 'Emergency service', targetLabel: 'Emergency service', sourceLines: ['Emergency only', ''],
              targetLines: ['Old emergency', 'Extra wording'], status: 'different', eligible: true, selected, potentialChangedBytes: 20 },
          ] }, { sourceNumber: 28, targetNumber: 86, status: 'missing-target', messages: [], changedBytes: 0 },
        ] } })
    }
    if (request.method() !== 'GET') writes.push({ path: url.pathname, body: request.postDataJSON() })
    if (url.pathname === '/api/firmware/transition-prompts' && request.method() === 'POST')
      return request.postDataJSON().previewToken === 'reviewed-prompt' ? route.fulfill({ status: 201, json: { id: draft, changedBytes: 20 } }) : route.fulfill({ status: 409 })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto('/firmware/jobs')
  await page.getByRole('button', { name: 'Review a firmware transition' }).click()
  await page.getByLabel('Source phone').selectOption(phone)
  await page.getByLabel('Target firmware').selectOption(firmware)
  await page.getByLabel('Prepared target configuration').selectOption(candidate)
  await page.getByRole('button', { name: 'Compare configurations' }).click()
  const region = page.getByRole('region', { name: 'Carry forward visual prompt configuration' })
  await region.getByRole('button', { name: 'Preview visual prompt carry-forward' }).click()
  await expect(region.getByText('70 · Emergency service')).toBeVisible()
  await expect(region.getByText('Prepared target language record missing or incompatible.')).toBeVisible()
  await expect(region.getByLabel('Carry MT27 message 0')).toBeDisabled()
  await region.getByLabel('Carry MT27 message 28').check()
  await region.getByLabel('I reviewed the selected text and target message positions').check()
  await expect(region.getByRole('button', { name: 'Create visual prompt target draft' })).toHaveCount(0)
  await region.getByRole('button', { name: 'Preview visual prompt carry-forward' }).click()
  await expect(region.getByText('20 prompt cell bytes would change.', { exact: false })).toBeVisible()
  await expect(region.getByRole('button', { name: 'Create visual prompt target draft' })).toBeEnabled()
  expect(writes).toEqual([])
  page.once('dialog', dialog => dialog.accept())
  await region.getByRole('button', { name: 'Create visual prompt target draft' }).click()
  await expect(region.getByRole('link', { name: 'Open visual prompt draft' })).toHaveAttribute('href', `/table-sets/${draft}`)
  expect(writes).toEqual([{ path: '/api/firmware/transition-prompts', body: { terminalId: phone, targetFirmwareVersionId: firmware,
    candidateSetId: candidate, name: 'Reviewed visual prompt transition', selections: [{ sourceNumber: 27, messageIds: [28] }], reviewed: true, previewToken: 'reviewed-prompt' } }])
  await expect(page.getByText('Remote upgrade is not qualified.', { exact: false })).toBeVisible()
})
