import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('saves independent MT26 display flashing, reserved flag and language count', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-display-operator'
  const id = '00000000-0000-4000-8000-000000002601'
  let revision = 1
  const flag = (key: string, label: string, value: string, isSpare = false) => ({ key, label, value, editor: 'checkbox', min: 0,
    max: 1, maxLength: null, choices: null, help: null, isSpare })
  const language = (key: string, label: string, value: string) => ({ key, label, value, editor: 'select', min: 0,
    max: 15, maxLength: null, help: null, isSpare: false,
    choices: [{ value: 0, label: 'Unused' }, { value: 1, label: 'English' }, { value: 2, label: 'French' },
      { value: 3, label: 'Spanish' }, { value: 4, label: 'Japanese' }] })
  let fields = [flag('displayNumber', 'Display called number', 'true'), flag('flashServiceLevel', 'Flash degraded-service status messages', 'false'),
    flag('hideCalling', 'Suppress the calling prompt', 'true'), flag('spare.display.bit3', 'Display prompt control spare bit 3 (0x08)', 'false', true),
    flag('spare.advertising.bit6', 'Advertising and date display spare bit 6 (0x40)', 'false', true),
    { key: 'languageCount', label: 'Number of languages', value: '2', editor: 'number', min: 1, max: 4, maxLength: null,
      choices: null, help: 'Other imported bits are retained.', isSpare: false },
    language('languageFirst', 'Default language', '1'), language('languageSecond', 'Second language', '2'),
    language('languageThird', 'Third language', '3'), language('languageFourth', 'Fourth language', '15')]
  const saves: Record<string, unknown>[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${id}`) return route.fulfill({ json: {
      id, name: 'Display feature draft', status: 0, revision: 1, customerId: null, isDefault: false, publishedAtUtc: null,
      publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
      tableProfile: null, firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition',
        definitionName: 'Feature and call options', tableNumber: 26, tableRevision: revision, sortOrder: 1, validationPassed: true,
        payloadSha256Hex: String(revision).repeat(64), payloadLengthBytes: 72, warnings: [] }],
    } })
    if (url.pathname === `/api/tables/sets/${id}/records/26/fields`) {
      if (request.method() === 'PUT') {
        const body = request.postDataJSON(); saves.push(body)
        fields = fields.map(field => field.key in body.changes ? { ...field, value: body.changes[field.key] } : field); revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: 'Later packed profile', length: 72, revision,
        sha256: String(revision).repeat(64), sections: [{ key: 'display', label: 'Display and advertising', fields }] } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${id}?record=26`)
  const order = page.getByRole('region', { name: 'Language scrolling order' })
  const first = order.getByRole('rowheader', { name: 'Slot 1' }).locator('..')
  await expect(first.getByRole('cell', { name: 'English' })).toBeVisible()
  await expect(first.getByRole('cell', { name: 'MT85' })).toBeVisible()
  await order.getByRole('button', { name: 'Move slot 2 earlier' }).click()
  await expect(first.getByRole('cell', { name: 'French' })).toBeVisible()
  await expect(first.getByRole('cell', { name: 'MT86' })).toBeVisible()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saves).toHaveLength(1)
  expect(saves[0].changes).toEqual({ languageFirst: '2', languageSecond: '1' })
  await page.reload()
  await expect(page.getByLabel('Default language')).toHaveValue('2')
  await expect(page.getByLabel('Second language')).toHaveValue('1')
  await expect(first.getByRole('cell', { name: 'MT86' })).toBeVisible()
  await expect(page.getByLabel('Flash degraded-service status messages')).not.toBeChecked()
  await expect(page.getByLabel('Display called number')).toBeChecked()
  await expect(page.getByLabel('Suppress the calling prompt')).toBeChecked()
  await page.getByLabel('Flash degraded-service status messages').check()
  await page.getByRole('spinbutton', { name: /^Number of languages/ }).fill('1')
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel('Display prompt control spare bit 3 (0x08)').check()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saves).toHaveLength(2)
  expect(saves[1]).toMatchObject({ revision: 2, sha256: '2'.repeat(64) })
  expect(saves[1].changes).toEqual({ flashServiceLevel: 'true', languageCount: '1', 'spare.display.bit3': 'true' })
  await page.reload()
  await expect(page.getByLabel('Flash degraded-service status messages')).toBeChecked()
  await expect(page.getByLabel('Display called number')).toBeChecked()
  await expect(page.getByLabel('Suppress the calling prompt')).toBeChecked()
  await expect(page.getByRole('spinbutton', { name: /^Number of languages/ })).toHaveValue('1')
  await expect(first.getByRole('cell', { name: 'French' })).toBeVisible()
  await expect(first.getByRole('cell', { name: 'MT85' })).toBeVisible()
  await expect(page.getByLabel('Third language')).toHaveValue('3')
  await expect(page.getByLabel('Fourth language')).toHaveValue('15')
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(page.getByLabel('Display prompt control spare bit 3 (0x08)')).toBeChecked()
  await expect(page.getByLabel('Advertising and date display spare bit 6 (0x40)')).not.toBeChecked()
})
