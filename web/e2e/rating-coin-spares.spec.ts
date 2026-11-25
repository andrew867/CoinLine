import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('edits coin and time-of-day settings without changing unrelated packed values', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const setId = '00000000-0000-4000-8000-000000000601'
  const fixtureToken = 'fixture-rate-operator'
  const field = (key: string, label: string, value: string, editor = 'number', isSpare = false) =>
    ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null,
      choices: null as { value: number; label: string }[] | null, help: null })
  const records = [
    { number: 50, profile: 'MTR 1.9 packed', length: 105, name: 'Coin validation', revision: 1, sections: [
      { key: 'cash', label: 'Cash box and escrow', fields: [field('cashBoxVolume', 'Cash box capacity', '90'),
        field('spare.byte8', 'Spare byte 8', '90', 'number', true)] },
      { key: 'coin-0', label: 'Coin slot 1', fields: [
        field('coin-0.value', 'Coin value (cents)', '25'),
        field('coin-0.window', 'Enable validator window', 'true', 'checkbox'),
        field('coin-0.accepted', 'Accept coin', 'true', 'checkbox'),
        field('coin-0.spare.flags.bit7', 'Coin validator flags spare bit 7 (0x80)', 'true', 'checkbox', true),
      ] },
    ] },
    { number: 152, profile: 'Later packed profile', length: 95, name: 'Holiday and time-of-day discounts', revision: 1, sections: [
      { key: 'period-0', label: 'Weekdays, period 1', fields: [
        field('period-0.endHour', 'Ends at hour', '0'),
        field('period-0.local', 'Local discount byte', '194'),
        { ...field('period-0.localIndex', 'Local discount', '2', 'select'), choices: [{ value: 2, label: 'MT93 discount 3' }] },
        { ...field('period-0.localPayment', 'Local payment', '0', 'select'), choices: [
          { value: 0, label: 'Disabled' }, { value: 3, label: 'Coins and cards' }] },
        field('period-0.local.spare.flags.bit6', 'Local discount flags spare bit 6 (0x40)', 'true', 'checkbox', true),
      ] },
      { key: 'reserved', label: 'Reserved configuration', fields: [field('spare.byte10', 'Spare byte 10', '90', 'number', true)] },
    ] },
  ]
  const saved = new Map<number, Record<string, string>>()
  const set = () => ({ id: setId, name: 'Workshop configuration', status: 0, revision: 1, customerId: null, isDefault: false,
    publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
    tableProfile: null, firmwareFindings: [], versions: records.map((item, index) => ({
      id: `fixture-version-${item.number}`, tableDefinitionId: `fixture-definition-${item.number}`,
      definitionName: item.name, tableNumber: item.number, tableRevision: item.revision, sortOrder: index + 1,
      validationPassed: true, payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: item.length, warnings: [],
    })) })
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') {
      return route.fulfill({ json: { token: fixtureToken, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    }
    if (request.headers().authorization !== `Bearer ${fixtureToken}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: set() })
    const record = records.find(record => url.pathname === `/api/tables/sets/${setId}/records/${record.number}/fields`)
    if (record) {
      if (request.method() === 'PUT') {
        const changes = request.postDataJSON().changes as Record<string, string>
        saved.set(record.number, changes)
        record.sections = record.sections.map(section => ({ ...section, fields: section.fields.map(field =>
          field.key in changes ? { ...field, value: changes[field.key] } : field) }))
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
  await page.goto(`/table-sets/${setId}?record=50`)
  await page.getByLabel('Section or entry').selectOption('coin-0')
  await page.getByLabel('Coin value (cents)').fill('30')
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel('Coin validator flags spare bit 7 (0x80)').uncheck()
  await expect(page.getByLabel('Accept coin')).toBeChecked()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saved.get(50)).toEqual({ 'coin-0.value': '30', 'coin-0.spare.flags.bit7': 'false' })
  await page.reload()
  await page.getByLabel('Section or entry').selectOption('coin-0')
  await expect(page.getByLabel('Coin value (cents)')).toHaveValue('30')
  await expect(page.getByLabel('Enable validator window')).toBeChecked()

  await page.goto(`/table-sets/${setId}?record=152`)
  await page.getByLabel('Ends at hour').fill('3')
  await page.getByLabel('Local payment').selectOption('3')
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel('Local discount flags spare bit 6 (0x40)').uncheck()
  await expect(page.getByRole('combobox', { name: /^Local discount/ })).toHaveValue('2')
  await expect(page.getByLabel('Local discount byte')).toHaveCount(0)
  await page.getByLabel('Section or entry').selectOption('reserved')
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel('Spare byte 10').fill('129')
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saved.get(152)).toEqual({ 'period-0.endHour': '3', 'period-0.localPayment': '3',
    'period-0.local.spare.flags.bit6': 'false', 'spare.byte10': '129' })
  await page.reload()
  await expect(page.getByLabel('Local payment')).toHaveValue('3')
  await page.getByLabel('Section or entry').selectOption('reserved')
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(page.getByLabel('Spare byte 10')).toHaveValue('129')
})
