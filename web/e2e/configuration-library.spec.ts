import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('finds a named configuration and opens its exact advertising editor', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const setId = '00000000-0000-4000-8000-000000000401'
  const firmwareId = '00000000-0000-4000-8000-000000000402'
  const duplicateSetId = '00000000-0000-4000-8000-000000000403'
  const token = 'fixture-configuration-library'
  const hash = 'a'.repeat(64)
  const record = { id: 'fixture-record', tableSetId: setId, setName: 'Workshop display draft', setStatus: 0,
    number: 29, definitionName: 'Text advertising', configurationId: '000099', tableRevision: 2, payloadSha256Hex: hash,
    payloadLengthBytes: 481, validationPassed: true, targetFirmwareVersionId: firmwareId, targetFirmware: 'Fixture firmware',
    recordCountInSet: 1, assignedPhoneCount: 0, nativeLayoutKnown: true }
  const blank = () => ({ text: '', durationTicks: 0, mode: 0, blankBefore: false, endSequence: true,
    spareByte: 0, reservedFlags: 0, containsUnmappedGlyphs: false })
  const slots = Array.from({ length: 20 }, blank)
  slots[0] = { ...slots[0], text: 'Library fixture', durationTicks: 500, endSequence: false }
  let requestedFilter = false
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Fixture operator', role: 'Operator',
        permissions: { overview: 1, 'table-records': 3, 'table-sets': 1, 'table-assignments': 1 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/tables/configuration-library/options') return route.fulfill({ json: {
      records: [{ number: 29, name: 'Text advertising' }], sets: [{ id: setId, name: record.setName }, { id: duplicateSetId, name: 'Duplicate fixture set' }],
      firmware: [{ id: firmwareId, name: record.targetFirmware }],
    } })
    if (url.pathname === '/api/tables/configuration-library') {
      if (url.searchParams.get('configurationId')) {
        expect(url.searchParams.get('configurationId')).toBe('000099')
        expect(url.searchParams.get('number')).toBe('29')
        expect(url.searchParams.get('firmwareVersionId')).toBe(firmwareId)
        expect(url.searchParams.get('status')).toBe('draft')
        requestedFilter = true
      }
      const duplicate = { ...record, tableSetId: duplicateSetId, setName: 'Duplicate fixture set', recordCountInSet: 2 }
      const rows = requestedFilter ? [record] : [record, { ...duplicate, id: 'duplicate-fixture-one', configurationId: '000100' },
        { ...duplicate, id: 'duplicate-fixture-two', configurationId: '000101' }]
      return route.fulfill({ json: { items: rows, total: rows.length, page: 1, pageSize: 50, canReadAssignment: true } })
    }
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: {
      id: setId, name: record.setName, status: 0, revision: 1, customerId: null, isDefault: false,
      publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: firmwareId,
      targetFirmware: record.targetFirmware, tableProfile: null, firmwareFindings: [],
      versions: [{ id: record.id, tableDefinitionId: 'fixture-definition', definitionName: record.definitionName,
        tableNumber: 29, tableRevision: 2, configurationId: record.configurationId, sortOrder: 1,
        validationPassed: true, payloadSha256Hex: hash, payloadLengthBytes: 481, warnings: [] }],
    } })
    if (url.pathname === `/api/tables/sets/${setId}/advertising`) return route.fulfill({ json: {
      slots, revision: 2, sha256: hash, onHookEnabled: true, quickAccessEnabled: false, callEstablishedEnabled: false, scrollTicks: 10,
    } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.getByRole('link', { name: 'Configuration library' }).click()
  await expect(page.getByRole('heading', { name: 'Configuration library' })).toBeVisible()
  await expect(page.getByText('Multiple records of this type in the set')).toHaveCount(2)
  await page.getByLabel('Configuration ID', { exact: true }).fill('000099')
  await page.getByLabel('Record type').selectOption('29')
  await page.getByLabel('Firmware target').selectOption(firmwareId)
  await page.getByLabel('Set status').selectOption('draft')
  await page.getByRole('button', { name: 'Search' }).click()
  await expect(page.getByText('Showing 1 to 1 of 1 records')).toBeVisible()
  expect(requestedFilter).toBe(true)
  const result = page.getByRole('region', { name: 'Saved configuration records' }).getByRole('row').filter({ hasText: '000099' })
  await expect(result).toContainText('MT29')
  await expect(result).toContainText('Workshop display draft')
  await expect(result).toContainText('Fixture firmware')
  await result.getByRole('link', { name: 'Open editor' }).click()
  await expect(page).toHaveURL(new RegExp(`/table-sets/${setId}\\?record=29#config-heading$`))
  await page.getByText('Edit advertising messages', { exact: true }).click()
  await expect(page.getByRole('group', { name: 'Message 1', exact: true }).getByLabel('Text', { exact: true })).toHaveValue('Library fixture')
})

test('opens the exact assignment list with a historical delivery receipt and an inactive phone', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-configuration-usage'
  const setId = 'fixture-published-set', versionId = 'fixture-version', cloneId = 'fixture-copy'
  const record = { id: versionId, tableSetId: setId, setName: 'Published display', setStatus: 1, number: 29,
    definitionName: 'Text advertising', configurationId: '000099', tableRevision: 2, payloadSha256Hex: 'a'.repeat(64),
    payloadLengthBytes: 481, validationPassed: true, targetFirmwareVersionId: null, targetFirmware: null,
    recordCountInSet: 1, assignedPhoneCount: 2, nativeLayoutKnown: true }
  let usageRequested = false
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Fixture operator', role: 'Operator',
        permissions: { overview: 1, 'table-records': 1, 'table-assignments': 1, phones: 1, downloads: 1 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/tables/configuration-library/options') return route.fulfill({ json: { records: [], sets: [], firmware: [] } })
    if (url.pathname === '/api/tables/configuration-library') return route.fulfill({ json: { items: [record,
      { ...record, id: cloneId, tableSetId: 'fixture-copy-set', setName: 'Unrelated copy' }], total: 2, page: 1, pageSize: 50, canReadAssignment: true } })
    if (url.pathname === `/api/tables/configuration-library/${versionId}/assignments`) {
      expect(request.method()).toBe('GET'); expect(url.searchParams.get('page')).toBe('1'); usageRequested = true
      return route.fulfill({ json: { version: { id: versionId, tableSetId: setId, setName: record.setName, number: 29,
        configurationId: record.configurationId, tableRevision: record.tableRevision }, total: 2, page: 1, pageSize: 50, canReadDownloads: true,
        items: [{ terminalId: 'fixture-phone-one', displayName: 'Installed fixture', terminalIdHex: '0000000001', deactivated: false,
          assignedAtUtc: '2026-10-01T10:00:00Z', lastExactAcknowledgement: { downloadBatchId: 'fixture-receipt', recordedAtUtc: '2026-10-01T10:05:00Z' } },
        { terminalId: 'fixture-phone-two', displayName: 'Inactive fixture', terminalIdHex: '0000000002', deactivated: true,
          assignedAtUtc: '2026-10-01T10:00:00Z', lastExactAcknowledgement: null }] } })
    }
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.getByRole('link', { name: 'Configuration library' }).click()
  const usage = page.getByRole('button', { name: 'Review phones for MT29 000099, Published display, revision 2' })
  await usage.click()
  const panel = page.getByRole('region', { name: 'Assigned phones and delivery evidence' })
  await expect(panel.getByRole('link', { name: 'Installed fixture' })).toHaveAttribute('href', '/terminals/fixture-phone-one')
  await expect(panel.getByRole('link', { name: 'View matched receipt' })).toHaveAttribute('href', '/downloads/fixture-receipt')
  await expect(panel.getByText('Inactive', { exact: true })).toBeVisible()
  await expect(panel.getByText('No exact version acknowledgement recorded')).toBeVisible()
  await expect(panel.getByText(/not terminal memory readback/)).toBeVisible()
  expect(usageRequested).toBe(true)
  await panel.getByRole('button', { name: 'Close usage' }).click()
  await expect(panel).toHaveCount(0)
  await expect(usage).toBeFocused()
})
