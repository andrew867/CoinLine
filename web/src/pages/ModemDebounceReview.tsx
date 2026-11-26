const detectorNames = ['Dial tone', 'Busy and ringback', 'Answer tone', 'Carrier/mark', 'Lost carrier']

function count(value: string | null | undefined) {
  if (value == null || !/^\d+$/.test(value)) return null
  const result = Number(value)
  return Number.isInteger(result) && result >= 0 && result <= 255 ? result : null
}

export function modemDebounceMilliseconds(interval: string | null | undefined, samples: string | null | undefined) {
  const intervalCount = count(interval), sampleCount = count(samples)
  return intervalCount && sampleCount ? intervalCount * sampleCount * 10 : null
}

function duration(interval: string | null | undefined, samples: string | null | undefined) {
  const result = modemDebounceMilliseconds(interval, samples)
  return result == null ? 'Not established' : `${result.toLocaleString()} ms`
}

export function ModemDebounceReview({ later, value, onOpen }: {
  later: boolean; value: (key: string) => string | null | undefined; onOpen: (section: string) => void
}) {
  const answer = value('answerToneDetection'), optional = value('answerToneOptional'), removal = value('skipAnswerToneRemoval')
  return <section className="table-config-panel" aria-label="Modem detector timing review">
    <h3>Modem detector timing review</h3>
    <p>Inspect assertion and release counts together, including unsaved edits. These settings are independent of the line's actual training sequence.</p>
    {later && <p>Nominal durations assume the 10 ms interval unit observed on a later terminal. This is a configuration estimate, not measured carrier behavior or a recommended default.</p>}
    {!later && <p>Counts are shown without a duration estimate because the sampling unit is not established here for this profile.</p>}
    <p>Answer tone: {answer === 'true' ? optional === 'true' ? 'Detection enabled, answer optional' : optional === 'false' ? 'Detection enabled, answer required' : 'Detection enabled, requirement unknown'
      : answer === 'false' ? 'Detection disabled' : 'Not established'}. Wait for its removal: {removal === 'true' ? 'Skipped' : removal === 'false' ? 'Enabled' : 'Not established'}.</p>
    <div className="table-set-versions" role="region" aria-label="Modem detector counts" tabIndex={0}>
      <table>
        <thead><tr><th>Detector</th><th>Interval</th><th>Off count</th><th>On count</th>{later && <><th>Nominal release</th><th>Nominal assertion</th></>}<th>Fields</th></tr></thead>
        <tbody>{detectorNames.map((name, index) => {
          const key = `debounce-${index}`, interval = value(`${key}.interval`), off = value(`${key}.off`), on = value(`${key}.on`)
          return <tr key={key}>
            <th scope="row">{name}</th><td>{interval ?? 'Unknown'}</td><td>{off ?? 'Unknown'}</td><td>{on ?? 'Unknown'}</td>
            {later && <><td>{duration(interval, off)}</td><td>{duration(interval, on)}</td></>}
            <td><button type="button" className="quiet" onClick={() => onOpen(key)} aria-label={`Review ${name} fields`}>Review</button></td>
          </tr>
        })}</tbody>
      </table>
    </div>
  </section>
}
