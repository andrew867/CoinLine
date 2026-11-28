export type RetainedLayoutCoverage = {
  unmappedByteCount: number
  unmappedRanges: { startOffset: number; endOffset: number }[]
  partiallyMappedBytes: { offset: number; unmappedMask: number }[]
}

export function RetainedLayoutReview({ coverage }: { coverage: RetainedLayoutCoverage }) {
  return <section className="table-config-panel" aria-label="Retained record layout">
    <h3>Partial editor, existing record required</h3>
    <p>Only the identified fields are editable. The complete record cannot be authored from a blank template.
      Saving fields retains all unidentified bytes and bits from the imported record.</p>
    <dl className="native-table-summary">
      <div><dt>Unmapped bytes</dt><dd>{coverage.unmappedByteCount}</dd></div>
      <div><dt>Partially mapped bytes</dt><dd>{coverage.partiallyMappedBytes.length}</dd></div>
    </dl>
    <details>
      <summary>Retained positions (no data values)</summary>
      <p>Offsets start at zero for the message type. Masks identify bits that are not assigned by the editor.</p>
      <p>Unmapped byte ranges: {coverage.unmappedRanges.map(range => range.startOffset === range.endOffset
        ? String(range.startOffset) : `${range.startOffset} to ${range.endOffset}`).join(', ') || 'None'}.</p>
      {coverage.partiallyMappedBytes.length > 0 && <ul>{coverage.partiallyMappedBytes.map(byte =>
        <li key={byte.offset}>Offset {byte.offset}: retained mask 0x{byte.unmappedMask.toString(16).padStart(2, '0').toUpperCase()}</li>)}</ul>}
    </details>
    <p>This describes editor coverage, not a complete verified terminal layout. Review the target separately before publication.</p>
  </section>
}
