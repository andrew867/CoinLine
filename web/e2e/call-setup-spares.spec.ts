import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('authors optional promotion text and usage settings while retaining protected call-setup fields', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const setId = '00000000-0000-4000-8000-000000000501'
  const token = 'fixture-call-setup-operator'
  const field = (key: string, label: string, value: string | null, editor: string, isSpare = false) =>
    ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null as number | null,
      choices: null as { value: number; label: string }[] | null, help: null, displayGlyphs: false })
  const prompt = (key: string, label: string, value: string | null) =>
    ({ ...field(key, label, value, 'text'), maxLength: 20, displayGlyphs: true })
  let promotionSections = [
    { key: 'display', label: 'Promotion messages', fields: [
      prompt('prompt1', 'First prompt', 'Cafe'), prompt('prompt2', 'Second prompt', null), prompt('prompt3', 'Third prompt', ''),
      field('spare.byte2', 'Spare byte 2', '165', 'number', true),
    ] },
    { key: 'promotion-0', label: 'Promotion range 1', fields: [
      field('promotion-0.enable', 'Enable promotion', 'true', 'checkbox'),
      { ...field('promotion-0.usageThreshold', 'Minimum card usage', '0', 'select'), choices: [
        { value: 0, label: 'Disabled' }, { value: 1, label: '25% used' }, { value: 2, label: '50% used' }, { value: 3, label: '75% used' },
      ] },
      field('promotion-0.spare.flags.bit7', 'Promotion flags spare bit 7 (0x80)', 'true', 'checkbox', true),
    ] },
  ]
  const installationSections = [
    { key: 'predial', label: 'Predial strings', fields: [
      field('predialPrimaryManager', 'Apply predial to the primary packet-data access number', 'false', 'checkbox'),
      field('predialAlternateManager', 'Apply predial to the alternate packet-data access number', 'false', 'checkbox'),
    ] },
    { key: 'craft', label: 'Craft credentials', fields: [
      { ...field('craftAccessCode', 'Craft access code', null, 'password'), maxLength: 7 },
      { ...field('keyCardNumber', 'Desk terminal key card number', null, 'password'), maxLength: 10 },
    ] },
  ]
  let revision = 1
  let saved: Record<string, string> = {}
  const set = () => ({ id: setId, name: 'Workshop optional configuration', status: 0, revision: 1, customerId: null, isDefault: false,
    publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
    tableProfile: null, firmwareFindings: [], versions: [
      { id: 'fixture-version-95', tableDefinitionId: 'fixture-definition-95', definitionName: 'Instant Win', tableNumber: 95,
        tableRevision: revision, sortOrder: 1, validationPassed: true, payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 153, warnings: [] },
      { id: 'fixture-version-31', tableDefinitionId: 'fixture-definition-31', definitionName: 'Installation and service', tableNumber: 31,
        tableRevision: 1, sortOrder: 2, validationPassed: true, payloadSha256Hex: 'b'.repeat(64), payloadLengthBytes: 37, warnings: [] },
    ] })
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') {
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    }
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: set() })
    if (url.pathname === `/api/tables/sets/${setId}/records/95/fields`) {
      if (request.method() === 'PUT') {
        saved = request.postDataJSON().changes
        promotionSections = promotionSections.map(section => ({ ...section, fields: section.fields.map(field =>
          field.key in saved ? { ...field, value: saved[field.key].normalize('NFC') } : field) }))
        revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: 'Later packed profile', length: 153, revision,
        sha256: 'a'.repeat(64), sections: promotionSections } })
    }
    if (url.pathname === `/api/tables/sets/${setId}/records/31/fields`) return route.fulfill({ json: {
      supported: true, editable: true, profile: 'Later packed profile', length: 37, revision: 1, sha256: 'b'.repeat(64), sections: installationSections } })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${setId}?record=95`)
  await page.getByLabel('First prompt', { exact: true }).fill('Cafe\u0301')
  await expect(page.getByRole('img', { name: 'First prompt, Café' })).toBeVisible()
  await page.getByLabel('Preview promotion prompt').selectOption('1')
  await expect(page.getByRole('img', { name: 'Second prompt, Unmapped glyphs' })).toBeVisible()
  await page.getByLabel('Section or entry').selectOption('promotion-0')
  await page.getByLabel('Minimum card usage').selectOption('3')
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel('Promotion flags spare bit 7 (0x80)').uncheck()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saved).toEqual({ prompt1: 'Cafe\u0301', 'promotion-0.usageThreshold': '3', 'promotion-0.spare.flags.bit7': 'false' })
  await page.reload()
  await expect(page.getByLabel('First prompt', { exact: true })).toHaveValue('Café')
  await page.getByLabel('Section or entry').selectOption('promotion-0')
  await expect(page.getByLabel('Minimum card usage')).toHaveValue('3')
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(page.getByLabel('Promotion flags spare bit 7 (0x80)')).not.toBeChecked()

  await page.goto(`/table-sets/${setId}?record=31`)
  await expect(page.getByLabel('Apply predial to the primary packet-data access number')).toBeVisible()
  await page.getByLabel('Section or entry').selectOption('craft')
  await expect(page.getByLabel('Craft access code')).toHaveValue('')
  await expect(page.getByLabel('Desk terminal key card number')).toHaveValue('')
})
