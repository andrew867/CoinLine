import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('finds a saved graphical link issue and saves the exact corrected mixed-panel address', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-graphical-review-operator'
  const setId = '00000000-0000-4000-8000-000000000852'
  const endpoint = `/api/tables/sets/${setId}/records/131/fields`
  let next = '23'
  let revision = 1
  let saved: Record<string, string> | null = null
  const field = (key: string, label: string, value: string, editor: string) =>
    ({ key, label, value, editor, isSpare: false, min: 0, max: 255, maxLength: null, choices: null, help: null })
  const links = () => ({
    records: [{ number: 131, availability: 'included' }, { number: 132, availability: 'missing' },
      { number: 133, availability: 'included' }],
    references: [
      { sourceRecord: 131, sourceSectionKey: 'screen-0', sourceSection: 'Screen 1', sourceField: 'Next screen',
        fieldKey: 'screen-0.next', role: 'configured_reference', address: Number(next),
        target: next === '23' ? 'Screen 24' : 'Screen 21, panel 2', targetState: next === '23' ? 'undefined_screen' : 'resolved',
        advisory: next === '23' ? 'The target screen is undefined in the included record.' : null },
      { sourceRecord: 131, sourceSectionKey: 'button-0', sourceSection: 'Screen 1, key 1', sourceField: 'Next screen',
        fieldKey: 'button-0.next', role: 'configured_reference', address: 8, target: 'Screen 9',
        targetState: 'missing_record', advisory: "MT132 is not in this set. A partial download may retain the terminal's existing record." },
      { sourceRecord: 133, sourceSectionKey: 'screen-4.panel-0.button-0', sourceSection: 'Screen 21, panel 1, key 1',
        sourceField: 'Next screen', fieldKey: 'screen-4.panel-0.button-0.next', role: 'configured_reference',
        address: 84, target: 'Screen 21, panel 2', targetState: 'resolved', advisory: null },
    ],
  })
  const layout = () => ({ supported: true, editable: true, profile: 'Later packed profile', length: 9224,
    revision, sha256: 'a'.repeat(64), graphicalLinks: links(), sections: [
      { key: 'rotation', label: 'Graphical screens and rotation', fields: [field('onHook', 'Display while on hook', 'true', 'checkbox')] },
      { key: 'screen-0', label: 'Screen 1', graphicalScreenIndex: 0, fields: [
        { ...field('screen-0.kind', 'Screen type', '84', 'screen-kind'), choices: [{ value: 84, label: 'Text' }] },
        field('screen-0.text', 'Screen text', 'Welcome', 'screen-text'),
        field('screen-0.next', 'Next screen', next, 'screen-link'),
      ] },
      { key: 'button-0', label: 'Screen 1, key 1', graphicalScreenIndex: 0, fields: [field('button-0.next', 'Next screen', '8', 'screen-link')] },
    ] })
  const set = () => ({ id: setId, name: 'Graphical link workshop', status: 0, revision: 1, customerId: null, isDefault: false,
    publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null,
    targetFirmware: null, tableProfile: null, firmwareFindings: [], versions: [
      { id: 'fixture-131', tableDefinitionId: 'fixture-def-131', definitionName: 'Graphical screens 1', tableNumber: 131,
        tableRevision: revision, sortOrder: 1, validationPassed: true, payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 9224, warnings: [] },
    ] })
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: set() })
    if (url.pathname === endpoint) {
      if (request.method() === 'PUT') {
        const changes = request.postDataJSON().changes as Record<string, string>
        saved = changes
        next = changes['screen-0.next'] ?? next
        revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: layout() })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${setId}?record=131`)
  await page.getByText('Review saved graphical links', { exact: true }).click()
  const review = page.getByRole('region', { name: 'Saved graphical link review' })
  await expect(review.getByText('2 advisory findings in 2 saved references.')).toBeVisible()
  const invalid = review.getByRole('row').filter({ hasText: 'Screen 24' })
  await expect(invalid).toContainText('Undefined screen')
  await expect(review.getByText("MT132 is not in this set. A partial download may retain the terminal's existing record.")).toBeVisible()
  await invalid.getByRole('button', { name: 'Open source section' }).click()
  await expect(page.getByLabel('Section or entry')).toHaveValue('screen-0')
  await page.getByLabel('Next screen', { exact: true }).selectOption('20')
  await page.getByLabel('Next screen panel').selectOption('1')
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saved).toEqual({ 'screen-0.next': '84' })
  await page.reload()
  await page.getByText('Review saved graphical links', { exact: true }).click()
  await expect(review.getByText('1 advisory finding in 2 saved references.')).toBeVisible()
  await review.getByLabel('Show only advisory findings').uncheck()
  const corrected = review.getByRole('row').filter({ hasText: 'Screen 21, panel 2' })
  await expect(corrected).toContainText('Resolved')
  await review.getByLabel('Review scope').selectOption('all')
  await expect(review.getByText('1 advisory finding in 3 saved references.')).toBeVisible()
})
