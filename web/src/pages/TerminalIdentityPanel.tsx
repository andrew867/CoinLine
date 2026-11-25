import { useEffect, useState, type FormEvent } from 'react'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import './TerminalInventoryPanel.css'

type PreviousValue = { value: string | null; changedAtUtc: string }
type Identity = {
  terminalIdHex: string
  previousId: PreviousValue | null
  previousType: PreviousValue | null
  firstContactAtUtc: string | null
}

function ChangeTime({ value }: { value: string }) {
  return <time dateTime={value}>{new Date(value).toLocaleString()}</time>
}

export function TerminalIdentityPanel({ terminalId, wireId, family }: {
  terminalId: string; wireId: string; family: string | null
}) {
  const endpoint = `/api/terminals/${terminalId}/identity`
  const canUpdate = canPerform('phones', op.update)
  const [identity, setIdentity] = useState<Identity | null>(null)
  const [edit, setEdit] = useState<{ expected: string; value: string } | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  useEffect(() => {
    let current = true
    void apiGet<Identity>(endpoint).then(data => { if (current) setIdentity(data) })
      .catch(() => { if (current) setError('Could not load terminal identity history.') })
    return () => { current = false }
  }, [endpoint, family, refresh])

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!edit || !canUpdate) return
    setSaving(true)
    setError('')
    setNotice('')
    try {
      const response = await apiFetch(endpoint, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ terminalIdHex: edit.value, expectedTerminalIdHex: edit.expected }),
      })
      if (!response.ok) {
        const problem = await response.json().catch(() => null)
        throw new Error(problem?.error || `Could not save the terminal ID (HTTP ${response.status}).`)
      }
      setEdit(null)
      setRefresh(value => value + 1)
      setNotice('Manager terminal ID saved.')
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not save the terminal ID.')
    } finally {
      setSaving(false)
    }
  }

  return <section className="table-config-panel phone-inventory" aria-labelledby="terminal-identity-heading">
    <h2 id="terminal-identity-heading">Terminal identity history</h2>
    <dl className="phone-inventory-facts">
      <div><dt>Manager terminal ID</dt><dd><code>{(identity?.terminalIdHex ?? wireId) || 'Unspecified'}</code></dd></div>
      {identity && <>
        <div><dt>Previous terminal ID</dt><dd>
          {identity.previousId ? <><code>{identity.previousId.value || 'Unspecified'}</code>
            <small>Changed in Manager <ChangeTime value={identity.previousId.changedAtUtc} /></small></>
            : 'No recorded change'}
        </dd></div>
        <div><dt>Previous terminal type</dt><dd>
          {identity.previousType ? <><span>{identity.previousType.value || 'Unspecified'}</span>
            <small>Changed in Manager <ChangeTime value={identity.previousType.changedAtUtc} /></small></>
            : 'No recorded change'}
        </dd></div>
        {identity.previousId && <div><dt>First NCC contact since the ID change</dt><dd>
          {identity.firstContactAtUtc ? <ChangeTime value={identity.firstContactAtUtc} /> : 'Awaiting a matched contact'}
        </dd></div>}
      </>}
    </dl>
    {edit ? <form onSubmit={event => void save(event)}>
      <p className="page-intro">This changes the ID CoinLine matches on NCC. Set the same ID on the phone to reconnect.</p>
      <div className="phone-inventory-actions">
        <label>New terminal ID <input data-field-kind="key" value={edit.value} required pattern="[0-9A-Fa-f]{10}" maxLength={10}
          disabled={saving} onChange={event => setEdit({ ...edit, value: event.target.value.toUpperCase() })} /></label>
        <button disabled={saving || edit.value === edit.expected.toUpperCase()}>Save terminal ID</button>
        <button type="button" className="quiet" disabled={saving} onClick={() => { setEdit(null); setError('') }}>Cancel</button>
      </div>
    </form> : <div className="phone-inventory-actions">
      {identity && <button type="button" disabled={!canUpdate} onClick={() => {
        setError('')
        setNotice('')
        setEdit({ expected: identity.terminalIdHex, value: identity.terminalIdHex })
      }}>Edit terminal ID</button>}
      <button type="button" className="quiet" onClick={() => { setError(''); setRefresh(value => value + 1) }}>Refresh history</button>
    </div>}
    {error && <p role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {!identity && !error && <p role="status">Loading terminal identity…</p>}
  </section>
}
