import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type Assignment = {
  eligibleFamily: boolean
  featureConfigurationId: string | null
  featureConfigurationName: string | null
  setupConfigurationId: string | null
  revision: number | null
  featureConfigurations: { id: string; name: string }[]
  wireFormatVerified: boolean
}

export function InmateAssignmentPanel({ terminalId, family }: { terminalId: string; family: string | null }) {
  const path = `/api/terminals/${terminalId}/inmate-configuration`
  const canUpdate = canPerform('inmate-assignments', op.update)
  const canReadFeatures = canPerform('inmate-features', op.read)
  const [assignment, setAssignment] = useState<Assignment | null>(null)
  const [feature, setFeature] = useState('')
  const [setup, setSetup] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  function accept(value: Assignment) {
    setAssignment(value)
    setFeature(value.featureConfigurationId ?? '')
    setSetup(value.setupConfigurationId ?? '')
  }

  useEffect(() => {
    let current = true
    setAssignment(null)
    setError('')
    setNotice('')
    void apiGet<Assignment>(path).then(value => {
      if (current) accept(value)
    }).catch(() => {
      if (current) setError('Could not load the inmate configuration assignment.')
    })
    return () => { current = false }
  }, [path, family])

  async function save(featureId: string, setupId: string) {
    if (!assignment || !canUpdate) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const response = await apiFetch(path, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          featureConfigurationId: featureId || null, setupConfigurationId: setupId || null,
          revision: assignment.revision,
        }),
      })
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? `Could not save assignment (HTTP ${response.status}).`)
      accept(await response.json() as Assignment)
      setNotice('Manager configuration references saved. No table download was created.')
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not save assignment.')
    } finally {
      setBusy(false)
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    void save(feature, setup)
  }

  const stored = !!(assignment?.featureConfigurationId || assignment?.setupConfigurationId)
  const isInmate = family === 'msr17-inmate'
  if (!isInmate && !assignment?.eligibleFamily && !stored && !notice) return null
  return <section className="table-config-panel" aria-labelledby="inmate-assignment-heading">
    <h2 id="inmate-assignment-heading">Inmate terminal configuration</h2>
    <p className="page-intro">References for the terminal's Inmate Feature and Inmate Setup configurations.</p>
    {error && <p role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {!assignment && !error && <p role="status">Loading inmate configuration…</p>}
    {assignment && <>
      {!assignment.wireFormatVerified && <p className="page-intro">These are Manager-side assignments only. The packed terminal record formats are not verified, so no download is generated.</p>}
      {assignment.eligibleFamily ? <form onSubmit={submit} className="stack-form">
        <label>Inmate Feature configuration
          <select value={feature} disabled={busy || !canUpdate} onChange={event => { setFeature(event.target.value); setNotice('') }}>
            <option value="">Not assigned</option>
            {assignment.featureConfigurations.map(option => <option value={option.id} key={option.id}>{option.name}</option>)}
          </select>
        </label>
        {canReadFeatures && <Link to="/inmate-features">Manage Inmate Feature configurations</Link>}
        <label>External Inmate Setup Configuration ID
          <input data-field-kind="key" value={setup} disabled={busy || !canUpdate} maxLength={6}
            aria-describedby="inmate-setup-help"
            onChange={event => { setSetup(event.target.value); setNotice('') }} />
        </label>
        <small id="inmate-setup-help">Enter the configuration identifier supplied by the separate inmate setup system. The setup contents are not stored here.</small>
        <div className="row-actions">
          <button type="submit" disabled={busy || !canUpdate ||
            (feature === (assignment.featureConfigurationId ?? '') && setup === (assignment.setupConfigurationId ?? ''))}>
            Save inmate configuration
          </button>
        </div>
      </form> : stored && <>
        <p>This phone is no longer assigned to an Inmate family. Its previous references are retained but inactive.</p>
        <dl>
          <dt>Inmate Feature configuration</dt><dd>{assignment.featureConfigurationName ?? 'Not assigned'}</dd>
          <dt>External Inmate Setup Configuration ID</dt><dd>{assignment.setupConfigurationId ?? 'Not assigned'}</dd>
        </dl>
        <button type="button" disabled={busy || !canUpdate} onClick={() => void save('', '')}>Clear retained references</button>
      </>}
    </>}
  </section>
}
