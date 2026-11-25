import { expect, test } from '@playwright/test'
import { signIn } from './auth'

const phone = {
  id: '00000000-0000-4000-8000-000000000101', siteId: '00000000-0000-4000-8000-000000000102',
  displayName: 'Workshop test phone', terminalIdHex: '0000000123', status: 1,
  terminalGroupId: null, groupName: null, onCall: false, lastSessionAtUtc: null,
}
const failure = 'Link acknowledgement was missing for MT131 fragment.'
const batch = {
  id: '00000000-0000-4000-8000-000000000103', terminalId: phone.id, status: 3, lastError: failure,
}
const download = {
  ...batch, tableSetName: 'Workshop setup', scope: 0, retryCount: 0, diagnosticsJson: null,
  timeline: [
    { id: 'step-1', stepIndex: 1, tableDefinitionName: 'Feature options (MT26)', hostDownloadPhaseName: 'Completed',
      hostDownloadPhase: 9, lastAckStatus: 'ack', itemStatus: 3, errorDetail: null, payloadSha256Hex: 'a'.repeat(64) },
    { id: 'step-2', stepIndex: 2, tableDefinitionName: 'Graphical advertising (MT131)', hostDownloadPhaseName: 'Failed',
      hostDownloadPhase: 10, lastAckStatus: 'missing_link_ack', itemStatus: 4, errorDetail: failure, payloadSha256Hex: 'b'.repeat(64) },
    { id: 'step-3', stepIndex: 3, tableDefinitionName: 'Graphical continuation (MT132)', hostDownloadPhaseName: 'Failed',
      hostDownloadPhase: 10, lastAckStatus: 'not_sent', itemStatus: 4, errorDetail: 'Not sent because the download stopped before this table.', payloadSha256Hex: 'c'.repeat(64) },
  ],
}

test('find the table missing an acknowledgement on a phone’s failed download', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-operator-token'
  const api = new Map<string, unknown>([
    ['/api/operator/dashboard', { terminalsByStatus: [{ status: 1, count: 1 }], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 1, uploadAlerts: { failed: 0, quarantined: 0 } }],
    ['/api/cards/reconciliation-pending', { paymentCount: 0, paymentTotal: 0, callCount: 0 }],
    ['/api/field-events', { total: 0, items: [] }],
    ['/api/sip-modem', { listening: false }],
    ['/api/terminals', [phone]],
    ['/api/terminals/firmware-options', []],
    [`/api/terminals/${phone.id}`, phone],
    [`/api/terminals/${phone.id}/identity`, { terminalIdHex: phone.terminalIdHex, previousId: null, previousType: null, firstContactAtUtc: null }],
    [`/api/terminals/${phone.id}/lifecycle`, { active: true, deactivatedAtUtc: null,
      deletionReviewDate: null, reviewDue: false, defaultDeletionReviewDate: '2026-12-01', revision: 1 }],
    [`/api/terminals/${phone.id}/table-assignment-options`, []],
    [`/api/terminals/${phone.id}/inmate-configuration`, { eligibleFamily: false, featureConfigurationId: null,
      featureConfigurationName: null, setupConfigurationId: null, revision: null, featureConfigurations: [], wireFormatVerified: false }],
    [`/api/terminals/${phone.id}/diagnostics`, { snapshots: [], craftDiagnostics: [], tableReloadRequests: [], cdrUploadRequests: [] }],
    [`/api/operator/terminals/${phone.id}/timeline`, { terminalId: phone.id, items: [] }],
    ['/api/tables/sets', []], ['/api/terminal-groups', []], ['/api/ncc/sessions', []], ['/api/call-records', []],
    ['/api/downloads', [batch]], [`/api/downloads/${batch.id}`, download],
  ])
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    const headers = request.headers()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') {
      const body = request.postDataJSON() as { username: string; password: string }
      if (body.username === 'operator' && body.password === 'lab-operator' && !headers.authorization) {
        return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator' } })
      }
    }
    if (headers.authorization !== `Bearer ${token}` || headers['x-operator-id'] || headers['x-operator-role']) {
      return route.fulfill({ status: 401, json: { error: 'Expected a signed-in operator.' } })
    }
    const body = api.get(url.pathname)
    return body === undefined ? route.fulfill({ status: 404 }) : route.fulfill({ json: body })
  })

  await signIn(page)
  await page.getByRole('link', { name: 'Phones', exact: true }).click()
  await page.getByRole('link', { name: phone.displayName }).click()
  await expect(page.getByRole('heading', { name: phone.displayName })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Terminal identity history' }).getByText(phone.terminalIdHex, { exact: true })).toBeVisible()
  await page.getByTestId('download-status').getByRole('link', { name: 'Failed' }).click()
  await expect(page.getByRole('heading', { name: 'Download batch' })).toBeVisible()
  await expect(page.getByText(phone.id, { exact: true })).toBeVisible()
  await expect(page.getByRole('alert')).toHaveText(failure)

  const missing = page.getByRole('listitem').filter({ hasText: 'Graphical advertising (MT131)' })
  await expect(missing).toContainText('Acknowledgement: Link acknowledgement missing')
  await expect(missing).toContainText(failure)
  const notSent = page.getByRole('listitem').filter({ hasText: 'Graphical continuation (MT132)' })
  await expect(notSent).toContainText('Acknowledgement: Not sent')
  await expect(notSent).toContainText('Not sent because the download stopped before this table.')
  await expect(page.getByRole('listitem').filter({ hasText: 'Feature options (MT26)' })).toContainText('Acknowledgement: Table acknowledged')
})
