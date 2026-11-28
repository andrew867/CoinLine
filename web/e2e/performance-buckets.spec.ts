import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('previews configured MT37 histogram boundaries, saves selected MT32 timing, and reopens it', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-performance-operator'
  const id = '00000000-0000-4000-8000-000000003201'
  const reportId = '00000000-0000-4000-8000-000000003701'
  const sourceBatch = '00000000-0000-4000-8000-000000003702'
  let revision = 1, start = '100'
  const saves: Record<string, unknown>[] = []
  const field = (key: string, label: string, value: string) => ({ key, label, value, editor: 'number', min: 0,
    max: 32767, maxLength: null, choices: null, help: null })
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3, uploads: 1, downloads: 1 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${id}`) return route.fulfill({ json: {
      id, name: 'Communications statistics draft', status: 0, revision: 1, customerId: null, isDefault: false, publishedAtUtc: null,
      publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
      tableProfile: null, firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition',
        definitionName: 'Communications statistics settings', tableNumber: 32, tableRevision: revision, sortOrder: 1, validationPassed: true,
        payloadSha256Hex: String(revision).repeat(64), payloadLengthBytes: 33, warnings: [] }],
    } })
    if (url.pathname === `/api/tables/sets/${id}/records/32/fields`) {
      if (request.method() === 'PUT') {
        const body = request.postDataJSON(); saves.push(body); start = body.changes['interval-0.start'] ?? start; revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: 'MTR 1.9 packed', length: 33,
        revision, sha256: String(revision).repeat(64), sections: [{ key: 'collection', label: 'Performance reporting', fields: [field('days', 'Collection interval (days)', '1')] },
          ...[[0, 'Access to dial complete', start, '50'], [1, 'Dial complete to carrier', '1000', '0'],
            [3, 'Carrier to first packet', '5', '13107'], [4, 'User waiting time', '-1', '100']].map(([index, label, first, interval]) => ({
            key: `interval-${index}`, label, fields: [field(`interval-${index}.start`, 'Start (10 ms ticks)', String(first)),
              field(`interval-${index}.duration`, 'Interval (10 ms ticks)', String(interval))],
          }))] } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    if (url.pathname === `/api/uploads/${reportId}`) return route.fulfill({ json: {
      id: reportId, terminalId: null, status: 2, idempotencyKey: 'fixture-report', isTerminalStatistics: true,
      rawPayloadHex: '', decodedMetadataJson: '{}', createdAtUtc: '2028-03-01T12:00:00Z', updatedAtUtc: '2028-03-01T12:00:00Z',
      performanceReference: { status: 'acknowledged', tableRevision: 1, downloadBatchId: sourceBatch,
        ackRecordedAtUtc: '2028-03-01T11:30:00Z', laterUncertainAttempt: false,
        timing: [{ measurementIndex: 0, startTicks: 100, intervalTicks: 50 }] },
      records: [{ id: 'fixture-record', terminalStatistics: {
        messageType: 37, name: 'Communications performance', carrierFormat: null, carriers: [],
        periodStart: '2028-02-29T10:00:00', periodEnd: '2028-03-01T10:00:00',
        groups: [{ label: 'Line seizure to end of dialing',
          counters: Array.from({ length: 7 }, (_, i) => ({ label: `Bucket ${i + 1}`, value: 80 + i })) }],
      } }],
    } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${id}?record=32`)
  await page.getByText('Performance histogram bucket ranges (MT37)', { exact: true }).click()
  const range = page.getByRole('region', { name: 'Access to dial complete', exact: true })
  await expect(range.getByText('3.50 s or more')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Dial complete to carrier', exact: true }).getByText('Empty range', { exact: true })).toHaveCount(5)
  await expect(page.getByRole('region', { name: 'Carrier to first packet', exact: true }).getByRole('status')).toContainText('wraps')
  await expect(page.getByRole('region', { name: 'User waiting time', exact: true }).getByRole('status')).toContainText('cannot be calculated')
  await page.getByLabel('Section or entry').selectOption('interval-0')
  await page.getByLabel('Start (10 ms ticks)').fill('150')
  await expect(range.getByText('4.00 s or more')).toBeVisible()
  await page.getByLabel('Section or entry').selectOption('collection')
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saves).toHaveLength(1)
  expect(saves[0]).toMatchObject({ revision: 1, sha256: '1'.repeat(64), changes: { 'interval-0.start': '150' } })
  expect(saves[0].changes).toEqual({ 'interval-0.start': '150' })
  await page.reload()
  await page.getByText('Performance histogram bucket ranges (MT37)', { exact: true }).click()
  await expect(range.getByText('4.00 s or more')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Dial complete to carrier', exact: true }).getByText('Empty range', { exact: true })).toHaveCount(5)

  await page.goto(`/uploads/${reportId}`)
  const reference = page.getByRole('region', { name: 'MT32 timing reference' })
  await expect(reference).toContainText('Revision 1')
  await expect(reference).toContainText('not proven historical bucket boundaries')
  await expect(reference.getByRole('link', { name: 'View source download' })).toHaveAttribute('href', `/downloads/${sourceBatch}`)
  const reportGroup = page.getByRole('heading', { name: 'Line seizure to end of dialing' }).locator('..')
  await expect(reportGroup.getByText('Reference: Below 1.00 s', { exact: true })).toBeVisible()
  await expect(reportGroup.getByText('Reference: 3.50 s or more', { exact: true })).toBeVisible()
  await expect(reportGroup.getByText('86', { exact: true })).toBeVisible()
  await expect(reportGroup.getByText('Reference: 4.00 s or more', { exact: true })).toHaveCount(0)
})

test('compares graphical usage owners with independently acknowledged records', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-graphical-report-operator'
  const id = '00000000-0000-4000-8000-000000015301'
  const batch = '00000000-0000-4000-8000-000000015302'
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login') return route.fulfill({ json: { token, displayName: 'Fixture operator',
      role: 'Operator', permissions: { overview: 1, uploads: 1, downloads: 1, 'table-records': 1 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === `/api/uploads/${id}`) return route.fulfill({ json: {
      id, terminalId: 'fixture-graphical-phone', status: 2, idempotencyKey: 'fixture-report', isTerminalStatistics: true,
      rawPayloadHex: '', decodedMetadataJson: '{}', relatedDlogTransactionId: null,
      createdAtUtc: '2028-04-01T12:00:00Z', updatedAtUtc: '2028-04-01T12:00:00Z',
      graphicalReferences: [
        { status: 'acknowledged', recordNumber: 131, collectionDurationDays: 7, laterUncertainAttempt: true,
          screens: [], downloadBatchId: batch, tableRevision: 2, ackRecordedAtUtc: '2028-04-01T11:00:00Z' },
        { status: 'missing', recordNumber: 132, screens: null },
        { status: 'acknowledged', recordNumber: 133, collectionDurationDays: null, laterUncertainAttempt: false,
          screens: [{ screenNumber: 21, recordNumber: 133, kindCode: 77, screenOwnerCode: 1234,
            keyOwnerCodes: [2000, 0, 0, 0, 0, 9000] }] },
      ],
      records: [{ id: 'fixture-record', terminalStatistics: { messageType: 153, name: 'Graphical usage statistics',
        periodStart: null, periodEnd: null, groups: [], carriers: [], carrierFormat: null,
        graphicalScreens: [{ screenNumber: 21, ownerCode: 1234, directionKeyPressCount: 3,
          keys: [{ keyNumber: 1, ownerCode: 2000, pressCount: 12 }, { keyNumber: 6, ownerCode: 2005, pressCount: 4 }] },
          { screenNumber: 9, ownerCode: 456, directionKeyPressCount: 0, keys: [] }] } }],
    } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/uploads/${id}`)
  const reference = page.getByRole('region', { name: 'Acknowledged graphical configuration reference' })
  await expect(reference.getByText(/Stored collection duration: 7 days/)).toBeVisible()
  await expect(reference.getByRole('link', { name: 'Download evidence' })).toHaveAttribute('href', `/downloads/${batch}`)
  await expect(reference.getByText(/later attempt has no confirmed/)).toBeVisible()
  const table = page.getByRole('table', { name: 'Graphical usage by screen' })
  await expect(table.getByText('Reference 1234, matches', { exact: true })).toBeVisible()
  await expect(table.getByText('Reference 9000, differs', { exact: true })).toBeVisible()
  await expect(table.getByText('No acknowledged screen reference', { exact: true })).toBeVisible()
  await expect(reference.getByText(/not the current assignment/)).toBeVisible()
})

test('reviews Audio-Gram collection and surcharges separately from received counters', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-messaging-report-operator'
  const id = '00000000-0000-4000-8000-000000006901'
  const batch = '00000000-0000-4000-8000-000000006802'
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login') return route.fulfill({ json: { token, displayName: 'Fixture operator',
      role: 'Operator', permissions: { overview: 1, uploads: 1, downloads: 1, 'table-records': 1 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === `/api/uploads/${id}`) return route.fulfill({ json: {
      id, terminalId: 'fixture-messaging-phone', status: 2, idempotencyKey: 'fixture-report', isTerminalStatistics: true,
      rawPayloadHex: '', decodedMetadataJson: '{}', relatedDlogTransactionId: null,
      createdAtUtc: '2028-04-01T12:00:00Z', updatedAtUtc: '2028-04-01T12:00:00Z',
      messagingReference: { status: 'acknowledged', downloadBatchId: batch, tableRevision: 2,
        ackRecordedAtUtc: '2028-04-01T11:40:00Z', laterUncertainAttempt: true,
        settings: { serviceMode: 1, callRecordMode: 0, collectStatistics: true, allowedPayments: ['Coins'], areas: [
          { area: 'Local', allowed: true, surchargeCents: 25 }, { area: 'Intra-area toll', allowed: false, surchargeCents: 50 },
          { area: 'Inter-area toll', allowed: false, surchargeCents: 0 }, { area: 'International', allowed: false, surchargeCents: 0 },
        ] } },
      records: [{ id: 'fixture-record', terminalStatistics: { messageType: 69, name: 'Messaging statistics',
        periodStart: '2028-04-01T06:00:00', periodEnd: '2028-04-01T11:00:00', carrierFormat: null, carriers: [],
        groups: [{ label: 'Messaging selected after no answer', counters: [{ label: 'Local', value: 17 }] }] } }],
    } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/uploads/${id}`)
  const reference = page.getByRole('region', { name: 'MT68 Audio-Gram configuration reference' })
  await expect(reference).toContainText('Revision 2')
  await expect(reference.getByRole('link', { name: 'View source download' })).toHaveAttribute('href', `/downloads/${batch}`)
  await expect(reference).toContainText('MT69 statistics collection')
  await expect(reference).toContainText('Coins')
  await expect(reference).toContainText('A later attempt has no confirmed application acknowledgement')
  const table = reference.getByRole('table', { name: 'Configured Audio-Gram call areas and surcharges' })
  await expect(table.getByRole('row', { name: 'Local Allowed 25 cents' })).toBeVisible()
  await expect(table.getByRole('row', { name: 'Intra-area toll Not selected 50 cents' })).toBeVisible()
  await expect(reference).toContainText('configured surcharges do not establish collected or settled amounts')
  await expect(page.getByRole('heading', { name: 'Messaging selected after no answer' })).toBeVisible()
  await expect(page.getByText('17', { exact: true })).toBeVisible()
})
