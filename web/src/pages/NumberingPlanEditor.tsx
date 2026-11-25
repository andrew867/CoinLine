import { useState } from 'react'

type Rule = {
  id: string
  noMatch: string | null
  match: string | null
  control: number
  tag: number
  callType: number
  digits: number[]
  conversion: string | null
  maximumDigits: number | null
}

type RuleSet = 'dial' | 'format'
export type NumberingPlan = Record<RuleSet, Rule[] | null>
export type NumberingPlanChanges = Partial<Record<RuleSet, Rule[]>>

type Choice = readonly [number, string]
const digits: Choice[] = [
  ...Array.from({ length: 9 }, (_, index) => [index + 1, String(index + 1)] as const),
  [10, '0'], [11, '*'], [12, '#'], [16, '0 or 1'], [17, 'Any decimal digit'], [18, '2 through 9'],
  [19, '0 or 2 through 9'], [20, 'Any keypad digit'],
]
const callTypes: Choice[] = [
  [0, 'Incoming'], [1, 'Unanswered'], [2, 'Abandoned'], [3, 'Local'], [4, 'Intra-area toll'], [5, 'Inter-area toll'],
  [6, 'International'], [7, 'Operator'], [8, 'Operator-assisted 0+'], [9, 'Toll-free'], [10, 'Directory assistance'],
  [11, 'Denied'], [15, 'No classification'],
]
const paymentTypes: Choice[] = [[0, 'Unspecified'], [2, 'No payment'], [3, 'Coin'], [4, 'Credit card'],
  [5, 'Calling card'], [6, 'Stored-value card'], [7, 'Debit card']]
const dialFlags: Choice[] = [[1, 'Try this rule during backtracking'], [4, 'Wait for the digit timeout'], [8, 'Remove the terminating key'],
  [32, 'Use network billing'], [64, 'Dial the configured terminating key'], [128, 'Mute until answer supervision']]
const formatFlags: Choice[] = [[1, 'Defer separator insertion'], [2, 'Suppress the deferred separator'],
  [4, 'Require exact length'], [8, 'Complete formatting here']]

const flag = (value: number, mask: number, enabled: boolean) => enabled ? value | mask : value & ~mask
const labelFor = (index: number) => index === 0 ? 'Rule 1 (start)' : `Rule ${index + 1}`

export function NumberingPlanEditor({ plan, changes, editable, onChange }: {
  plan: NumberingPlan
  changes: NumberingPlanChanges
  editable: boolean
  onChange: (group: RuleSet, rules: Rule[]) => void
}) {
  const [group, setGroup] = useState<RuleSet>('dial')
  const [selected, setSelected] = useState('')
  const dial = group === 'dial'
  const rules = changes[group] ?? plan[group]
  const index = Math.max(0, rules?.findIndex((rule) => rule.id === selected) ?? 0)
  const current = rules?.[index]
  const space = rules?.reduce((bytes, rule) => bytes + rule.digits.length + (dial ? 6 : 5)
    + (dial && (rule.conversion !== null || rule.maximumDigits !== null) ? 1 : 0), 0)

  function update(fields: Partial<Rule>) {
    if (current && rules) onChange(group, rules.map((rule) => rule.id === current.id ? { ...rule, ...fields } : rule))
  }

  function add() {
    if (!rules) return
    const id = `new-${globalThis.crypto?.randomUUID?.() ?? Date.now()}`
    const added: Rule = { id, noMatch: null, match: null, control: 0, tag: 0, callType: dial ? 15 : 0,
      digits: [17], conversion: null, maximumDigits: null }
    const updated = [...rules, added]
    if (current?.noMatch === null) updated[index] = { ...current, noMatch: id }
    onChange(group, updated)
    setSelected(id)
  }

  function remove() {
    if (!current || !rules) return
    onChange(group, rules.filter((rule) => rule.id !== current.id).map((rule) => ({
      ...rule,
      noMatch: rule.noMatch === current.id ? null : rule.noMatch,
      match: rule.match === current.id ? null : rule.match,
      conversion: rule.conversion === current.id ? null : rule.conversion,
      control: rule.conversion === current.id ? rule.control & ~16 : rule.control,
    })))
    setSelected(rules[index + 1]?.id ?? rules[index - 1]?.id ?? '')
  }

  function move(direction: -1 | 1) {
    if (!current || !rules || !rules[index + direction]) return
    const updated = [...rules]
    updated[index] = updated[index + direction]
    updated[index + direction] = current
    onChange(group, updated)
    setSelected(current.id)
  }

  return <div className="numbering-plan">
    <div className="table-config-selectors">
      <label>Rule set
        <select value={group} onChange={(event) => { setGroup(event.target.value as RuleSet); setSelected('') }}>
          <option value="dial">Dialing</option>
          <option value="format">Display formatting</option>
        </select>
      </label>
      {current && rules && <label>Rule
        <select value={current.id} onChange={(event) => setSelected(event.target.value)}>
          {rules.map((rule, position) => <option value={rule.id} key={rule.id}>{labelFor(position)}</option>)}
        </select>
      </label>}
      {space !== undefined && <small>{space} of {dial ? 180 : 132} rule bytes used</small>}
    </div>
    {rules === null && <p>This rule set uses an unrecognized wire layout.</p>}
    {rules && rules.length === 0 && <p>This rule set is empty.</p>}
    {rules && editable && <div className="row-actions">
      <button type="button" onClick={add}>Add rule</button>
      {current && <>
        <button type="button" disabled={index === 0} onClick={() => move(-1)}>Move up</button>
        <button type="button" disabled={index === rules.length - 1} onClick={() => move(1)}>Move down</button>
        <button type="button" disabled={dial && rules.length === 1} onClick={remove}>Remove rule</button>
      </>}
    </div>}
    {current && rules && <>
      <fieldset className="native-table-fields" disabled={!editable}>
        <legend>{labelFor(index)}</legend>
        <LinkField label="On match" value={current.match} rules={rules} allowed={(rule, position) => position > 0 && rule.id !== current.id}
          onChange={(match) => update({ match })} />
        <LinkField label="On mismatch" value={current.noMatch} rules={rules} allowed={(rule, position) => position > 0 && rule.id !== current.id}
          onChange={(noMatch) => update({ noMatch })} />
        {dial && <>
          <ChoiceField label="Call classification" value={current.callType & 15} choices={callTypes}
            onChange={(value) => update({ callType: (current.callType & 240) | value })} />
          <ChoiceField label="Payment classification" value={(current.callType >> 4) & 7} choices={paymentTypes}
            onChange={(value) => update({ callType: (current.callType & 143) | (value << 4) })} />
          <ChoiceField label="Digit group" value={current.tag >> 4}
            choices={[[0, 'Unspecified'], [1, 'Prefix'], [2, 'Carrier'], [3, 'Destination'], [4, 'Four-digit carrier']]}
            onChange={(value) => update({ tag: (current.tag & 15) | (value << 4) })} />
          <ChoiceField label="Digit group subtype" value={current.tag & 15}
            choices={[[0, 'Unspecified'], [1, 'Area code'], [2, 'Exchange'], [3, 'Local dialing index'], [4, 'Local dialing code']]}
            onChange={(value) => update({ tag: (current.tag & 240) | value })} />
          <label className="native-table-toggle"><input type="checkbox" checked={(current.callType & 128) !== 0}
            onChange={(event) => update({ callType: flag(current.callType, 128, event.target.checked) })} />International dialing</label>
          <LinkField label="Convert to prefix" value={current.conversion} rules={rules} allowed={(rule) => (rule.tag >> 4) === 1}
            onChange={(conversion) => update({ conversion, maximumDigits: conversion === null ? current.maximumDigits : null,
              control: flag(conversion === null ? current.control : current.control & ~2, 16, conversion !== null) })} />
          <label className="native-table-field">Maximum digits for variable-length match
            <input type="number" min={current.digits.length + 1} max="255" value={current.maximumDigits ?? ''}
              onChange={(event) => {
                const maximumDigits = event.target.value === '' ? null : Number(event.target.value)
                update({ maximumDigits, conversion: maximumDigits === null ? current.conversion : null,
                  control: flag(maximumDigits === null ? current.control : current.control & ~16, 2, maximumDigits !== null) })
              }} />
          </label>
        </>}
        {(dial ? dialFlags : formatFlags).map(([mask, label]) => <label className="native-table-toggle" key={mask}>
          <input type="checkbox" checked={(current.control & mask) !== 0}
            onChange={(event) => update({ control: flag(current.control, mask, event.target.checked) })} />{label}
        </label>)}
      </fieldset>
      <fieldset className="native-table-fields numbering-digits" disabled={!editable}>
        <legend>Digit pattern</legend>
        {current.digits.map((digit, position) => {
          const change = (value: number) => update({ digits: current.digits.map((item, at) => at === position ? value : item) })
          return <div className="numbering-digit" key={position}>
            <ChoiceField label={`Digit ${position + 1}`} value={digit & 31} choices={digits}
              onChange={(value) => change((digit & 224) | value)} />
            {!dial && <>
              <label className="native-table-toggle"><input type="checkbox" checked={(digit & 64) !== 0}
                onChange={(event) => change(flag(digit, 64, event.target.checked))} />Separator before</label>
              <label className="native-table-toggle"><input type="checkbox" checked={(digit & 128) !== 0}
                onChange={(event) => change(flag(digit, 128, event.target.checked))} />Separator after</label>
            </>}
            {editable && <button type="button" disabled={current.digits.length === 1} aria-label={`Remove digit ${position + 1}`}
              onClick={() => update({ digits: current.digits.filter((_, at) => at !== position) })}>Remove digit</button>}
          </div>
        })}
        {editable && <div className="row-actions"><button type="button"
          onClick={() => update({ digits: [...current.digits, 17] })}>Add digit</button></div>}
      </fieldset>
    </>}
  </div>
}

function ChoiceField({ label, value, choices, onChange }: {
  label: string; value: number; choices: Choice[]; onChange: (value: number) => void
}) {
  return <label className="native-table-field">{label}
    <select value={value} onChange={(event) => onChange(Number(event.target.value))}>
      {!choices.some(([choice]) => choice === value) && <option value={value}>Stored value {value}</option>}
      {choices.map(([choice, name]) => <option key={choice} value={choice}>{name}</option>)}
    </select>
  </label>
}

function LinkField({ label, value, rules, allowed, onChange }: {
  label: string; value: string | null; rules: Rule[]; allowed: (rule: Rule, index: number) => boolean
  onChange: (value: string | null) => void
}) {
  const options = rules.map((rule, index) => ({ rule, index })).filter(({ rule, index }) => allowed(rule, index))
  return <label className="native-table-field">{label}
    <select value={value ?? ''} onChange={(event) => onChange(event.target.value || null)}>
      <option value="">No link</option>
      {value && !options.some(({ rule }) => rule.id === value) && <option value={value}>Current link needs attention</option>}
      {options.map(({ rule, index }) => <option key={rule.id} value={rule.id}>{labelFor(index)}</option>)}
    </select>
  </label>
}
