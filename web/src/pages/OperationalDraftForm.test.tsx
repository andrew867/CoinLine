import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { OperationalDraftForm, type OperationalSpec } from './OperationalDraftForm'
import { NumberingPlanEditor, type NumberingPlan } from './NumberingPlanEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })
const numeric = (key: string, label: string) => ({ key, label, editor: 'number' as const, min: 0, max: 32767, choices: null, help: null })
const flag = (key: string, label: string) => ({ key, label, editor: 'checkbox' as const, min: 0, max: 255, choices: null, help: null })
const timing: OperationalSpec = { number: 30, name: 'User interface parameters', profile: 'MTR 1.9 packed', length: 68,
  sections: [{ key: 'prompts', label: 'Prompt and input timing', fields: [numeric('clearDigits', 'Clear digits after')] },
    { key: 'display', label: 'Display timing', fields: [numeric('scrollSpeed', 'Display scroll timing')] }] }
const modem = (later: boolean): OperationalSpec => ({ number: 33, name: 'Modem parameters', profile: later ? 'Later packed profile' : 'MTR 1.9 packed', length: 36,
  sections: [{ key: 'modem', label: 'Modem line timing', fields: [flag('carrierLossTimeout', 'Enable carrier-loss timeout'),
    flag('answerToneDetection', 'Enable answer tone detection'), ...(later ? [numeric('answerRemovalDelay', 'Delay after answer tone removal')] : [])] }] })

it('authors a missing record through named settings and manual entry, preserving failed-create values', async () => {
  consoleSession.save('fixture-token', '', 'Fixture administrator', 'Admin')
  let created = false; const saved = vi.fn(), dirty = vi.fn()
  vi.mocked(apiFetch).mockImplementation(async url => Response.json(String(url).includes('operational-authoring') ? { templates: [timing] } : created ? {
    supported: true, editable: true, profile: timing.profile, length: timing.length, revision: 1, sha256: 'fixture-hash',
    sections: [{ key: 'prompts', label: 'Prompt and input timing', fields: [{ ...numeric('clearDigits', 'Clear digits after'), value: '100', maxLength: null }] }],
  } : { supported: false, reason: 'Import a complete record to open its editor.', templates: [] }))
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ error: 'Fixture conflict' }, { status: 409 }))
    .mockImplementationOnce(async () => { created = true; return new Response(null, { status: 204 }) })
  render(<NativeTableEditor setId="fixture" number={30} revision={0} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={saved} onDirty={dirty} />)
  const submit = await screen.findByRole('button', { name: 'Create operational record' })
  expect(submit).toBeDisabled()
  expect(screen.getByLabelText(/Clear digits after/)).toHaveValue(null)
  screen.getByText('Import reviewed named settings').closest('details')!.open = true
  fireEvent.change(screen.getByLabelText('Named settings JSON'), { target: { value: '{"unknown":1}' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review named settings' }))
  expect(screen.getByRole('alert')).toHaveTextContent('Unknown setting')
  fireEvent.change(screen.getByLabelText('Named settings JSON'), { target: { value: '{"clearDigits":100}' } })
  fireEvent.click(screen.getByRole('button', { name: 'Review named settings' }))
  expect(screen.getByRole('region', { name: 'Named settings review' })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Apply reviewed settings' }))
  expect(screen.getByLabelText(/Clear digits after/)).toHaveValue(100)
  expect(submit).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Settings section'), { target: { value: 'display' } })
  fireEvent.change(screen.getByLabelText(/Display scroll timing/), { target: { value: '40000' } })
  expect(submit).toBeDisabled()
  fireEvent.change(screen.getByLabelText(/Display scroll timing/), { target: { value: '25' } })
  expect(submit).toBeEnabled()
  expect(dirty).toHaveBeenLastCalledWith(true)
  fireEvent.click(submit)
  expect(await screen.findByText('Fixture conflict')).toBeVisible()
  expect(screen.getByLabelText(/Display scroll timing/)).toHaveValue(25)
  fireEvent.click(submit)
  await waitFor(() => expect(saved).toHaveBeenCalledTimes(1))
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/fixture/records/30/operational-authoring', {
    profile: timing.profile, length: 68, fields: { clearDigits: '100', scrollSpeed: '25' },
  })
  expect(dirty).toHaveBeenLastCalledWith(false)
  await waitFor(() => expect(screen.queryByRole('form', { name: 'Create operational record' })).toBeNull())
})

it('keeps unbound modem profiles independent and sets only unentered switches off when requested', () => {
  const create = vi.fn()
  render(<OperationalDraftForm templates={[modem(false), modem(true)]} busy={false} onCreate={create} onDirty={vi.fn()} />)
  expect(screen.getByRole('button', { name: 'Create operational record' })).toBeDisabled()
  const profile = screen.getByLabelText('Operational layout')
  fireEvent.change(profile, { target: { value: 'MTR 1.9 packed' } })
  fireEvent.change(screen.getByLabelText(/Enable carrier-loss timeout/), { target: { value: 'true' } })
  fireEvent.click(screen.getByRole('button', { name: 'Set unentered switches to Off' }))
  expect(screen.getByLabelText(/Enable carrier-loss timeout/)).toHaveValue('true')
  expect(screen.getByLabelText(/Enable answer tone detection/)).toHaveValue('false')
  expect(screen.queryByLabelText(/Delay after answer tone removal/)).toBeNull()
  fireEvent.change(profile, { target: { value: 'Later packed profile' } })
  expect(screen.getByLabelText(/Enable carrier-loss timeout/)).toHaveValue('')
  expect(screen.getByLabelText(/Delay after answer tone removal/)).toBeVisible()
  fireEvent.change(profile, { target: { value: 'MTR 1.9 packed' } })
  expect(screen.getByLabelText(/Enable carrier-loss timeout/)).toHaveValue('true')
  fireEvent.click(screen.getByRole('button', { name: 'Create operational record' }))
  expect(create).toHaveBeenCalledWith({ profile: 'MTR 1.9 packed', length: 36, fields: { carrierLossTimeout: 'true', answerToneDetection: 'false' } })
})

it('does not load authoring choices for a read-only operator', async () => {
  consoleSession.save('fixture-token', '', 'Fixture reader', 'Operator', { 'table-records': 1 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json({ supported: false, reason: 'Import a complete record to open its editor.', templates: [] }))
  render(<NativeTableEditor setId="fixture" number={30} revision={0} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
  expect(await screen.findByText('Import a complete record to open its editor.')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create operational record' })).toBeNull()
  expect(apiFetch).toHaveBeenCalledTimes(1)
})

it('explains that an observed-length-only target still needs an independently reviewed import', async () => {
  consoleSession.save('fixture-token', '', 'Fixture administrator', 'Admin')
  vi.mocked(apiFetch).mockImplementation(async url => Response.json(String(url).includes('operational-authoring')
    ? { templates: [] } : { supported: false, reason: 'Import a complete record to open its editor.', templates: [], observedLengthsOnly: true }))
  render(<NativeTableEditor setId="fixture" number={26} revision={0} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
  expect(await screen.findByText(/No confirmed authoring layout is available/)).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create operational record' })).toBeNull()
})

it('creates MT31 with explicit blank predial choices and separately entered protected values', async () => {
  consoleSession.save('fixture-token', '', 'Fixture administrator', 'Admin')
  const credential = (key: string, label: string, maxLength: number) => ({ ...numeric(key, label), editor: 'password' as const, maxLength })
  const digits = (key: string, label: string) => ({ ...numeric(key, label), editor: 'text' as const, maxLength: 8 })
  const spec: OperationalSpec = { number: 31, name: 'Installation and servicing', profile: 'MTR 1.9 packed', length: 37,
    sections: [{ key: 'predial', label: 'Predial strings', fields: [digits('predial', 'Primary predial digits'),
      digits('alternatePredial', 'Alternate predial digits'), flag('usePredial', 'Enable predial')] },
    { key: 'craft', label: 'Craft credentials', fields: [credential('craftAccessCode', 'Craft access code', 7), credential('keyCardNumber', 'Desk terminal key card number', 10)] }] }
  const access = '2'.repeat(7), card = '3'.repeat(10)
  const saved = vi.fn(), dirty = vi.fn(); let created = false
  vi.mocked(apiFetch).mockImplementation(async url => Response.json(String(url).includes('operational-authoring') ? { templates: [spec] } : created ? {
    supported: true, editable: true, profile: spec.profile, length: spec.length, revision: 1, sha256: 'fixture-hash',
    sections: [{ key: 'craft', label: 'Craft credentials', fields: spec.sections[1].fields.map(field => ({ ...field, value: null })) }],
  } : { supported: false, reason: 'Import a complete record to open its editor.', templates: [] }))
  vi.mocked(apiPostRaw).mockImplementation(async () => { created = true; return new Response(null, { status: 204 }) })
  render(<NativeTableEditor setId="fixture" number={31} revision={0} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={saved} onDirty={dirty} />)
  const submit = await screen.findByRole('button', { name: 'Create operational record' })
  fireEvent.click(screen.getByRole('button', { name: 'Use no digits for Primary predial digits' }))
  expect(dirty).toHaveBeenLastCalledWith(true)
  expect(screen.getByText(/predial · Explicitly blank/)).toBeVisible()
  fireEvent.change(screen.getByLabelText('Alternate predial digits'), { target: { value: '*0#' } })
  fireEvent.click(screen.getByRole('button', { name: 'Set unentered switches to Off' }))
  expect(submit).toBeDisabled()
  screen.getByText('Import reviewed named settings').closest('details')!.open = true
  const json = screen.getByLabelText('Named settings JSON')
  fireEvent.change(json, { target: { value: JSON.stringify({ craftAccessCode: access }) } })
  fireEvent.click(screen.getByRole('button', { name: 'Review named settings' }))
  expect(screen.getByRole('alert')).toHaveTextContent('Protected configuration values')
  expect(json).toHaveValue('')
  expect(screen.queryByRole('region', { name: 'Named settings review' })).toBeNull()
  fireEvent.change(screen.getByLabelText('Settings section'), { target: { value: 'craft' } })
  const accessInput = screen.getByLabelText(/Craft access code/), cardInput = screen.getByLabelText(/Desk terminal key card number/)
  expect(accessInput).toHaveAttribute('type', 'password')
  expect(cardInput).toHaveAttribute('type', 'password')
  fireEvent.change(accessInput, { target: { value: access.slice(1) } })
  fireEvent.change(cardInput, { target: { value: card } })
  expect(submit).toBeDisabled()
  fireEvent.change(accessInput, { target: { value: access } })
  expect(submit).toBeEnabled()
  expect(screen.queryByText(access)).toBeNull()
  expect(screen.queryByText(card)).toBeNull()
  fireEvent.click(submit)
  await waitFor(() => expect(saved).toHaveBeenCalledTimes(1))
  expect(apiPostRaw).toHaveBeenCalledWith('/api/tables/sets/fixture/records/31/operational-authoring', {
    profile: spec.profile, length: 37, fields: { predial: '', alternatePredial: '*0#', usePredial: 'false' },
    credentials: { craftAccessCode: access, keyCardNumber: card },
  })
  await waitFor(() => expect(screen.queryByRole('form', { name: 'Create operational record' })).toBeNull())
})

it('authors MT62 with an explicit dialing graph, formatting choice and supported currency glyphs', () => {
  const early: OperationalSpec = { number: 62, name: 'Numbering and display-format plan', profile: 'MTR 1.9 packed', length: 353, numberingFormatCapacity: 132,
    sections: [{ key: 'numbering', label: 'Numbering', fields: [numeric('screenTimeout','Screening digit timeout'),
      { ...numeric('currencySymbol','Currency symbol'), editor: 'text', displayGlyphs: true, maxLength: 4 },
      { ...numeric('keyCardNumber','Desk terminal key card number'), editor: 'password', maxLength: 10 }] }] }
  const later: OperationalSpec = { ...early, profile: 'Later packed profile', numberingFormatCapacity: 140,
    sections: [{ ...early.sections[0], fields: early.sections[0].fields.filter(field => field.key !== 'currencySymbol') }] }
  const create = vi.fn(), dirty = vi.fn()
  render(<OperationalDraftForm templates={[early,later]} busy={false} onCreate={create} onDirty={dirty} />)
  const profile = screen.getByLabelText('Operational layout')
  const submit = screen.getByRole('button',{name:'Create operational record'})
  fireEvent.change(profile,{target:{value:early.profile}})
  fireEvent.change(screen.getByLabelText(/Screening digit timeout/),{target:{value:'300'}})
  fireEvent.change(screen.getByLabelText(/Desk terminal key card number/),{target:{value:'3'.repeat(10)}})
  fireEvent.change(screen.getByLabelText('Currency symbol'),{target:{value:'€'}})
  expect(screen.getByLabelText('Currency symbol')).toHaveAttribute('aria-invalid','true')
  fireEvent.change(screen.getByLabelText('Currency symbol'),{target:{value:'¢'}})
  expect(submit).toBeDisabled()
  fireEvent.click(screen.getByRole('button',{name:'Add rule'}))
  fireEvent.change(screen.getByLabelText('Call classification'),{target:{value:'3'}})
  fireEvent.change(screen.getByLabelText('Payment classification'),{target:{value:'4'}})
  fireEvent.change(screen.getByLabelText('Digit group'),{target:{value:'3'}})
  expect(submit).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Rule set'),{target:{value:'format'}})
  expect(screen.getByText('0 of 132 rule bytes used')).toBeVisible()
  fireEvent.click(screen.getByRole('button',{name:'Leave display formatting empty'}))
  expect(submit).toBeEnabled()
  fireEvent.change(profile,{target:{value:later.profile}})
  expect(screen.queryByLabelText('Currency symbol')).toBeNull()
  fireEvent.change(screen.getByLabelText('Rule set'),{target:{value:'format'}})
  expect(screen.getByText('0 of 140 rule bytes used')).toBeVisible()
  expect(submit).toBeDisabled()
  fireEvent.change(profile,{target:{value:early.profile}})
  expect(submit).toBeEnabled()
  fireEvent.click(submit)
  expect(create).toHaveBeenCalledWith({profile:early.profile,length:353,fields:{screenTimeout:'300',currencySymbol:'¢'},
    credentials:{keyCardNumber:'3'.repeat(10)},numberingPlan:{dial:[expect.objectContaining({callType:67,tag:48,digits:[17]})],format:[]}})
  expect(dirty).toHaveBeenLastCalledWith(true)
})

it('warns about broad ordinary no-payment classification but not exact free-service rules', () => {
  const broad = {id:'ordinary',noMatch:null,match:null,control:0,tag:50,callType:35,digits:[18,17,17,17,17,17,17],conversion:null,maximumDigits:null}
  const plan: NumberingPlan = {dial:[broad],format:[]}
  const {rerender}=render(<NumberingPlanEditor plan={plan} changes={{}} editable={false} onChange={vi.fn()}/>)
  expect(screen.getByRole('status')).toHaveTextContent('A broad ordinary destination rule selects No payment')
  rerender(<NumberingPlanEditor plan={{dial:[{...broad,callType:67}],format:[]}} changes={{}} editable={false} onChange={vi.fn()}/>)
  expect(screen.queryByRole('status')).toBeNull()
  rerender(<NumberingPlanEditor plan={{dial:[{...broad,callType:41,digits:[2,3,4,5]}],format:[]}} changes={{}} editable={false} onChange={vi.fn()}/>)
  expect(screen.queryByRole('status')).toBeNull()
})
