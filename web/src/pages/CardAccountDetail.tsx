import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'

type Detail = {
  id: string
  cardProductId: string
  productCode: string
  terminalId: string | null
  panLast4Display: string
  credentialTokenMasked: string
  resolvedCardType: number
  credentialKind: number
  balance: number
  cardBalance: { amount: number; currency: string; updatedAtUtc?: string } | null
  smartcardProfile: { profileJson: string; smartcardTypeId: string | null } | null
  epurseProfile: { profileJson: string } | null
  simulationMode: boolean
}

export function CardAccountDetail() {
  const { id } = useParams<{ id: string }>()
  const [row, setRow] = useState<Detail | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [modal, setModal] = useState(false)
  const [delta, setDelta] = useState('1')
  const [reason, setReason] = useState('UI adjustment — audit trail')
  const [adjErr, setAdjErr] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [timeline, setTimeline] = useState<{ items: unknown[] } | null>(null)
  const [tlErr, setTlErr] = useState<string | null>(null)

  const load = () => {
    if (!id) return Promise.resolve()
    return apiGet<Detail>(`/api/cards/accounts/${id}`).then(setRow)
  }

  useEffect(() => {
    if (!id) {
      setLoading(false)
      setErr('Missing account id.')
      return
    }
    setLoading(true)
    setErr(null)
    load()
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
    setTlErr(null)
    apiGet<{ items: unknown[] }>(`/api/cards/accounts/${id}/timeline`)
      .then(setTimeline)
      .catch((e) => setTlErr(e instanceof Error ? e.message : 'timeline error'))
  }, [id])

  async function onAdjust(e: FormEvent) {
    e.preventDefault()
    if (!id) return
    setSaving(true)
    setAdjErr(null)
    try {
      await apiPost<{ balance: number }>(`/api/cards/accounts/${id}/adjust-balance`, {
        delta: Number(delta),
        reason,
        simulationMode: true,
      })
      setModal(false)
      await load()
    } catch (ex) {
      setAdjErr(ex instanceof Error ? ex.message : 'adjust failed')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <p>
        <Link to="/card-accounts">← Card accounts</Link>
      </p>
      <h1>Card account</h1>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && !row && <p role="status">Not found.</p>}
      {!loading && !err && row && (
        <>
          <p style={{ color: '#555' }}>
            Balances are simulation ledger rows — not issuer authoritative funds without HARDWARE_VALIDATION_REQUIRED.
          </p>
          <dl style={{ display: 'grid', gridTemplateColumns: '180px 1fr', gap: 8 }}>
            <dt>Product</dt>
            <dd>{row.productCode}</dd>
            <dt>PAN display</dt>
            <dd>{row.panLast4Display}</dd>
            <dt>Credential (masked)</dt>
            <dd>{row.credentialTokenMasked || '—'}</dd>
            <dt>Resolved type</dt>
            <dd>{row.resolvedCardType}</dd>
            <dt>Balance</dt>
            <dd>{row.balance}</dd>
            <dt>Ledger snapshot</dt>
            <dd>
              {row.cardBalance
                ? `${row.cardBalance.amount} ${row.cardBalance.currency}`
                : '—'}
            </dd>
          </dl>
          <button type="button" onClick={() => setModal(true)}>
            Adjust balance (audit reason required)
          </button>

          <section style={{ marginTop: 24 }}>
            <h2 style={{ fontSize: '1rem' }}>Balance / audit timeline</h2>
            <p style={{ fontSize: 13, color: '#555' }}>
              Ledger adjustments, payment transactions, card reads/writes, and matching audit rows — detail JSON preserved.
            </p>
            {tlErr && <p style={{ color: 'crimson' }}>{tlErr}</p>}
            {!timeline && !tlErr && <p role="status">Loading timeline…</p>}
            {timeline && timeline.items.length === 0 && !tlErr && (
              <p style={{ color: '#666' }}>No ledger events yet.</p>
            )}
            {timeline && timeline.items.length > 0 && (
              <ul style={{ fontSize: 13 }}>
                {timeline.items.map((it, i) => (
                  <li key={i} style={{ marginBottom: 8 }}>
                    <pre style={{ margin: 0, fontSize: 11, background: '#f6f8fa', padding: 8, overflow: 'auto' }}>
                      {JSON.stringify(it, null, 2)}
                    </pre>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {(row.smartcardProfile || row.epurseProfile) && (
            <section style={{ marginTop: 24 }}>
              <h2 style={{ fontSize: '1rem' }}>Profiles (opaque JSON)</h2>
              {row.smartcardProfile && (
                <pre style={{ background: '#f6f6f6', padding: 12, overflow: 'auto', fontSize: 12 }}>
                  {row.smartcardProfile.profileJson}
                </pre>
              )}
              {row.epurseProfile && (
                <pre style={{ background: '#f6f6f6', padding: 12, overflow: 'auto', fontSize: 12 }}>
                  {row.epurseProfile.profileJson}
                </pre>
              )}
            </section>
          )}

          {modal && (
            <div
              role="dialog"
              aria-modal="true"
              style={{
                position: 'fixed',
                inset: 0,
                background: 'rgba(0,0,0,0.35)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <form
                onSubmit={onAdjust}
                style={{
                  background: '#fff',
                  padding: 20,
                  borderRadius: 8,
                  minWidth: 320,
                  boxShadow: '0 4px 20px rgba(0,0,0,0.15)',
                }}
              >
                <h2 style={{ marginTop: 0 }}>Balance adjustment</h2>
                <p style={{ fontSize: 14 }}>Simulation-only until certification. Minimum 3-character audit reason.</p>
                {adjErr && <p style={{ color: 'crimson' }}>{adjErr}</p>}
                <label style={{ display: 'block', marginBottom: 8 }}>
                  Delta (negative allowed if product permits)
                  <input
                    type="number"
                    step="0.01"
                    value={delta}
                    onChange={(e) => setDelta(e.target.value)}
                    required
                    style={{ display: 'block', width: '100%' }}
                  />
                </label>
                <label style={{ display: 'block', marginBottom: 12 }}>
                  Audit reason
                  <input
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    required
                    minLength={3}
                    style={{ display: 'block', width: '100%' }}
                  />
                </label>
                <div style={{ display: 'flex', gap: 8 }}>
                  <button type="submit" disabled={saving}>
                    {saving ? 'Saving…' : 'Apply'}
                  </button>
                  <button type="button" onClick={() => setModal(false)}>
                    Cancel
                  </button>
                </div>
              </form>
            </div>
          )}
        </>
      )}
    </div>
  )
}
