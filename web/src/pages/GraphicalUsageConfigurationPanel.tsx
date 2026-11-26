import { useState } from 'react'

export type GraphicalUsageScreen = { recordNumber: number; screenNumber: number; kindCode: number; screenOwnerCode: number; keyOwnerCodes: number[] }
export type GraphicalUsageConfiguration = { collectionDurationDays: number | null; screens: GraphicalUsageScreen[] }
type Presence = { number: number; availability: string }
type UsageRow = Omit<GraphicalUsageScreen, 'kindCode' | 'screenOwnerCode' | 'keyOwnerCodes'> & {
  kindCode: number | null; screenOwnerCode: number | null; keyOwnerCodes: (number | null)[]; staged: boolean
}

function editedNumber(edits: Readonly<Record<string, string>>, key: string, stored: number | null, max = 65535) {
  if (!Object.hasOwn(edits, key)) return stored
  const value = edits[key]
  return /^\d+$/.test(value) && Number.isSafeInteger(Number(value)) && Number(value) <= max ? Number(value) : null
}

export function previewGraphicalUsage(usage: GraphicalUsageConfiguration, number: number, edits: Readonly<Record<string, string>>) {
  const currentHeader = number === 131 && usage.collectionDurationDays !== null
  const duration = currentHeader ? editedNumber(edits, 'statDuration', usage.collectionDurationDays) : usage.collectionDurationDays
  const durationStaged = currentHeader && Object.hasOwn(edits, 'statDuration')
  const screens: UsageRow[] = usage.screens.map(screen => {
    if (screen.recordNumber !== number) return { ...screen, staged: false }
    const local = screen.screenNumber - 1 - (number - 131) * 8
    const kindKey = `screen-${local}.kind`, ownerKey = `screen-${local}.owner`
    const keyKeys = Array.from({ length: 6 }, (_, index) => `button-${local * 6 + index}.owner`)
    return { ...screen, kindCode: editedNumber(edits, kindKey, screen.kindCode, 255),
      screenOwnerCode: editedNumber(edits, ownerKey, screen.screenOwnerCode),
      keyOwnerCodes: keyKeys.map((key, index) => editedNumber(edits, key, screen.keyOwnerCodes[index] ?? null)),
      staged: [kindKey, ownerKey, ...keyKeys].some(key => Object.hasOwn(edits, key)) }
  })
  return { duration, durationStaged, screens }
}

const kindNames: Record<number, string> = { 0: 'Undefined', 66: 'Bitmap', 77: 'Mixed', 84: 'Text' }
function state(row: UsageRow, duration: number | null) {
  if (row.kindCode === null || row.screenOwnerCode === null || row.keyOwnerCodes.some(owner => owner === null)) return 'Invalid staged value'
  if (row.kindCode === 0) return 'Undefined screen'
  if (!Object.hasOwn(kindNames, row.kindCode)) return 'Unknown screen type'
  if (row.screenOwnerCode === 0) return 'No screen owner code'
  if (duration === null) return 'Collection period unavailable'
  return duration === 0 ? 'Collection disabled' : 'Configured for collection'
}

export function GraphicalUsageConfigurationPanel({ usage, records, number, edits, onOpenSection }: {
  usage?: GraphicalUsageConfiguration | null; records: Presence[]; number: number; edits: Readonly<Record<string, string>>
  onOpenSection: (key: string) => void
}) {
  const [filter, setFilter] = useState('defined')
  const model = usage && previewGraphicalUsage(usage, number, edits)
  const rows = model?.screens.filter(row => filter === 'all' || (filter === 'defined' && row.kindCode !== 0) ||
    (filter === 'configured' && state(row, model.duration) === 'Configured for collection')) ?? []
  const tracked = model?.screens.filter(row => state(row, model.duration) === 'Configured for collection').length ?? 0
  const available = records.filter(record => record.availability === 'included').length
  const unavailable = records.filter(record => record.availability !== 'included')
  return <details className="graphical-simulator">
    <summary>Review graphical statistics configuration</summary>
    <section className="table-config-panel" aria-label="Graphical statistics configuration">
      <h3>Graphical usage collection (MT153)</h3>
      <p>MT131 controls the collection period for all screens. A nonzero main-screen owner code is required for that screen to be reported. Mixed panels accumulate presses under the main screen and the six main key positions. This preview is configuration only, not a count of actual usage.</p>
      {!model ? <p role="status">The saved graphical statistics configuration is unavailable.</p> : <>
        <p><strong>MT131 collection period:</strong> {model.duration === null
          ? model.durationStaged ? 'Invalid staged value' : 'Unavailable in this set'
          : model.duration === 0 ? 'Disabled (zero days)' : `${model.duration} day${model.duration === 1 ? '' : 's'}`}
          {model.durationStaged && <small> · Staged</small>}</p>
        <p role="status">{model.duration === null ? 'Collection eligibility cannot be established without a valid MT131 reporting period.'
          : `${tracked} ${tracked === 1 ? 'screen configured' : 'screens configured'} for collection across ${available} included ${available === 1 ? 'record' : 'records'}.`}</p>
        {unavailable.length > 0 && <p>Not reviewed: {unavailable.map(record => `MT${record.number} (${record.availability})`).join(', ')}. A partial set may retain other installed records on the phone.</p>}
        <label>Screen coverage <select value={filter} onChange={event => setFilter(event.target.value)}>
          <option value="defined">Defined screens</option><option value="configured">Configured for collection</option><option value="all">All included screen slots</option>
        </select></label>
        {rows.length === 0 ? <p>No matching screen slots.</p> : <div className="graphical-usage-config-scroll">
          <table className="graphical-usage-config-table">
            <thead><tr><th scope="col">Screen</th><th scope="col">Screen owner</th>
              {Array.from({ length: 6 }, (_, index) => <th scope="col" key={index}>Key {index + 1} owner</th>)}
              <th scope="col">Collection setting</th></tr></thead>
            <tbody>{rows.map(row => <tr key={row.screenNumber}>
              <th scope="row">Screen {row.screenNumber}<small>MT{row.recordNumber} · {row.kindCode === null ? 'Invalid' : kindNames[row.kindCode] ?? `Unknown (${row.kindCode})`}</small>
                {row.staged && <small>Staged</small>}
                {row.recordNumber === number && <button type="button" className="quiet" onClick={() => onOpenSection(`screen-${row.screenNumber - 1 - (number - 131) * 8}`)}>Open Screen {row.screenNumber} settings</button>}</th>
              <td>{row.screenOwnerCode ?? 'Invalid'}</td>
              {row.keyOwnerCodes.map((owner, index) => <td key={index}>{owner ?? 'Invalid'}</td>)}
              <td>{state(row, model.duration)}</td>
            </tr>)}</tbody>
          </table>
        </div>}
        <small>Owner codes are stored values, not usage counts. Leaving a key owner code zero does not by itself exclude a reported key position.</small>
      </>}
    </section>
  </details>
}
