import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('assigns two independently selected configurations and clears one optional record from a phone', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-worksheet-operator'
  const id = '00000000-0000-4000-8000-000000920101', phonePath = `/api/terminals/${id}`
  const endpoint = `${phonePath}/configuration-worksheet`, base = '00000000-0000-4000-8000-000000920102', resultSet = '00000000-0000-4000-8000-000000920103'
  const firmware = '00000000-0000-4000-8000-000000920104'
  const phone = { id, siteId: 'fixture-site', displayName: 'Workshop phone', terminalIdHex: '0000000101', status: 1,
    firmwareVersionId: firmware, terminalFamilyId: 'mtr19-multipay', deactivatedAtUtc: null }
  let applied = false
  const assignments: Record<string, unknown>[] = [], unexpectedMutations: string[] = [], libraryReads: string[] = []
  const assigned = (number: number, configurationId: string, revision = 1) => ({ id: `base-${number}`, setId: applied ? resultSet : base,
    setName: applied ? 'Updated phone configuration' : 'Assigned configuration', configurationId, revision, length: number === 29 ? 481 : 105,
    sha256: 'a'.repeat(64), validationPassed: true })
  const options = (number: number, configurationId: string, issue: string | null = null) => ({ id: `source-${number}`, tableSetId: `set-${number}`,
    setName: `Published MT${number}`, configurationId, revision: 5, length: number === 29 ? 481 : 105, sha256: 'b'.repeat(64), targetFirmware: 'Fixture target', issue })
  const rows = () => [
    { number: 29, name: 'Text advertising', family: 'Advertising', familyRequired: false, canChoose: true, canClear: true, ambiguous: false, assigned: assigned(29, applied ? 'ADV002' : 'ADV001', applied ? 2 : 1) },
    { number: 50, name: 'Coin validation', family: 'Coins', familyRequired: true, canChoose: true, canClear: false, ambiguous: false, assigned: assigned(50, applied ? 'COIN02' : 'COIN01', applied ? 2 : 1) },
    { number: 32, name: 'Communications statistics', family: 'Reporting', familyRequired: false, canChoose: true, canClear: !applied, ambiguous: false, assigned: applied ? null : assigned(32, 'STAT01') },
    { number: 31, name: 'Installation and service', family: 'Service', familyRequired: true, canChoose: true, canClear: false, ambiguous: false, assigned: assigned(31, 'SERV01') },
  ]
  const family = { familyLabel: 'MTR 1.9 Multi-pay', documented: true, tables: [{ name: 'Local dialing', required: true, status: 'missing' }] }
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') return route.fulfill({ json: { token, role: 'Operator', displayName: 'Fixture operator',
      permissions: { overview: 1, phones: 1, 'table-assignments': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === endpoint + '/preview' && request.method() === 'POST') {
      return route.fulfill({ json: { previewToken: 'fixture-review', canApply: true, initialAssignment: false, family, changes: [
        { tableNumber: 26, action: 'keep', required: true, personalized: false, current: assigned(26, 'FEAT01'), result: assigned(26, 'FEAT01') },
        { tableNumber: 29, action: 'replace', required: false, personalized: false, current: assigned(29, 'ADV001'), result: assigned(29, 'ADV002', 2) },
        { tableNumber: 32, action: 'remove', required: false, personalized: false, current: assigned(32, 'STAT01'), result: null },
        { tableNumber: 50, action: 'replace', required: false, personalized: false, current: assigned(50, 'COIN01'), result: assigned(50, 'COIN02', 2) },
      ] } })
    }
    if (url.pathname === endpoint && request.method() === 'POST') {
      assignments.push(request.postDataJSON()); applied = true
      return route.fulfill({ json: { tableSetId: resultSet, tableSetName: 'Updated phone configuration', changedNumbers: [29, 50], removedNumbers: [32] } })
    }
    if (request.method() !== 'GET') { unexpectedMutations.push(url.pathname); return route.fulfill({ status: 405 }) }
    if (url.pathname.startsWith('/api/tables/sets')) { libraryReads.push(url.pathname); return route.fulfill({ status: 403 }) }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [{ id: phone.terminalFamilyId, release: 'MTR 1.9', variant: 'Multi-pay', maximizerTypeNumbers: [28] }] })
    if (url.pathname === '/api/terminals/firmware-options') return route.fulfill({ json: [{ id: firmware, label: 'Fixture target', buildId: null }] })
    if (url.pathname === phonePath) return route.fulfill({ json: phone })
    if (url.pathname === `${phonePath}/identity`) return route.fulfill({ json: { terminalIdHex: phone.terminalIdHex, previousId: null, previousType: null, firstContactAtUtc: null } })
    if (url.pathname === `${phonePath}/table-assignment`) return route.fulfill({ json: { terminalId: id, tableSetId: applied ? resultSet : base,
      tableSetName: applied ? 'Updated phone configuration' : 'Assigned configuration', previousTableSetId: applied ? base : null, siteId: phone.siteId,
      customerId: null, assignedAtUtc: '2028-01-01T12:00:00Z', targetFirmwareVersionId: firmware, targetFirmware: 'Fixture target' } })
    if (url.pathname === `${phonePath}/table-assignment-options`) return route.fulfill({ json: [{ id: base, name: 'Assigned configuration', status: 1,
      targetFirmwareVersionId: firmware, targetFirmware: 'Fixture target', compatibilityIssue: null }] })
    if (url.pathname === endpoint) return route.fulfill({ json: { id, displayName: phone.displayName, terminalIdHex: phone.terminalIdHex, firmware: 'Fixture target',
      baseSetId: applied ? resultSet : base, baseSetName: applied ? 'Updated phone configuration' : 'Assigned configuration', assignmentRevision: applied ? 8 : 7,
      canUpdate: true, family, records: rows() } })
    if (url.pathname === endpoint + '/options') {
      const number = Number(url.searchParams.get('number'))
      return route.fulfill({ json: { records: number === 29 ? [options(29, 'ADV002'), { ...options(29, 'WRONG1', 'Requires another firmware'), id: 'wrong' }]
        : [options(50, 'COIN02')], total: number === 29 ? 2 : 1, page: 1, pageSize: 50 } })
    }
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/terminals/${id}`)
  await expect(page.getByRole('heading', { name: 'Workshop phone' })).toBeVisible()
  await page.getByText('Individual configuration assignments').click()
  const table = page.getByRole('table', { name: 'Assigned configuration records' })
  await expect(table.getByText('ADV001 · revision 1')).toBeVisible()
  await page.getByRole('button', { name: 'Look up MT29' }).click()
  const lookup = page.getByRole('region', { name: 'Configuration lookup for MT29' })
  await lookup.getByLabel('Configuration ID or set name').fill('ADV002')
  await lookup.getByRole('button', { name: 'Find configurations' }).click()
  await expect(lookup.getByRole('button', { name: 'Choose WRONG1' })).toBeDisabled()
  await lookup.getByRole('button', { name: 'Choose ADV002' }).click()
  await page.getByRole('button', { name: 'Look up MT50' }).click()
  await page.getByRole('button', { name: 'Choose COIN02' }).click()
  await page.getByRole('button', { name: 'Clear MT32' }).click()
  await expect(table.getByText('Assign ADV002 · revision 5')).toBeVisible()
  await expect(table.getByText('Assign COIN02 · revision 5')).toBeVisible()
  await expect(table.getByText('Clear optional assignment')).toBeVisible()
  await page.getByRole('button', { name: 'Preview configuration assignment' }).click()
  const preview = page.getByRole('region', { name: 'Configuration assignment preview' })
  await expect(preview.getByText('Required dependency')).toBeVisible()
  await expect(preview.getByText(/Local dialing \(missing\)/)).toBeVisible()
  const apply = preview.getByRole('button', { name: 'Apply configuration assignment' })
  await expect(apply).toBeDisabled()
  await preview.getByLabel('I reviewed the resulting host assignment.').check()
  await apply.click()
  await expect(page.getByText(/Host assignment updated to Updated phone configuration. No download has been queued/)).toBeVisible()
  await expect(table.getByText('ADV002 · revision 2')).toBeVisible()
  await expect(table.getByText('COIN02 · revision 2')).toBeVisible()
  await expect(table.getByText('SERV01 · revision 1')).toBeVisible()
  expect(assignments).toEqual([{ baseSetId: base, assignmentRevision: 7, personalizeAccessIdentity: false, previewToken: 'fixture-review', choices: [
    { number: 29, action: 'copy', sourceSetId: 'set-29', sourceVersionId: 'source-29' }, { number: 32, action: 'clear' },
    { number: 50, action: 'copy', sourceSetId: 'set-50', sourceVersionId: 'source-50' },
  ] }])
  expect(unexpectedMutations).toEqual([])
  expect(libraryReads).toEqual([])
})

test('starts an unassigned phone from a published starter with reviewed identity and no automatic download', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-starter-operator'
  const id = '00000000-0000-4000-8000-000000920201', phonePath = `/api/terminals/${id}`, endpoint = `${phonePath}/configuration-worksheet`
  const firmware = '00000000-0000-4000-8000-000000920202', source = '00000000-0000-4000-8000-000000920203', result = '00000000-0000-4000-8000-000000920204'
  const phone = { id, siteId: 'fixture-site', displayName: 'New workshop phone', terminalIdHex: '0000000201', status: 1,
    firmwareVersionId: firmware, terminalFamilyId: 'mtr19-multipay', deactivatedAtUtc: null }
  const records = [
    { number: 21, name: 'Management access', family: 'Access', familyRequired: true, length: 18 },
    { number: 29, name: 'Text advertising', family: 'Advertising', familyRequired: false, length: 481 },
    { number: 50, name: 'Coin validation', family: 'Coins', familyRequired: true, length: 105 },
  ]
  const options = records.map(row => ({ number: row.number, id: `source-${row.number}`, tableSetId: source, setName: 'Published workshop starter',
    configurationId: `START${row.number}`, revision: 1, length: row.length, sha256: 'a'.repeat(64), targetFirmware: 'Fixture target', issue: null }))
  const family = { familyLabel: 'MTR 1.9 Multi-pay', documented: true, tables: [{ name: 'Local dialing', required: true, status: 'missing' }] }
  let applied = false
  const previews: Record<string, unknown>[] = [], assignments: Record<string, unknown>[] = [], unexpectedMutations: string[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') return route.fulfill({ json: { token, role: 'Operator', displayName: 'Fixture operator',
      permissions: { overview: 1, phones: 1, 'table-assignments': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === endpoint + '/preview' && request.method() === 'POST') {
      const data = request.postDataJSON(); previews.push(data)
      if (!data.personalizeAccessIdentity) return route.fulfill({ status: 400, json: { error: 'The terminal access record carries another phone’s wire ID.' } })
      return route.fulfill({ json: { previewToken: 'starter-review', canApply: true, initialAssignment: true, family,
        changes: options.map(row => ({ tableNumber: row.number, action: 'add', required: false, personalized: row.number === 21, current: null, result: row })) } })
    }
    if (url.pathname === endpoint && request.method() === 'POST') {
      assignments.push(request.postDataJSON()); applied = true
      return route.fulfill({ json: { tableSetId: result, tableSetName: 'New phone configuration', changedNumbers: records.map(row => row.number), removedNumbers: [] } })
    }
    if (request.method() !== 'GET') { unexpectedMutations.push(url.pathname); return route.fulfill({ status: 405 }) }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [{ id: phone.terminalFamilyId, release: 'MTR 1.9', variant: 'Multi-pay', maximizerTypeNumbers: [28] }] })
    if (url.pathname === '/api/terminals/firmware-options') return route.fulfill({ json: [{ id: firmware, label: 'Fixture target', buildId: null }] })
    if (url.pathname === phonePath) return route.fulfill({ json: phone })
    if (url.pathname === `${phonePath}/identity`) return route.fulfill({ json: { terminalIdHex: phone.terminalIdHex, previousId: null, previousType: null, firstContactAtUtc: null } })
    if (url.pathname === `${phonePath}/table-assignment`) return applied ? route.fulfill({ json: { terminalId: id, tableSetId: result,
      tableSetName: 'New phone configuration', previousTableSetId: null, siteId: phone.siteId, customerId: null, assignedAtUtc: '2028-01-01T12:00:00Z',
      targetFirmwareVersionId: firmware, targetFirmware: 'Fixture target' } }) : route.fulfill({ status: 404 })
    if (url.pathname === `${phonePath}/table-assignment-options`) return route.fulfill({ json: [] })
    if (url.pathname === `${endpoint}/initial-sources`) return route.fulfill({ json: { available: true, page: 1, pageSize: 50, total: 1,
      sources: [{ id: source, name: 'Published workshop starter', isDefault: true, recordCount: 3, hasAccess: true }] } })
    if (url.pathname === `${endpoint}/initial-sources/${source}`) return route.fulfill({ json: { id: source, name: 'Published workshop starter',
      records: options, requiresAccessPersonalization: true, family } })
    if (url.pathname === endpoint) return route.fulfill({ json: { id, displayName: phone.displayName, terminalIdHex: phone.terminalIdHex, firmware: 'Fixture target',
      baseSetId: applied ? result : null, baseSetName: applied ? 'New phone configuration' : null, assignmentRevision: applied ? 1 : null, canUpdate: true, family,
      records: records.map((row, index) => ({ ...row, canChoose: true, canClear: false, ambiguous: false, assigned: applied ? {
        ...options[index], setId: result, setName: 'New phone configuration', validationPassed: true } : null })) } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/terminals/${id}`)
  await expect(page.getByRole('heading', { name: 'New workshop phone' })).toBeVisible()
  await page.getByText('Individual configuration assignments').click()
  await page.getByRole('button', { name: 'Choose a matching starter' }).click()
  const starter = page.getByRole('region', { name: 'Initial configuration starter' })
  await starter.getByLabel('Published starter').selectOption(source)
  await expect(starter.getByText('3 records can be staged: MT21, MT29, MT50.')).toBeVisible()
  await expect(starter.getByText(/MT21 record belongs to a different line/)).toBeVisible()
  await starter.getByRole('button', { name: 'Stage 3 starter records' }).click()
  await expect(page.getByText('3 staged changes')).toBeVisible()
  expect(previews).toEqual([]); expect(assignments).toEqual([])
  await page.getByRole('button', { name: 'Preview configuration assignment' }).click()
  await expect(page.getByText(/terminal access record carries another phone/)).toBeVisible()
  await page.getByLabel(/Use this phone’s registered wire ID/).check()
  await page.getByRole('button', { name: 'Preview configuration assignment' }).click()
  const preview = page.getByRole('region', { name: 'Configuration assignment preview' })
  await expect(preview.getByText('Registered wire ID applied')).toBeVisible()
  await expect(preview.getByText(/Local dialing \(missing\)/)).toBeVisible()
  await expect(preview.getByRole('button', { name: 'Apply configuration assignment' })).toBeDisabled()
  await preview.getByLabel('I reviewed the resulting host assignment.').check()
  await preview.getByRole('button', { name: 'Apply configuration assignment' }).click()
  await expect(page.getByText(/Host assignment updated to New phone configuration. No download has been queued/)).toBeVisible()
  expect(assignments).toEqual([{ baseSetId: null, assignmentRevision: null, personalizeAccessIdentity: true, previewToken: 'starter-review',
    choices: options.map(row => ({ number: row.number, action: 'copy', sourceSetId: source, sourceVersionId: row.id })) }])
  expect(previews).toHaveLength(2)
  expect(unexpectedMutations).toEqual([])
})
