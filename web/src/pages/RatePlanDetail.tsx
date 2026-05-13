import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiDelete, apiGet, apiPost, apiPut } from '../api/client'

type VerSummary = { id: string; versionNumber: number; status: number; publishedAtUtc: string | null }
type Detail = {
  id: string
  name: string
  mode: number
  customerId: string | null
  publishedVersionId: string | null
  versions: VerSummary[]
}

type RuleRow = {
  id: string
  priority: number
  matchKind: number
  pattern: string
  outcome: number
  ratePerMinuteUsd: number
  expression: string
}

type TariffRow = { id: string; name: string; ratePerMinuteUsd: number; notes: string }
type PrefixRow = { id: string; prefixDigits: string; tariffId: string | null; notes: string }
type BandRow = {
  id: string
  dayOfWeekMask: number
  startMinuteOfDay: number
  endMinuteOfDay: number
  tariffId: string | null
}

type VerDetail = {
  id: string
  versionNumber: number
  status: number
  rules: RuleRow[]
  tariffs: TariffRow[]
  destinationPrefixes: PrefixRow[]
  timeBands: BandRow[]
}

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

async function fetchVersionJson(id: string, planId: string): Promise<VerDetail> {
  const r = await fetch(`/api/rate-plans/${planId}/versions/${id}`, { headers })
  if (!r.ok) throw new Error(String(r.status))
  return (await r.json()) as VerDetail
}

export function RatePlanDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Detail | null>(null)
  const [planLoading, setPlanLoading] = useState(true)
  const [selVer, setSelVer] = useState<string | null>(null)
  const [verDetail, setVerDetail] = useState<VerDetail | null>(null)
  const [versionLoading, setVersionLoading] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  const [np, setNp] = useState(100)
  const [pat, setPat] = useState('888')
  const [out, setOut] = useState(0)
  const [rpm, setRpm] = useState(0.05)

  const [tarName, setTarName] = useState('Peak')
  const [tarRpm, setTarRpm] = useState(0.12)
  const [tarNotes, setTarNotes] = useState('')

  const [pfxDigits, setPfxDigits] = useState('1')
  const [pfxTariffId, setPfxTariffId] = useState<string>('')
  const [pfxNotes, setPfxNotes] = useState('')

  const [bandMask, setBandMask] = useState(127)
  const [bandStart, setBandStart] = useState(600)
  const [bandEnd, setBandEnd] = useState(780)
  const [bandTariffId, setBandTariffId] = useState<string>('')

  const load = () => {
    if (!id) return
    setPlanLoading(true)
    setErr(null)
    apiGet<Detail>(`/api/rate-plans/${id}`)
      .then(setD)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setPlanLoading(false))
  }

  useEffect(() => {
    load()
  }, [id])

  useEffect(() => {
    if (!id || !selVer) {
      setVerDetail(null)
      return
    }
    setVersionLoading(true)
    fetchVersionJson(selVer, id)
      .then(setVerDetail)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setVersionLoading(false))
  }, [id, selVer])

  async function refreshVersion() {
    if (!id || !selVer) return
    setVerDetail(await fetchVersionJson(selVer, id))
  }

  async function addVersion(e: FormEvent) {
    e.preventDefault()
    if (!id) return
    setErr(null)
    try {
      await apiPost<{ id: string }>(`/api/rate-plans/${id}/versions`, {})
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function addVersionClone(e: FormEvent) {
    e.preventDefault()
    if (!id || !selVer) return
    setErr(null)
    try {
      await apiPost<{ id: string }>(`/api/rate-plans/${id}/versions`, { cloneFromVersionId: selVer })
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function publish(versionId: string) {
    if (!id) return
    if (!window.confirm('Publish this version? It becomes the active snapshot for quotes.')) return
    setErr(null)
    try {
      await apiPost(`/api/rate-plans/${id}/publish`, { ratePlanVersionId: versionId, confirm: true })
      load()
      setSelVer(versionId)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function addRule(e: FormEvent) {
    e.preventDefault()
    if (!id || !selVer) return
    setErr(null)
    try {
      await apiPost(`/api/rate-plans/${id}/versions/${selVer}/rules`, {
        priority: np,
        matchKind: 0,
        pattern: pat,
        outcome: out,
        ratePerMinuteUsd: rpm,
        expression: '{}',
      })
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function addTariff(e: FormEvent) {
    e.preventDefault()
    if (!id || !selVer) return
    setErr(null)
    try {
      await apiPost(`/api/rate-plans/${id}/versions/${selVer}/tariffs`, {
        name: tarName,
        ratePerMinuteUsd: tarRpm,
        notes: tarNotes || undefined,
      })
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function saveTariff(t: TariffRow) {
    if (!id || !selVer) return
    setErr(null)
    try {
      await apiPut(`/api/tariffs/${t.id}`, {
        name: t.name,
        ratePerMinuteUsd: t.ratePerMinuteUsd,
        notes: t.notes || undefined,
      })
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function removeTariff(tariffId: string) {
    if (!window.confirm('Delete this tariff? It must not be referenced by prefixes or bands.')) return
    setErr(null)
    try {
      await apiDelete(`/api/tariffs/${tariffId}`)
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function addPrefix(e: FormEvent) {
    e.preventDefault()
    if (!id || !selVer) return
    setErr(null)
    try {
      await apiPost(`/api/rate-plans/${id}/versions/${selVer}/destination-prefixes`, {
        prefixDigits: pfxDigits,
        tariffId: pfxTariffId ? pfxTariffId : null,
        notes: pfxNotes || undefined,
      })
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function savePrefix(p: PrefixRow) {
    setErr(null)
    try {
      await apiPut(`/api/destination-prefixes/${p.id}`, {
        prefixDigits: p.prefixDigits,
        tariffId: p.tariffId,
        notes: p.notes || undefined,
      })
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function removePrefix(prefixId: string) {
    if (!window.confirm('Delete this destination prefix row?')) return
    setErr(null)
    try {
      await apiDelete(`/api/destination-prefixes/${prefixId}`)
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function addBand(e: FormEvent) {
    e.preventDefault()
    if (!id || !selVer) return
    setErr(null)
    try {
      await apiPost(`/api/rate-plans/${id}/versions/${selVer}/time-bands`, {
        dayOfWeekMask: bandMask,
        startMinuteOfDay: bandStart,
        endMinuteOfDay: bandEnd,
        tariffId: bandTariffId ? bandTariffId : null,
      })
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function saveBand(b: BandRow) {
    setErr(null)
    try {
      await apiPut(`/api/time-bands/${b.id}`, {
        dayOfWeekMask: b.dayOfWeekMask,
        startMinuteOfDay: b.startMinuteOfDay,
        endMinuteOfDay: b.endMinuteOfDay,
        tariffId: b.tariffId,
      })
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function removeBand(bandId: string) {
    if (!window.confirm('Delete this time band?')) return
    setErr(null)
    try {
      await apiDelete(`/api/time-bands/${bandId}`)
      await refreshVersion()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  if (!id) return null

  const draft = verDetail?.status === 0

  return (
    <div>
      <p>
        <Link to="/rate-plans">← Rate plans</Link>
      </p>
      {planLoading && <p role="status">Loading plan…</p>}
      {!planLoading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!planLoading && !d && !err && <p role="status">Plan not found.</p>}
      {d && (
        <>
          <h1>{d.name}</h1>
          <p style={{ color: '#a60', maxWidth: 720 }}>
            Editing rules and catalog rows is allowed only on <strong>draft</strong> versions. Publish to apply quotes.
            This UI does not claim firmware parity.
          </p>
          <p>
            Mode: {d.mode} · Published: {d.publishedVersionId ?? '—'}
          </p>
          <form onSubmit={addVersion} style={{ display: 'inline-block', marginRight: 12 }}>
            <button type="submit">New draft version</button>
          </form>
          {selVer && (
            <form onSubmit={addVersionClone} style={{ display: 'inline-block' }}>
              <button type="submit">New draft (copy selected version)</button>
            </form>
          )}
        </>
      )}
      <h2>Versions</h2>
      {d && d.versions.length === 0 && <p role="status">No versions yet — create a draft.</p>}
      <ul>
        {d?.versions.map((v) => (
          <li key={v.id}>
            <button type="button" onClick={() => setSelVer(v.id)}>
              v{v.versionNumber}
            </button>{' '}
            status={v.status} {v.publishedAtUtc && ` published ${v.publishedAtUtc}`}
            {v.status === 0 && (
              <button type="button" onClick={() => publish(v.id)}>
                Publish
              </button>
            )}
          </li>
        ))}
      </ul>
      {selVer && versionLoading && <p role="status">Loading version…</p>}
      {selVer && verDetail && (
        <div>
          <h3>Rules (version {verDetail.versionNumber})</h3>
          {!draft && <p style={{ color: '#666' }}>Published — rules are read-only.</p>}
          <table cellPadding={6} style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th align="left">Pri</th>
                <th align="left">Pattern</th>
                <th align="left">Outcome</th>
                <th align="left">$/min</th>
              </tr>
            </thead>
            <tbody>
              {verDetail.rules.length === 0 ? (
                <tr>
                  <td colSpan={4}>
                    <em>No rules — add one below (draft only).</em>
                  </td>
                </tr>
              ) : (
                verDetail.rules.map((r) => (
                  <tr key={r.id}>
                    <td>{r.priority}</td>
                    <td>{r.pattern}</td>
                    <td>{r.outcome}</td>
                    <td>{r.ratePerMinuteUsd}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
          {draft && (
            <form onSubmit={addRule} style={{ marginTop: 12 }}>
              <label>
                Priority{' '}
                <input type="number" value={np} onChange={(e) => setNp(+e.target.value)} />
              </label>{' '}
              <label>
                Pattern{' '}
                <input value={pat} onChange={(e) => setPat(e.target.value)} />
              </label>{' '}
              <label>
                Outcome (0=rated,1=block,2=free,3=emergency){' '}
                <input type="number" value={out} onChange={(e) => setOut(+e.target.value)} />
              </label>{' '}
              <label>
                $/min{' '}
                <input type="number" step="0.01" value={rpm} onChange={(e) => setRpm(+e.target.value)} />
              </label>{' '}
              <button type="submit">Add rule</button>
            </form>
          )}

          <h3 style={{ marginTop: 24 }}>Tariffs</h3>
          {!draft && <p style={{ color: '#666' }}>Published — catalog is read-only.</p>}
          <table cellPadding={6} style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th align="left">Name</th>
                <th align="left">$/min</th>
                <th align="left">Notes</th>
                {draft && <th align="left">Actions</th>}
              </tr>
            </thead>
            <tbody>
              {verDetail.tariffs.length === 0 ? (
                <tr>
                  <td colSpan={draft ? 4 : 3}>
                    <em>No tariffs — add below (draft).</em>
                  </td>
                </tr>
              ) : (
                verDetail.tariffs.map((t) => (
                  <tr key={t.id}>
                    {draft ? (
                      <>
                        <td>
                          <input
                            value={t.name}
                            onChange={(e) => {
                              const name = e.target.value
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      tariffs: vd.tariffs.map((x) => (x.id === t.id ? { ...x, name } : x)),
                                    }
                                  : vd
                              )
                            }}
                          />
                        </td>
                        <td>
                          <input
                            type="number"
                            step="0.01"
                            value={t.ratePerMinuteUsd}
                            onChange={(e) => {
                              const ratePerMinuteUsd = +e.target.value
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      tariffs: vd.tariffs.map((x) =>
                                        x.id === t.id ? { ...x, ratePerMinuteUsd } : x
                                      ),
                                    }
                                  : vd
                              )
                            }}
                          />
                        </td>
                        <td>
                          <input
                            value={t.notes}
                            onChange={(e) => {
                              const notes = e.target.value
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      tariffs: vd.tariffs.map((x) => (x.id === t.id ? { ...x, notes } : x)),
                                    }
                                  : vd
                              )
                            }}
                          />
                        </td>
                        <td>
                          <button type="button" onClick={() => saveTariff(t)}>
                            Save
                          </button>{' '}
                          <button type="button" onClick={() => removeTariff(t.id)}>
                            Delete
                          </button>
                        </td>
                      </>
                    ) : (
                      <>
                        <td>{t.name}</td>
                        <td>{t.ratePerMinuteUsd}</td>
                        <td>{t.notes}</td>
                      </>
                    )}
                  </tr>
                ))
              )}
            </tbody>
          </table>
          {draft && (
            <form onSubmit={addTariff} style={{ marginTop: 12 }}>
              <label>
                Name <input value={tarName} onChange={(e) => setTarName(e.target.value)} />
              </label>{' '}
              <label>
                $/min{' '}
                <input type="number" step="0.01" value={tarRpm} onChange={(e) => setTarRpm(+e.target.value)} />
              </label>{' '}
              <label>
                Notes <input value={tarNotes} onChange={(e) => setTarNotes(e.target.value)} />
              </label>{' '}
              <button type="submit">Add tariff</button>
            </form>
          )}

          <h3 style={{ marginTop: 24 }}>Destination prefixes</h3>
          <p style={{ maxWidth: 720, fontSize: 14, color: '#444' }}>
            Longest matching prefix wins for tariff lookup (see rating engine). Optional tariff links this prefix to a catalog row.
          </p>
          <table cellPadding={6} style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th align="left">Digits</th>
                <th align="left">Tariff</th>
                <th align="left">Notes</th>
                {draft && <th align="left">Actions</th>}
              </tr>
            </thead>
            <tbody>
              {verDetail.destinationPrefixes.length === 0 ? (
                <tr>
                  <td colSpan={draft ? 4 : 3}>
                    <em>No prefixes.</em>
                  </td>
                </tr>
              ) : (
                verDetail.destinationPrefixes.map((p) => (
                  <tr key={p.id}>
                    {draft ? (
                      <>
                        <td>
                          <input
                            value={p.prefixDigits}
                            onChange={(e) => {
                              const prefixDigits = e.target.value
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      destinationPrefixes: vd.destinationPrefixes.map((x) =>
                                        x.id === p.id ? { ...x, prefixDigits } : x
                                      ),
                                    }
                                  : vd
                              )
                            }}
                          />
                        </td>
                        <td>
                          <select
                            value={p.tariffId ?? ''}
                            onChange={(e) => {
                              const v = e.target.value
                              const tariffId = v === '' ? null : v
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      destinationPrefixes: vd.destinationPrefixes.map((x) =>
                                        x.id === p.id ? { ...x, tariffId } : x
                                      ),
                                    }
                                  : vd
                              )
                            }}
                          >
                            <option value="">—</option>
                            {verDetail.tariffs.map((t) => (
                              <option key={t.id} value={t.id}>
                                {t.name}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>
                          <input
                            value={p.notes}
                            onChange={(e) => {
                              const notes = e.target.value
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      destinationPrefixes: vd.destinationPrefixes.map((x) =>
                                        x.id === p.id ? { ...x, notes } : x
                                      ),
                                    }
                                  : vd
                              )
                            }}
                          />
                        </td>
                        <td>
                          <button type="button" onClick={() => savePrefix(p)}>
                            Save
                          </button>{' '}
                          <button type="button" onClick={() => removePrefix(p.id)}>
                            Delete
                          </button>
                        </td>
                      </>
                    ) : (
                      <>
                        <td>{p.prefixDigits}</td>
                        <td>{p.tariffId ?? '—'}</td>
                        <td>{p.notes}</td>
                      </>
                    )}
                  </tr>
                ))
              )}
            </tbody>
          </table>
          {draft && (
            <form onSubmit={addPrefix} style={{ marginTop: 12 }}>
              <label>
                Prefix digits{' '}
                <input value={pfxDigits} onChange={(e) => setPfxDigits(e.target.value)} />
              </label>{' '}
              <label>
                Tariff{' '}
                <select value={pfxTariffId} onChange={(e) => setPfxTariffId(e.target.value)}>
                  <option value="">—</option>
                  {verDetail.tariffs.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </label>{' '}
              <label>
                Notes <input value={pfxNotes} onChange={(e) => setPfxNotes(e.target.value)} />
              </label>{' '}
              <button type="submit">Add prefix</button>
            </form>
          )}

          <h3 style={{ marginTop: 24 }}>Time bands</h3>
          <p style={{ maxWidth: 720, fontSize: 14, color: '#444' }}>
            Day mask 1–127 (Sun bit … Sat bit). Minutes 0–1440 (end exclusive). When a band matches, it overrides destination tariff for airtime.
          </p>
          <table cellPadding={6} style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th align="left">Mask</th>
                <th align="left">Start</th>
                <th align="left">End</th>
                <th align="left">Tariff</th>
                {draft && <th align="left">Actions</th>}
              </tr>
            </thead>
            <tbody>
              {verDetail.timeBands.length === 0 ? (
                <tr>
                  <td colSpan={draft ? 5 : 4}>
                    <em>No time bands.</em>
                  </td>
                </tr>
              ) : (
                verDetail.timeBands.map((b) => (
                  <tr key={b.id}>
                    {draft ? (
                      <>
                        <td>
                          <input
                            type="number"
                            value={b.dayOfWeekMask}
                            onChange={(e) => {
                              const dayOfWeekMask = +e.target.value
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      timeBands: vd.timeBands.map((x) =>
                                        x.id === b.id ? { ...x, dayOfWeekMask } : x
                                      ),
                                    }
                                  : vd
                              )
                            }}
                          />
                        </td>
                        <td>
                          <input
                            type="number"
                            value={b.startMinuteOfDay}
                            onChange={(e) => {
                              const startMinuteOfDay = +e.target.value
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      timeBands: vd.timeBands.map((x) =>
                                        x.id === b.id ? { ...x, startMinuteOfDay } : x
                                      ),
                                    }
                                  : vd
                              )
                            }}
                          />
                        </td>
                        <td>
                          <input
                            type="number"
                            value={b.endMinuteOfDay}
                            onChange={(e) => {
                              const endMinuteOfDay = +e.target.value
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      timeBands: vd.timeBands.map((x) =>
                                        x.id === b.id ? { ...x, endMinuteOfDay } : x
                                      ),
                                    }
                                  : vd
                              )
                            }}
                          />
                        </td>
                        <td>
                          <select
                            value={b.tariffId ?? ''}
                            onChange={(e) => {
                              const v = e.target.value
                              const tariffId = v === '' ? null : v
                              setVerDetail((vd) =>
                                vd
                                  ? {
                                      ...vd,
                                      timeBands: vd.timeBands.map((x) =>
                                        x.id === b.id ? { ...x, tariffId } : x
                                      ),
                                    }
                                  : vd
                              )
                            }}
                          >
                            <option value="">—</option>
                            {verDetail.tariffs.map((t) => (
                              <option key={t.id} value={t.id}>
                                {t.name}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>
                          <button type="button" onClick={() => saveBand(b)}>
                            Save
                          </button>{' '}
                          <button type="button" onClick={() => removeBand(b.id)}>
                            Delete
                          </button>
                        </td>
                      </>
                    ) : (
                      <>
                        <td>{b.dayOfWeekMask}</td>
                        <td>{b.startMinuteOfDay}</td>
                        <td>{b.endMinuteOfDay}</td>
                        <td>{b.tariffId ?? '—'}</td>
                      </>
                    )}
                  </tr>
                ))
              )}
            </tbody>
          </table>
          {draft && (
            <form onSubmit={addBand} style={{ marginTop: 12 }}>
              <label>
                Mask{' '}
                <input type="number" value={bandMask} onChange={(e) => setBandMask(+e.target.value)} />
              </label>{' '}
              <label>
                Start min{' '}
                <input type="number" value={bandStart} onChange={(e) => setBandStart(+e.target.value)} />
              </label>{' '}
              <label>
                End min{' '}
                <input type="number" value={bandEnd} onChange={(e) => setBandEnd(+e.target.value)} />
              </label>{' '}
              <label>
                Tariff{' '}
                <select value={bandTariffId} onChange={(e) => setBandTariffId(e.target.value)}>
                  <option value="">—</option>
                  {verDetail.tariffs.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </label>{' '}
              <button type="submit">Add time band</button>
            </form>
          )}
        </div>
      )}
    </div>
  )
}
