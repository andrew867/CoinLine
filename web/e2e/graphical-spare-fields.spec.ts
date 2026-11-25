import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('edits graphical text, preview and softkey icons without overwriting reserved fields', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const setId = '00000000-0000-4000-8000-000000000701'
  const fixtureToken = 'fixture-graphics-operator'
  const field = (key: string, label: string, value: string, editor = 'number', isSpare = false) =>
    ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null,
      choices: null as { value: number; label: string }[] | null, help: null })
  const records = [
    { number: 132, revision: 1, sections: [
      { key: 'rotation', label: 'Graphical screens and rotation', fields: [
        field('onHook', 'Display while on hook', 'true', 'checkbox'),
        field('spare.byte1', 'Header spare byte', '165', 'number', true),
      ] },
      { key: 'screen-0', label: 'Screen 9', graphicalScreenIndex: 0, fields: [
        { ...field('screen-0.kind', 'Screen type', '84', 'screen-kind'), choices: [{ value: 84, label: 'Text' }] },
        field('screen-0.text', 'Screen text', 'Welcome', 'screen-text'),
        field('screen-0.spare.text.byte18', 'Text body spare byte 18', '42', 'number', true),
        field('screen-0.spare.byte2', 'Screen control spare byte 2', '199', 'number', true),
      ] },
    ] },
    { number: 133, revision: 1, sections: [
      { key: 'rotation', label: 'Graphical screens and rotation', fields: [field('spare.byte1', 'Header spare byte', '165', 'number', true)] },
      { key: 'button-0', label: 'Screen 17, key 1', graphicalScreenIndex: 0, fields: [
        field('button-0.icon', 'Packed icon byte', '195', 'screen-icon'),
        { ...field('button-0.iconId', 'Icon ID', '3', 'select'), max: 63,
          choices: Array.from({ length: 64 }, (_, value) => ({ value, label: String(value) })) },
        field('button-0.spare.icon.bit6', 'Icon flags spare bit 6 (0x40)', 'true', 'checkbox', true),
        field('button-0.spare.icon.bit7', 'Icon flags spare bit 7 (0x80)', 'true', 'checkbox', true),
      ] },
    ] },
  ]
  const saved = new Map<number, Record<string, string>>()
  const set = () => ({ id: setId, name: 'Workshop graphical configuration', status: 0, revision: 1, customerId: null, isDefault: false,
    publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
    tableProfile: null, firmwareFindings: [], versions: records.map((item, index) => ({
      id: `fixture-version-${item.number}`, tableDefinitionId: `fixture-definition-${item.number}`,
      definitionName: `Graphical screens ${item.number - 130}`, tableNumber: item.number, tableRevision: item.revision, sortOrder: index + 1,
      validationPassed: true, payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 9224, warnings: [],
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
        if (record.number === 133) {
          const fields = record.sections[1].fields
          const value = (key: string) => fields.find(field => field.key === key)!.value
          fields.find(field => field.key === 'button-0.icon')!.value = String(Number(value('button-0.iconId'))
            | (value('button-0.spare.icon.bit6') === 'true' ? 64 : 0)
            | (value('button-0.spare.icon.bit7') === 'true' ? 128 : 0))
        }
        record.revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: 'Later packed profile', length: 9224,
        revision: record.revision, sha256: 'a'.repeat(64), sections: record.sections } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${setId}?record=132`)
  await expect(page.getByText(/MT131 to control rotation for all 24 screens/)).toBeVisible()
  await page.getByLabel('Section or entry').selectOption('screen-0')
  await page.getByLabel('Screen text').fill('Workshop')
  await expect(page.getByRole('img', { name: /Screen lines 1 through 9.*Workshop/ })).toBeVisible()
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel('Text body spare byte 18').fill('129')
  await expect(page.getByLabel('Screen control spare byte 2')).toHaveValue('199')
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saved.get(132)).toEqual({ 'screen-0.text': 'Workshop', 'screen-0.spare.text.byte18': '129' })
  await page.reload()
  await page.getByLabel('Section or entry').selectOption('screen-0')
  await expect(page.getByLabel('Screen text')).toHaveValue('Workshop')
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(page.getByLabel('Text body spare byte 18')).toHaveValue('129')
  await expect(page.getByLabel('Screen control spare byte 2')).toHaveValue('199')

  await page.goto(`/table-sets/${setId}?record=133`)
  await page.getByLabel('Section or entry').selectOption('button-0')
  await expect(page.getByLabel('Packed icon byte')).toHaveCount(0)
  await page.getByLabel('Icon ID').selectOption('5')
  await page.getByText('Spare fields', { exact: true }).click()
  await page.getByLabel('Icon flags spare bit 7 (0x80)').uncheck()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saved.get(133)).toEqual({ 'button-0.iconId': '5', 'button-0.spare.icon.bit7': 'false' })
  await page.reload()
  await page.getByLabel('Section or entry').selectOption('button-0')
  await expect(page.getByLabel('Icon ID')).toHaveValue('5')
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(page.getByLabel('Icon flags spare bit 6 (0x40)')).toBeChecked()
  await expect(page.getByLabel('Icon flags spare bit 7 (0x80)')).not.toBeChecked()
})
