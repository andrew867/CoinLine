import { useEffect, useState } from 'react'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'

type HealthJson = { status?: string; ts?: string }
type SipModem = {
  enabled?: boolean
  listening?: boolean
  sipPort?: number
  minimodemInstalled?: boolean
  audio?: string
  modem?: string
  nccPort?: number
  activeCalls?: number
  registrar?: string | null
  username?: string | null
  assignedNumber?: string | null
  registered?: boolean
  registrationNote?: string | null
  note?: string
}

type SerialModem = {
  enabled?: boolean
  watching?: boolean
  bridged?: boolean
  portName?: string | null
  baudRate?: number
  nccPort?: number
  activeBridges?: number
  phase?: string
  note?: string
}

export function StatusPage() {
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [health, setHealth] = useState<HealthJson | null>(null)
  const [live, setLive] = useState<HealthJson | null>(null)
  const [readyStatus, setReadyStatus] = useState<number | null>(null)
  const [readyBody, setReadyBody] = useState<string | null>(null)
  const [sip, setSip] = useState<SipModem | null>(null)
  const [serial, setSerial] = useState<SerialModem | null>(null)
  const [selfTest, setSelfTest] = useState<string | null>(null)
  const [selfTestBusy, setSelfTestBusy] = useState(false)
  const [hangupBusy, setHangupBusy] = useState(false)
  const [hangupMsg, setHangupMsg] = useState<string | null>(null)

  async function refreshSip() {
    const sipResponse = await fetch('/api/sip-modem')
    if (sipResponse.ok)
      setSip((await sipResponse.json()) as SipModem)
  }

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setErr(null)

    async function load() {
      try {
        const h = await fetch('/health')
        const l = await fetch('/health/live')
        const rd = await fetch('/ready')
        if (cancelled) return
        const hb = (await h.json()) as HealthJson
        const lb = (await l.json()) as HealthJson
        const rt = await rd.text()
        setHealth(hb)
        setLive(lb)
        setReadyStatus(rd.status)
        setReadyBody(rt.slice(0, 4000))
        const sipResponse = await fetch('/api/sip-modem')
        if (sipResponse.ok)
          setSip((await sipResponse.json()) as SipModem)
        const serialResponse = await fetch('/api/serial-modem')
        if (serialResponse.ok)
          setSerial((await serialResponse.json()) as SerialModem)
        if (!h.ok || !l.ok) throw new Error(`health ${h.status} / live ${l.status}`)
      } catch (e) {
        if (!cancelled) setErr(e instanceof Error ? e.message : 'error')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    void load()
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <div>
      <h1>Platform status</h1>
      <p className="page-intro">
        Health of this server, and whether a SIP phone can reach the management listener.
      </p>
      {loading && <LoadingBlock label="Fetching health and readiness…" />}
      {err && <ErrorBanner message={err} />}
      {!loading && !err && readyStatus != null && readyStatus >= 400 && (
        <p role="alert" style={{ color: '#a40', maxWidth: 720 }}>
          Readiness returned HTTP {readyStatus}. Database, payload storage, or worker heartbeat may be unhealthy — see
          detail below and operator runbooks.
        </p>
      )}
      {!loading && !err && (
        <div style={{ display: 'grid', gap: '1rem', maxWidth: 720 }}>
          <section>
            <h2>Lab dial checklist</h2>
            <p>
              When the emulator can place a management call, use these host values. Phase 5 stays open until that call
              completes an install batch — Compose health alone does not close the gate.
            </p>
            <ul>
              <li>
                Dial string <code>2130706433</code> (loopback IPv4) with <code>COINLINE_NCC_PORT=4000</code>
              </li>
              <li>
                Seeded terminal wire id <code>0102030405</code> (Payphone-01)
              </li>
              <li>Listener must be up on TCP 4000 (this stack starts it unless <code>COINLINE_NCC_LISTEN=0</code>)</li>
              <li>Assign a published install table set on the phone before expecting a Completed download</li>
              <li>
                Emulator boot status still records install prompt then out of service (M10 false) — do not mark phase 5
                passed from this page
              </li>
            </ul>
          </section>
          <section>
            <h2>SIP audio modem</h2>
            {sip?.listening ? (
              <div>
                <p>
                  Listening on UDP {sip.sipPort}. Audio is {sip.audio}, modem is {sip.modem}. Decoded bytes go to the
                  listener on port {sip.nccPort}. minimodem is {sip.minimodemInstalled ? 'installed' : 'not installed'}.
                  Active calls: {sip.activeCalls ?? 0}.
                </p>
                {sip.registrar ? (
                  <p>
                    Registrar {sip.registrar}
                    {sip.username ? ` as ${sip.username}` : ''}.
                    {sip.assignedNumber ? ` Number ${sip.assignedNumber}.` : ''}{' '}
                    {sip.registered ? 'Registered.' : 'Not registered yet.'}
                    {sip.registrationNote ? ` ${sip.registrationNote}` : ''}
                  </p>
                ) : (
                  <p>No SIP registrar is configured. Copy docker/sip.env.example to docker/sip.env to register.</p>
                )}
                <p className="row-actions">
                  <button
                    type="button"
                    disabled={selfTestBusy}
                    onClick={() => {
                      setSelfTestBusy(true)
                      setSelfTest(null)
                      void fetch('/api/sip-modem/self-test', { method: 'POST', headers: { 'X-Operator-Role': 'Admin' } })
                        .then(async (r) => {
                          const body = (await r.json().catch(() => null)) as { detail?: string; ok?: boolean } | null
                          setSelfTest(body?.detail ?? (r.ok ? 'Self-test finished.' : `Self-test failed (${r.status}).`))
                          await refreshSip()
                        })
                        .catch((e) => setSelfTest(e instanceof Error ? e.message : 'Self-test failed.'))
                        .finally(() => setSelfTestBusy(false))
                    }}
                  >
                    {selfTestBusy ? 'Running self-test…' : 'Run loopback self-test'}
                  </button>
                  {(sip.activeCalls ?? 0) > 0 ? (
                    <button
                      type="button"
                      disabled={hangupBusy}
                      onClick={() => {
                        setHangupBusy(true)
                        setHangupMsg(null)
                        void fetch('/api/sip-modem/hangup', { method: 'POST', headers: { 'X-Operator-Role': 'Admin' } })
                          .then(async (r) => {
                            const body = (await r.json().catch(() => null)) as { detail?: string } | null
                            setHangupMsg(body?.detail ?? (r.ok ? 'Calls cleared.' : `Hangup failed (${r.status}).`))
                            await refreshSip()
                          })
                          .catch((e) => setHangupMsg(e instanceof Error ? e.message : 'Hangup failed.'))
                          .finally(() => setHangupBusy(false))
                      }}
                    >
                      {hangupBusy ? 'Ending calls…' : 'End active calls'}
                    </button>
                  ) : null}
                </p>
                {selfTest && <p role="status">{selfTest}</p>}
                {hangupMsg && <p role="status">{hangupMsg}</p>}
              </div>
            ) : (
              <p>{sip?.note ?? 'The SIP audio modem is off.'}</p>
            )}
          </section>
          <section>
            <h2>Serial modem inlet</h2>
            {serial?.enabled || serial?.watching ? (
              <p>
                Phase {serial.phase ?? '—'}.
                {serial.portName ? ` Port ${serial.portName}` : ''}
                {serial.baudRate ? ` at ${serial.baudRate} baud` : ''}.
                Active bridges: {serial.activeBridges ?? 0}. Decoded bytes go to the listener on port{' '}
                {serial.nccPort ?? 4000}.
              </p>
            ) : (
              <p>{serial?.note ?? 'The serial modem inlet is off.'}</p>
            )}
          </section>
          <section>
            <h2>Liveness</h2>
            {health && live ? (
              <pre style={{ background: '#f6f8fa', padding: '0.75rem', overflow: 'auto' }}>
                {JSON.stringify({ health, live }, null, 2)}
              </pre>
            ) : (
              <EmptyHint>No liveness payload (unexpected).</EmptyHint>
            )}
          </section>
          <section>
            <h2>
              Readiness {readyStatus != null ? `(${readyStatus})` : ''}{' '}
              {readyStatus != null && readyStatus >= 400 ? (
                <span style={{ color: '#a40' }}>degraded</span>
              ) : null}
            </h2>
            {readyBody ? (
              <pre style={{ background: '#f6f8fa', padding: '0.75rem', overflow: 'auto', whiteSpace: 'pre-wrap' }}>
                {readyBody}
              </pre>
            ) : (
              <EmptyHint>No readiness body returned.</EmptyHint>
            )}
            {!loading &&
              !err &&
              readyStatus != null &&
              readyStatus < 400 &&
              readyBody &&
              (readyBody.includes('Healthy') || readyBody.includes('"status":"Healthy"')) && (
                <EmptyHint>Readiness checks reported healthy.</EmptyHint>
              )}
          </section>
        </div>
      )}
    </div>
  )
}
