import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'

type Usage = {
  version: { id: string; tableSetId: string; setName: string; number: number; configurationId: string | null; tableRevision: number }
  items: { terminalId: string; displayName: string; terminalIdHex: string; deactivated: boolean; assignedAtUtc: string;
    lastExactAcknowledgement: { downloadBatchId: string; recordedAtUtc: string } | null }[]
  total: number; page: number; pageSize: number; canReadDownloads: boolean
}

function DateTime({ value }: { value: string }) {
  return <time dateTime={value}>{new Date(value).toLocaleString()}</time>
}

export function ConfigurationUsagePanel({ versionId, onClose }: { versionId: string; onClose: () => void }) {
  const [page, setPage] = useState(1)
  const [refresh, setRefresh] = useState(0)
  const [data, setData] = useState<Usage | null>(null)
  const [error, setError] = useState('')
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => { heading.current?.focus() }, [])
  useEffect(() => {
    const controller = new AbortController()
    setData(null); setError('')
    void apiFetch(`/api/tables/configuration-library/${encodeURIComponent(versionId)}/assignments?page=${page}`, { signal: controller.signal })
      .then(async response => {
        if (!response.ok) {
          const body = await response.json().catch(() => null)
          throw new Error(body?.error ?? `Request failed (HTTP ${response.status}).`)
        }
        return response.json() as Promise<Usage>
      }).then(result => { if (!controller.signal.aborted) setData(result) })
      .catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Could not load assigned phones.') })
    return () => controller.abort()
  }, [versionId, page, refresh])

  return <section id="configuration-usage" className="table-config-panel" aria-labelledby="configuration-usage-title">
    <div className="configuration-library-summary">
      <h2 id="configuration-usage-title" ref={heading} tabIndex={-1}>Assigned phones and delivery evidence</h2>
      <div className="row-actions"><button type="button" onClick={() => setRefresh(value => value + 1)}>Refresh usage</button><button type="button" onClick={onClose}>Close usage</button></div>
    </div>
    {error && <p role="alert">Could not load configuration usage: {error}</p>}
    {!error && !data && <p role="status">Loading assigned phones…</p>}
    {data && <>
      <p><strong>MT{data.version.number} · {data.version.configurationId ?? 'Unnamed'} · revision {data.version.tableRevision}</strong><br />Set: {data.version.setName}</p>
      <p>These phones are currently assigned to the set containing this exact record version. Inactive phones remain listed. No other set with the same configuration ID is included.</p>
      {data.canReadDownloads ? <p>The last exact acknowledgement is historical application-level delivery evidence, not terminal memory readback. A byte-identical copy in another version, or a later changed record, is not represented by this receipt.</p>
        : <p>Download evidence is unavailable with your current read permissions.</p>}
      <p role="status">{data.total} assigned {data.total === 1 ? 'phone' : 'phones'}</p>
      {data.items.length ? <div className="configuration-library-table" tabIndex={0} role="region" aria-label="Phones assigned to this configuration">
        <table><thead><tr><th>Phone</th><th>Terminal ID</th><th>Assignment</th>{data.canReadDownloads && <th>Last exact version acknowledgement</th>}</tr></thead>
          <tbody>{data.items.map(phone => <tr key={phone.terminalId}>
            <td><Link to={`/terminals/${phone.terminalId}`}>{phone.displayName}</Link><small>{phone.deactivated ? 'Inactive' : 'Active'}</small></td>
            <td>{phone.terminalIdHex}</td><td><DateTime value={phone.assignedAtUtc} /></td>
            {data.canReadDownloads && <td>{phone.lastExactAcknowledgement ? <><DateTime value={phone.lastExactAcknowledgement.recordedAtUtc} /><br />
              <Link to={`/downloads/${phone.lastExactAcknowledgement.downloadBatchId}`}>View matched receipt</Link></> : 'No exact version acknowledgement recorded'}</td>}
          </tr>)}</tbody></table>
      </div> : <p>{data.total ? 'No assigned phones on this page.' : 'No phones are assigned to this configuration set.'}</p>}
      <div className="configuration-library-summary">
        <button type="button" disabled={data.page <= 1} onClick={() => setPage(value => value - 1)}>Previous usage page</button>
        <span>Usage page {data.page} of {Math.max(1, Math.ceil(data.total / data.pageSize))}</span>
        <button type="button" disabled={data.page * data.pageSize >= data.total} onClick={() => setPage(value => value + 1)}>Next usage page</button>
      </div>
    </>}
  </section>
}
