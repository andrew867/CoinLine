import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('edits a later display-format rule in the former currency tail without changing numbering settings', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-numbering-operator'
  const rule = { id: 'row-0', noMatch: null as string | null, match: null, control: 4, tag: 0, callType: 0,
    digits: [0x51], conversion: null, maximumDigits: null }
  const field = (key: string, label: string, value: string, isSpare = false) =>
    ({ key, label, value, editor: 'number', isSpare, min: 0, max: 255, maxLength: null, choices: null, help: null })
  const records = [false, true].map((later, index) => ({
    id: `00000000-0000-4000-8000-00000000100${index + 1}`, later, revision: 1,
    plan: { dial: [], format: [{ ...rule, noMatch: later ? 'row-132' : 'row-12' },
      { ...rule, id: later ? 'row-132' : 'row-12', digits: later ? [0x91, 0x51, 0x91] : [0x91] }] },
    sections: [
      { key: 'numbering', label: 'Digit collection and routing', fields: [field('spare.byte1', 'Spare byte 1', '182', true)] },
      ...(!later ? [{ key: 'number-display', label: 'Number display and currency', fields: [field('currencyWidth', 'Minimum display width', '5')] }] : []),
    ],
  }))
  const saves: Record<string, unknown>[] = []
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    const item = records.find(record => url.pathname === `/api/tables/sets/${record.id}`)
    if (item) return route.fulfill({ json: {
      id: item.id, name: 'Numbering plan draft', status: 0, revision: 1, customerId: null,
      isDefault: false, publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
      tableProfile: null, firmwareFindings: [], versions: [{
        id: 'fixture-version', tableDefinitionId: 'fixture-definition', definitionName: 'Numbering plan', tableNumber: 62,
        tableRevision: item.revision, sortOrder: 1, validationPassed: true, payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 353, warnings: [],
      }],
    } })
    const record = records.find(record => url.pathname === `/api/tables/sets/${record.id}/records/62/fields`)
    if (record) {
      if (request.method() === 'PUT') {
        const body = request.postDataJSON()
        saves.push(body)
        record.plan = { ...record.plan, ...body.numberingPlan }
        record.sections = record.sections.map(section => ({ ...section, fields: section.fields.map(field =>
          field.key in body.changes ? { ...field, value: body.changes[field.key] } : field) }))
        record.revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: record.later ? 'Later packed profile' : 'MTR 1.9 packed',
        length: 353, revision: record.revision, sha256: 'a'.repeat(64), sections: record.sections,
        numberingPlan: record.plan, numberingFormatCapacity: record.later ? 140 : 132 } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${records[0].id}?record=62`)
  await page.getByLabel('Section or entry').selectOption('number-display')
  await expect(page.getByLabel('Minimum display width')).toHaveValue('5')
  await page.getByRole('button', { name: 'Numbering rules' }).click()
  await page.getByLabel('Rule set').selectOption('format')
  await expect(page.getByText('12 of 132 rule bytes used')).toBeVisible()

  await page.goto(`/table-sets/${records[1].id}?record=62`)
  await expect(page.getByText('Number display and currency')).toHaveCount(0)
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel('Spare byte 1').fill('129')
  await page.getByRole('button', { name: 'Numbering rules' }).click()
  await page.getByLabel('Rule set').selectOption('format')
  await expect(page.getByText('14 of 140 rule bytes used')).toBeVisible()
  await page.getByRole('combobox', { name: 'Rule', exact: true }).selectOption('row-132')
  const last = page.locator('.numbering-digit').nth(2)
  await last.getByLabel('Separator after').uncheck()
  await last.getByLabel('Separator before').check()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saves[0]).toMatchObject({ changes: { 'spare.byte1': '129' },
    numberingPlan: { format: [{ ...rule, noMatch: 'row-132' }, { ...rule, id: 'row-132', digits: [0x91, 0x51, 0x51] }] } })
  await page.reload()
  await page.getByRole('button', { name: 'Numbering rules' }).click()
  await page.getByLabel('Rule set').selectOption('format')
  await page.getByRole('combobox', { name: 'Rule', exact: true }).selectOption('row-132')
  await expect(page.locator('.numbering-digit').nth(2).getByLabel('Separator after')).not.toBeChecked()
  await expect(page.locator('.numbering-digit').nth(2).getByLabel('Separator before')).toBeChecked()
})
