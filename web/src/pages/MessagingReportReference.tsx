import { Link } from 'react-router-dom'

type MessagingSettings = {
  serviceMode: number
  callRecordMode: number
  collectStatistics: boolean
  areas: { area: string; allowed: boolean; surchargeCents: number }[]
  allowedPayments: string[]
}

export type MessagingReference = {
  status: 'acknowledged' | 'missing' | 'invalid' | 'ambiguous' | 'restricted'
  downloadBatchId: string | null
  tableRevision: number | null
  ackRecordedAtUtc: string | null
  laterUncertainAttempt: boolean
  settings: MessagingSettings | null
}

const modeLabel = (mode: number) => mode === 0 ? 'Disabled' : mode === 1 ? 'Enabled' : `Unrecognized stored code ${mode}`

export function MessagingReportReference({ reference }: { reference?: MessagingReference | null }) {
  const status = reference?.status
  const hasSource = status === 'acknowledged' || status === 'invalid'
  return <section className="performance-report-reference" aria-label="MT68 Audio-Gram configuration reference">
    <h3>MT68 Audio-Gram configuration reference</h3>
    {reference && hasSource ? <>
      <p>Last retained terminal-acknowledged MT68 recorded before the host received this report.
        {reference.tableRevision != null && <> Revision {reference.tableRevision}.</>}
        {reference.ackRecordedAtUtc && <> Acknowledgement recorded <time dateTime={reference.ackRecordedAtUtc}>
          {new Date(reference.ackRecordedAtUtc).toLocaleString()}</time> (host clock).</>}
        {reference.downloadBatchId && <> <Link to={`/downloads/${reference.downloadBatchId}`}>View source download</Link>.</>}
      </p>
      {reference.laterUncertainAttempt && <p role="status">A later attempt has no confirmed application acknowledgement. Its effect on the terminal is unknown.</p>}
      {status === 'invalid' ? <p>The acknowledged payload does not match the supported 72-byte MT68 layout.</p>
        : reference.settings ? <>
          <dl className="collection-reference-fields">
            <div><dt>Messaging service</dt><dd>{modeLabel(reference.settings.serviceMode)}</dd></div>
            <div><dt>Service call records</dt><dd>{modeLabel(reference.settings.callRecordMode)}</dd></div>
            <div><dt>MT69 statistics collection</dt><dd>{reference.settings.collectStatistics ? 'Enabled' : 'Disabled'}</dd></div>
            <div><dt>Configured payment permissions</dt><dd>{reference.settings.allowedPayments.join(', ') || 'None selected'}</dd></div>
          </dl>
          <div className="graphical-usage-scroll">
            <table className="graphical-usage-table">
              <caption>Configured Audio-Gram call areas and surcharges</caption>
              <thead><tr><th scope="col">Call area</th><th scope="col">Permission</th><th scope="col">Configured surcharge</th></tr></thead>
              <tbody>{reference.settings.areas.map(area => <tr key={area.area}>
                <th scope="row">{area.area}</th><td>{area.allowed ? 'Allowed' : 'Not selected'}</td>
                <td>{area.surchargeCents.toLocaleString()} cents</td>
              </tr>)}</tbody>
            </table>
          </div>
        </> : <p>The retained configuration settings are unavailable.</p>}
      <p>This is a receipt-time reference, not the current assignment or proof of settings throughout the phone-clock period.
        Reported attempts and counts are unchanged; configured surcharges do not establish collected or settled amounts.</p>
    </> : status === 'restricted' ? <p>Downloads and Table records read permissions are required to view this reference.</p>
      : status === 'ambiguous' ? <p>Multiple retained acknowledgements have the same latest recorded time. No single configuration can be selected.</p>
        : status === 'missing' ? <p>No retained MT68 application acknowledgement was found for this phone before receipt.</p>
          : <p>No MT68 configuration reference was returned for this report.</p>}
  </section>
}
