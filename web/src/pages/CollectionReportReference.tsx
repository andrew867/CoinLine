import { Link } from 'react-router-dom'
import { optionalCdrCollection, summaryCollection, thresholdDescription } from './collectionPolicy'

export type CallCarrierSettings = {
  startHour: number; startMinute: number; days: number; threshold: number
  timestamps: { hour: number; minute: number }[]; policyFlags: number
  detailThreshold: number; detailHour: number; detailMinute: number; detailDays: number; detailHours: number
}
export type CollectionReference = {
  status: 'acknowledged' | 'missing' | 'invalid' | 'ambiguous' | 'restricted'
  recordNumber: number; downloadBatchId: string | null; tableRevision: number | null
  ackRecordedAtUtc: string | null; laterUncertainAttempt: boolean
  collectionSettings: CallCarrierSettings | null; callRecordThreshold: number | null
}

const optionalModes: Record<number, string> = {
  0: 'Normal calls only', 1: 'All call attempts', 4: 'Completed calls only',
  8: 'Completed calls except local coin', 16: 'Completed calls except local coin and stored-value',
}

function describeSettings(settings: CallCarrierSettings) {
  const mode = settings.policyFlags & 0x1D
  const values: Record<string, string> = Object.fromEntries(Object.entries(settings)
    .filter(([, value]) => typeof value === 'number').map(([key, value]) => [key, String(value)]))
  values.enabled = String(mode)
  settings.timestamps.forEach((timestamp, index) => {
    values[`timestamp-${index}.hour`] = String(timestamp.hour); values[`timestamp-${index}.minute`] = String(timestamp.minute)
  })
  const read = (key: string) => values[key]
  const carrierEnabled = (settings.policyFlags & 2) !== 0
  const combinedBit = (settings.policyFlags & 0x20) !== 0
  return <dl className="collection-reference-fields">
    <div><dt>Call statistics schedule</dt><dd>{summaryCollection(read)}</dd></div>
    <div><dt>Statistics upload threshold</dt><dd>{thresholdDescription(String(settings.threshold))}</dd></div>
    <div><dt>Carrier collection</dt><dd>{carrierEnabled ? 'Enabled' : 'Disabled'}{combinedBit &&
      '; combined-summary bit set (meaning depends on terminal profile)'}</dd></div>
    <div><dt>Optional CDR mode</dt><dd>{optionalModes[mode] ?? 'Multiple or unrecognised stored mode bits'}</dd></div>
    <div><dt>Optional CDR window</dt><dd>{optionalCdrCollection(read)}</dd></div>
    <div><dt>Optional CDR upload threshold</dt><dd>{thresholdDescription(String(settings.detailThreshold))}</dd></div>
    <div><dt>Stored policy flags</dt><dd><code>0x{settings.policyFlags.toString(16).toUpperCase().padStart(2, '0')}</code></dd></div>
  </dl>
}

export function CollectionReportReference({ number, reference }: { number: 34 | 35; reference?: CollectionReference | null }) {
  const title = number === 34 ? 'MT34 call and carrier collection reference' : 'MT35 call-record threshold reference'
  const status = reference?.status
  const hasSource = status === 'acknowledged' || status === 'invalid'
  return <section className="performance-report-reference" aria-label={title}>
    <h3>{title}</h3>
    {reference && hasSource ? <>
      <p>Last retained terminal-acknowledged MT{number} recorded before the host received this upload.
        {reference.tableRevision != null && <> Revision {reference.tableRevision}.</>}
        {reference.ackRecordedAtUtc && <> Acknowledgement recorded <time dateTime={reference.ackRecordedAtUtc}>
          {new Date(reference.ackRecordedAtUtc).toLocaleString()}</time> (host clock).</>}
        {reference.downloadBatchId && <> <Link to={`/downloads/${reference.downloadBatchId}`}>View source download</Link>.</>}
      </p>
      <p>This is a receipt-time configuration reference, not proof of the settings in effect during the call or reporting period.
        The terminal clock or unobserved changes may differ. Received records and counters are unchanged.</p>
      {status === 'invalid' && <p>The acknowledged record does not have the supported MT{number} format. Settings are unavailable.</p>}
      {status === 'acknowledged' && number === 34 && reference.collectionSettings && describeSettings(reference.collectionSettings)}
      {status === 'acknowledged' && number === 35 && reference.callRecordThreshold != null && <p>
        <strong>Normal call-record upload threshold:</strong> {thresholdDescription(String(reference.callRecordThreshold))}.
        An active MT34 optional logging window can select a different threshold.</p>}
      {reference.laterUncertainAttempt && <p role="status">A later download attempt before receipt did not retain an acknowledgement.
        The last acknowledged record might no longer describe the phone.</p>}
    </> : <p>{status === 'ambiguous' ? 'Multiple retained acknowledgements have the same latest recorded time. No single reference can be selected.'
      : status === 'restricted' ? 'Viewing this reference requires read access to Downloads and Table records.'
        : `No retained acknowledged MT${number} before receipt is available.`}</p>}
  </section>
}
