import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('applies reusable carrier spares only by choice and edits scheduler reserves independently', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const carrierSet = '00000000-0000-4000-8000-000000000801'
  const schedulerSet = '00000000-0000-4000-8000-000000000802'
  const token = 'fixture-carrier-operator'
  const field = (key: string, label: string, value: string | null, editor = 'number', isSpare = false) =>
    ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null as number | null,
      choices: null, help: null })
  const records = [
    { id: carrierSet, number: 23, length: 678, profile: 'MTR 1.9 packed', name: 'Carrier draft', revision: 1, sections: [
      { key: 'selection', label: 'Carrier selection', fields: [field('primary', 'Primary reference', '255')] },
      { key: 'carrier-5', label: 'Carrier 5', fields: [
        field('carrier-5.reference', 'Carrier reference', '18'),
        { ...field('carrier-5.name', 'Display name', 'Original brand', 'text'), maxLength: 20 },
        field('carrier-5.card-0', 'Accept card range 1', 'true', 'checkbox'),
        field('carrier-5.spare.byte1', 'Carrier row spare byte', '150', 'number', true),
      ] },
    ] },
    { id: schedulerSet, number: 94, length: 100, profile: 'Later packed profile', name: 'Firmware parameters draft', revision: 1, sections: [
      { key: 'schedule', label: 'Code download schedule', fields: [
        field('enabled', 'Enable the scheduled firmware download', 'false', 'checkbox'),
        { ...field('serverAuthentication', 'Code server authentication', null, 'password-hex'), maxLength: 12 },
      ] },
      { key: 'reserved', label: 'Reserved code-download bytes', fields: [
        field('spareParameters', 'Encoded spare parameters', '00'.repeat(26), 'text'),
        field('spare.byte26', 'Spare byte 26', '91', 'number', true),
      ] },
    ] },
  ]
  const definition = { id: 'fixture-carrier', code: 'CARR', description: 'Workshop carrier definition',
    tableNumber: 23, revision: 3, reference: '9', displayName: 'Fixture brand', featureGroupB: false, screeningEntry: null }
  const applied: Record<string, unknown>[] = []
  let schedulerChanges: Record<string, string> = {}
  const set = (item: typeof records[number]) => ({ id: item.id, name: item.name, status: 0, revision: 1, customerId: null,
    isDefault: false, publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
    tableProfile: null, firmwareFindings: [], versions: [{ id: `fixture-version-${item.number}`, tableDefinitionId: `fixture-definition-${item.number}`,
      definitionName: item.name, tableNumber: item.number, tableRevision: item.revision, sortOrder: 1,
      validationPassed: true, payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: item.length, warnings: [],
    }] })
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') {
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3, 'carrier-library': 1 } } })
    }
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    const item = records.find(record => url.pathname === `/api/tables/sets/${record.id}`)
    if (item) return route.fulfill({ json: set(item) })
    if (url.pathname === '/api/carrier-library' && request.method() === 'GET') return route.fulfill({ json: [definition] })
    if (url.pathname === '/api/carrier-library/fixture-carrier/apply' && request.method() === 'POST') {
      const body = request.postDataJSON()
      applied.push(body)
      const record = records[0]
      const fields = record.sections[1].fields
      fields.find(field => field.key === 'carrier-5.reference')!.value = '9'
      fields.find(field => field.key === 'carrier-5.name')!.value = definition.displayName
      if (body.copySpareFields) fields.find(field => field.key === 'carrier-5.spare.byte1')!.value = '197'
      if (!body.keepCardAcceptance) fields.find(field => field.key === 'carrier-5.card-0')!.value = 'false'
      record.revision++
      return route.fulfill({ status: 204 })
    }
    const record = records.find(record => url.pathname === `/api/tables/sets/${record.id}/records/${record.number}/fields`)
    if (record) {
      if (request.method() === 'PUT') {
        schedulerChanges = request.postDataJSON().changes
        record.sections = record.sections.map(section => ({ ...section, fields: section.fields.map(field =>
          field.key in schedulerChanges ? { ...field, value: schedulerChanges[field.key] } : field) }))
        record.revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: record.profile, length: record.length,
        revision: record.revision, sha256: 'a'.repeat(64), sections: record.sections } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${carrierSet}?record=23`)
  await page.getByLabel('Section or entry').selectOption('carrier-5')
  await page.getByLabel('Library carrier').selectOption(definition.id)
  await expect(page.getByLabel('Reserved fields for this row')).toHaveValue('keep')
  await page.getByLabel('Card acceptance for this row').selectOption('keep')
  await page.getByRole('button', { name: 'Apply carrier definition' }).click()
  await expect.poll(() => applied.length).toBe(1)
  expect(applied[0]).toMatchObject({ slot: 5, keepCardAcceptance: true, copySpareFields: false })
  await page.reload()
  await page.getByLabel('Section or entry').selectOption('carrier-5')
  await expect(page.getByRole('img', { name: /lower line Fixture brand/ })).toBeVisible()
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(page.getByLabel('Carrier row spare byte')).toHaveValue('150')
  await page.getByLabel('Library carrier').selectOption(definition.id)
  await page.getByLabel('Reserved fields for this row').selectOption('copy')
  await page.getByRole('button', { name: 'Apply carrier definition' }).click()
  await expect.poll(() => applied.length).toBe(2)
  expect(applied[1]).toMatchObject({ slot: 5, keepCardAcceptance: true, copySpareFields: true })
  await page.reload()
  await page.getByLabel('Section or entry').selectOption('carrier-5')
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(page.getByLabel('Carrier row spare byte')).toHaveValue('197')
  await expect(page.getByLabel('Accept card range 1')).toBeChecked()

  await page.goto(`/table-sets/${schedulerSet}?record=94`)
  await expect(page.getByLabel('Code server authentication')).toHaveValue('')
  await expect(page.getByLabel('Enable the scheduled firmware download')).not.toBeChecked()
  await page.getByLabel('Section or entry').selectOption('reserved')
  await expect(page.getByLabel('Encoded spare parameters')).toHaveCount(0)
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel('Spare byte 26').fill('129')
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(schedulerChanges).toEqual({ 'spare.byte26': '129' })
  await page.reload()
  await page.getByLabel('Section or entry').selectOption('reserved')
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(page.getByLabel('Spare byte 26')).toHaveValue('129')
})
