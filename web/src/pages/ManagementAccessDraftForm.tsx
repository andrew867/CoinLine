import { useState } from 'react'

export type ManagementAccessSpec = { profile: string; length: number; managerDigits: number; supportsAdsi: boolean }
export type ManagementAccessInput = { terminalNumber: string; primary: string; alternate: string; cadId?: string; cpeId?: string }

export function ManagementAccessDraftForm({ templates, busy, onCreate, onDirty }: {
  templates: ManagementAccessSpec[]; busy: boolean
  onCreate: (request: { profile: string; length: number; managementAccess: ManagementAccessInput }) => void
  onDirty: (dirty: boolean) => void
}) {
  const [selected, setSelected] = useState(templates.length === 1 ? String(templates[0].length) : '')
  const [input, setInput] = useState<ManagementAccessInput>({ terminalNumber: '', primary: '', alternate: '', cadId: '', cpeId: '' })
  const spec = templates.find(template => String(template.length) === selected)
  const validNumber = (value: string, minimum: number, maximum: number) => /^[0-9]*$/.test(value) && value.length >= minimum && value.length <= maximum
  const validHex = (value = '') => !value || /^[0-9A-Fa-f]{8}$/.test(value)
  const valid = !!spec && validNumber(input.terminalNumber, 10, 10) && validNumber(input.primary, 1, spec.managerDigits)
    && validNumber(input.alternate, 0, spec.managerDigits) && (!spec.supportsAdsi || validHex(input.cadId) && validHex(input.cpeId))

  const update = (key: keyof ManagementAccessInput, value: string) => {
    const next = { ...input, [key]: value }
    setInput(next)
    onDirty(Object.values(next).some(Boolean))
  }

  return <form className="table-config-panel" aria-label="Create management access record" onSubmit={event => {
    event.preventDefault()
    if (!spec || !valid || busy) return
    const { terminalNumber, primary, alternate, cadId, cpeId } = input
    onCreate({ profile: spec.profile, length: spec.length, managementAccess: spec.supportsAdsi
      ? { terminalNumber, primary, alternate, cadId, cpeId } : { terminalNumber, primary, alternate } })
  }}>
    <h3>Author MT21 management access</h3>
    <p>Enter the actual site values. Creating this record saves a draft only; it does not register a phone, assign a set or initiate a download.</p>
    <label>Management-access layout<select value={selected} disabled={busy || templates.length === 1}
      onChange={event => setSelected(event.target.value)}>
      {!spec && <option value="">Choose a layout</option>}
      {templates.map(template => <option key={template.length} value={template.length}>{template.profile}, {template.length} bytes</option>)}
    </select></label>
    {spec && <div className="table-config-selectors">
      <label>Terminal wire ID (ten digits)<input value={input.terminalNumber} inputMode="numeric" maxLength={10}
        disabled={busy} required onChange={event => update('terminalNumber', event.target.value)} /></label>
      <label>Primary manager number<input value={input.primary} inputMode="numeric" maxLength={spec.managerDigits}
        disabled={busy} required onChange={event => update('primary', event.target.value)} /></label>
      <label>Alternate manager number (optional)<input value={input.alternate} inputMode="numeric" maxLength={spec.managerDigits}
        disabled={busy} onChange={event => update('alternate', event.target.value)} /></label>
      {spec.supportsAdsi && <>
        <label>ADSI CAD identifier (optional)<input value={input.cadId} maxLength={8} disabled={busy}
          onChange={event => update('cadId', event.target.value)} /></label>
        <label>ADSI CPE identifier (optional)<input value={input.cpeId} maxLength={8} disabled={busy}
          onChange={event => update('cpeId', event.target.value)} /></label>
        <small>Enter eight hexadecimal digits per ADSI identifier. Blank optional identifiers and unassigned bytes start at zero; no ADSI service is configured by those values.</small>
      </>}
      <small>Manager numbers use up to {spec.managerDigits} decimal digits in this layout. Include any required access prefix explicitly.</small>
    </div>}
    <button type="submit" disabled={!valid || busy}>{busy ? 'Creating…' : 'Create MT21 record'}</button>
  </form>
}
