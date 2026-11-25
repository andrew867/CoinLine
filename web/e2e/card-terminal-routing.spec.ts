import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('saves carrier rerouting and later card reserves without changing validation settings', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const setId = '00000000-0000-4000-8000-000000000901'
  const token = 'fixture-routing-operator'
  const field = (key: string, label: string, value: string, editor = 'checkbox', isSpare = false) =>
    ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null, choices: null, help: null })
  const records = [
    { number: 26, length: 72, name: 'Terminal features', revision: 1, sections: [
      { key: 'payment', label: 'Terminal and payment', fields: [field('localAuthorization', 'Require host authorization for local card calls', 'true')] },
      { key: 'carrierReroute', label: 'Carrier payment rerouting', fields: [
        field('rerouteCoin', 'Permit alternate carrier routing for coins', 'false'),
        field('rerouteCredit', 'Permit alternate carrier routing for credit cards', 'false'),
        field('spare.carrierReroute.bit7', 'Carrier reroute control spare bit 7 (0x80)', 'true', 'checkbox', true),
      ] },
    ] },
    { number: 134, length: 1153, name: 'Card ranges', revision: 1, sections: [
      { key: 'card-0', label: 'Card range 1', fields: [
        field('card-0.hostValidation', 'Require host validation', 'true'),
        field('card-0.spare.control.bit7', 'Additional card control spare bit 7 (0x80)', 'true', 'checkbox', true),
      ] },
      { key: 'card-0-spill', label: 'Card range 1 data spill', fields: [
        field('card-0.spare.spillTerminator.bit7', 'Spill terminator spare bit 7 (0x80)', 'true', 'checkbox', true),
      ] },
    ] },
  ]
  const saved: { number: number; changes: Record<string, string> }[] = []
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: {
      id: setId, name: 'Terminal routing draft', status: 0, revision: 1, customerId: null,
      isDefault: false, publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
      tableProfile: null, firmwareFindings: [], versions: records.map((record, index) => ({
        id: `fixture-version-${record.number}`, tableDefinitionId: `fixture-definition-${record.number}`, definitionName: record.name,
        tableNumber: record.number, tableRevision: record.revision, sortOrder: index + 1, validationPassed: true,
        payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: record.length, warnings: [],
      })),
    } })
    const record = records.find(record => url.pathname === `/api/tables/sets/${setId}/records/${record.number}/fields`)
    if (record) {
      if (request.method() === 'PUT') {
        const { changes } = request.postDataJSON()
        saved.push({ number: record.number, changes })
        record.sections = record.sections.map(section => ({ ...section, fields: section.fields.map(field =>
          field.key in changes ? { ...field, value: changes[field.key] } : field) }))
        record.revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: 'Later packed profile', length: record.length,
        revision: record.revision, sha256: 'a'.repeat(64), sections: record.sections } })
    }
    if (url.pathname.endsWith('/card-sequence')) return route.fulfill({ json: { available: false, reason: 'No companion record' } })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${setId}?record=26`)
  await expect(page.getByLabel(/Require host authorization/)).toBeChecked()
  await page.getByLabel('Section or entry').selectOption('carrierReroute')
  await page.getByLabel(/Permit alternate carrier routing for coins/).check()
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel(/Carrier reroute control spare bit 7/).uncheck()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saved[0]).toEqual({ number: 26, changes: { rerouteCoin: 'true', 'spare.carrierReroute.bit7': 'false' } })
  await page.reload()
  await expect(page.getByLabel(/Require host authorization/)).toBeChecked()
  await page.getByLabel('Section or entry').selectOption('carrierReroute')
  await expect(page.getByLabel(/Permit alternate carrier routing for coins/)).toBeChecked()
  await expect(page.getByLabel(/Permit alternate carrier routing for credit cards/)).not.toBeChecked()

  await page.goto(`/table-sets/${setId}?record=134`)
  await expect(page.getByLabel('Require host validation')).toBeChecked()
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel(/Additional card control spare bit 7/).uncheck()
  await page.getByLabel('Section or entry').selectOption('card-0-spill')
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel(/Spill terminator spare bit 7/).uncheck()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saved[1]).toEqual({ number: 134, changes: { 'card-0.spare.control.bit7': 'false', 'card-0.spare.spillTerminator.bit7': 'false' } })
  await page.reload()
  await expect(page.getByLabel('Require host validation')).toBeChecked()
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(page.getByLabel(/Additional card control spare bit 7/)).not.toBeChecked()
})
