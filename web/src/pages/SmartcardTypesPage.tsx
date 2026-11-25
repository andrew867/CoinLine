import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'
import { StatusBadge } from '../components/StatusBadge'
import './TableSetConfiguration.css'

type Row = {
  id: string
  code: string
  name: string
  atrProfile: number
  mapsToCardType: number
  notes: string
}

type Application = {
  key: string; name: string; family: string; subtype: string; transport: string
  enabledInInspectedBuilds: boolean; implementationEvidence: string; hostClassification: number | null; qualification: string
}
type Catalogue = {
  referenceScope: string; applications: Application[]
  physicalCardTransactionsQualified: boolean; referenceApplicationEvidenceOnly: boolean
}
const transports: Record<string, string> = {
  synchronous_memory: 'Synchronous memory', asynchronous_processor: 'Asynchronous processor',
}
const evidence: Record<string, string> = {
  active_handler: 'Application and handler included', application_disabled: 'Application disabled in reference builds',
  historical_partial: 'Historical or partial implementation', dispatch_disabled: 'Application dispatch disabled',
}
const classifications: Record<number, string> = {
  0: 'Unknown', 1: 'Magstripe', 2: 'Smart card', 3: 'E-purse', 4: 'Mondex', 5: 'Smart City', 6: 'Proton',
}

export function SmartcardTypesPage() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [catalogue, setCatalogue] = useState<Catalogue | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [catalogueError, setCatalogueError] = useState<string | null>(null)
  const [transport, setTransport] = useState('all')

  useEffect(() => {
    let active = true
    void apiGet<Row[]>('/api/smartcards/types').then(value => { if (active) setRows(value) })
      .catch(error => { if (active) setErr(error instanceof Error ? error.message : 'Could not load host classifications.') })
    void apiGet<Catalogue>('/api/smartcards/applications').then(value => { if (active) setCatalogue(value) })
      .catch(error => { if (active) setCatalogueError(error instanceof Error ? error.message : 'Could not load application evidence.') })
    return () => { active = false }
  }, [])

  const visible = catalogue?.applications.filter(application => transport === 'all' || application.transport === transport) ?? []
  return (
    <div>
      <h1>Smartcard types</h1>
      <p className="page-intro">Distinguish the card's contact transport, payment application and firmware support from the host's accounting classification.</p>
      <section className="table-config-panel" aria-labelledby="applications-heading">
        <h2 id="applications-heading">Reference application compatibility</h2>
        <p>A contact-reader test is not a payment transaction. This matrix describes inspected firmware configurations, not every terminal or a guarantee of card acceptance.</p>
        {catalogue === null && !catalogueError && <p role="status">Loading application catalogue…</p>}
        {catalogueError && <p role="alert">Could not load application catalogue: {catalogueError}</p>}
        {catalogue && <>
          <p><strong>Reference scope:</strong> {catalogue.referenceScope}.</p>
          <p><StatusBadge variant="warn" label="Application payments not qualified" /></p>
          <label>Contact transport
            <select value={transport} onChange={event => setTransport(event.target.value)} style={{ display: 'block' }}>
              <option value="all">All applications</option>
              <option value="synchronous_memory">Synchronous memory</option>
              <option value="asynchronous_processor">Asynchronous processor</option>
            </select>
          </label>
          <div role="region" aria-label="Smartcard application matrix" tabIndex={0} style={{ overflowX: 'auto', marginTop: 12 }}>
            <table cellPadding={8} style={{ borderCollapse: 'collapse', width: '100%' }}>
              <thead><tr><th scope="col">Application</th><th scope="col">Transport</th><th scope="col">Purse</th>
                <th scope="col">Reference builds</th><th scope="col">Evidence</th><th scope="col">Payment qualification</th></tr></thead>
              <tbody>{visible.map(application => <tr key={application.key} style={{ borderBottom: '1px solid #eee' }}>
                <td>{application.name}<div><small>{application.key}</small></div></td>
                <td>{transports[application.transport] ?? 'Unclassified'}</td>
                <td>{application.subtype === 'disposable' ? 'Disposable' : 'Reloadable'}</td>
                <td><StatusBadge label={application.enabledInInspectedBuilds ? 'Enabled' : 'Disabled'}
                  variant={application.enabledInInspectedBuilds ? 'info' : 'neutral'} /></td>
                <td>{evidence[application.implementationEvidence] ?? 'Evidence unavailable'}</td>
                <td><StatusBadge label="Not qualified" variant="warn" /></td>
              </tr>)}</tbody>
            </table>
          </div>
          <p>Security modules and generic rechargeable handlers are infrastructure, not customer card applications.
            A generic asynchronous reader or magnetic Visa/Mastercard support does not establish chip-payment acceptance.</p>
        </>}
      </section>
      <section className="table-config-panel" aria-labelledby="host-classifications-heading">
        <h2 id="host-classifications-heading">Registered host classifications</h2>
        <p>These catalogue rows describe stored host account classifications. They are not issuer credentials, card personalization data, or firmware application IDs.</p>
        {rows === null && !err && <p role="status">Loading host classifications…</p>}
        {err && <p role="alert">Could not load host classifications: {err}</p>}
        {rows?.length === 0 && <p role="status">No registered host classifications.</p>}
        {!!rows?.length && <div role="region" aria-label="Host classifications" tabIndex={0} style={{ overflowX: 'auto' }}>
          <table cellPadding={8} style={{ borderCollapse: 'collapse', width: '100%' }}>
            <thead><tr><th scope="col">Code</th><th scope="col">Name</th><th scope="col">ATR profile reference</th>
              <th scope="col">Host card type</th><th scope="col">Notes</th></tr></thead>
            <tbody>{rows.map(row => <tr key={row.id} style={{ borderBottom: '1px solid #eee' }}>
              <td>{row.code}</td><td>{row.name}</td><td>{row.atrProfile}</td>
              <td>{classifications[row.mapsToCardType] ?? 'Unknown'}</td><td>{row.notes}</td>
            </tr>)}</tbody>
          </table>
        </div>}
      </section>
    </div>
  )
}
