import { optionalCdrCollection, summaryCollection, thresholdDescription } from './collectionPolicy'

type Props = { number: number; later: boolean; value: (key: string) => string | null | undefined }

export function CollectionPolicyPreview({ number, later, value }: Props) {
  const carrierMode = value('carrierStatistics')
  const carrier = carrierMode === '0' ? 'Disabled' : carrierMode === '1' ? 'Per carrier' : later && carrierMode === '17' ? 'Combined (MT71 reference 253)' : 'Review imported flags'
  return <section className="table-config-panel" aria-label="Collection policy preview">
    <h3>Collection policy preview</h3>
    <dl className="native-table-summary" aria-live="polite">
      <div><dt>Collection schedule</dt><dd>{summaryCollection(value)}</dd></div>
      <div><dt>Statistics call-in threshold</dt><dd>{thresholdDescription(value('threshold'))}</dd></div>
      {number === 34 && <>
        <div><dt>Carrier statistics</dt><dd>{carrier}</dd></div>
        <div><dt>Optional CDR window</dt><dd>{optionalCdrCollection(value)}</dd></div>
        {value('enabled') !== '0' && <div><dt>Optional CDR call-in threshold</dt><dd>{thresholdDescription(value('detailThreshold'))}</dd></div>}
      </>}
    </dl>
    <p>These fields configure what the phone collects and when its stored-record counts trigger a call-in. They do not change the host's reporting filters.</p>
  </section>
}
