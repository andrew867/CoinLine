import { languageOrderPlan, swapActiveLanguages } from './languageOrderPlan'

export function LanguageOrderPanel({ value, editable, onStage }: {
  value: (key: string) => string | null | undefined
  editable: boolean
  onStage: (changes: Record<string, string>) => void
}) {
  const plan = languageOrderPlan(value)
  const move = (position: number, direction: -1 | 1) => {
    const changes = swapActiveLanguages(value, position, direction)
    if (editable && changes) onStage(changes)
  }
  return <section className="table-config-panel language-order-panel" aria-label="Language scrolling order">
    <h3>Language scrolling order</h3>
    <p>The leading configured positions are active. The first is the default; the others follow in scrolling order.
      Inactive positions retain their stored values. Moving two active languages stages only those two order fields.</p>
    {!plan.validCount && <p role="status">The stored language count is invalid or unavailable. Choose one to four before interpreting the active order.</p>}
    {!!plan.invalid.length && <p role="status">Active positions {plan.invalid.join(', ')} contain unused, unrecognised or unavailable language codes.
      An invalid downloaded order can be ignored or replaced by a fallback during installation.</p>}
    {plan.duplicate && <p role="status">The active order repeats a language. Review the distinct choices; repetition is not a verified firmware rejection.</p>}
    {plan.exceedsInstalledPair && <p role="status">The inspected two-language runtime uses at most two installed user languages.
      Three or four may be present during installation but should not be assumed to work in service.</p>}
    <div className="language-order-scroll"><table>
      <caption>Configured user languages and conditional prompt records</caption>
      <thead><tr><th scope="col">Position</th><th scope="col">Language</th><th scope="col">State</th>
        <th scope="col">Installed two-language prompt reference</th><th scope="col">Order</th></tr></thead>
      <tbody>{plan.rows.map(row => <tr key={row.key}>
        <th scope="row">Slot {row.position}</th><td>{row.name}</td>
        <td>{!plan.validCount ? 'Undetermined' : row.active ? row.position === 1 ? 'Default' : 'Active' : 'Inactive, retained'}</td>
        <td>{row.promptRecord ? `MT${row.promptRecord}` : !plan.validCount || row.active ? 'Unconfirmed' : 'Not active'}</td>
        <td>{row.active && editable ? <div className="row-actions">
          <button className="quiet" type="button" aria-label={`Move slot ${row.position} earlier`}
            disabled={!swapActiveLanguages(value, row.position, -1)} onClick={() => move(row.position, -1)}>Earlier</button>
          <button className="quiet" type="button" aria-label={`Move slot ${row.position} later`}
            disabled={!swapActiveLanguages(value, row.position, 1)} onClick={() => move(row.position, 1)}>Later</button>
        </div> : null}</td>
      </tr>)}</tbody>
    </table></div>
    <p>For the inspected installed two-language layout, a single language uses MT85. With two distinct languages,
      the lower language code uses MT85 and the higher uses MT86, independently of the default and scrolling order.
      This is a configuration reference, not a device readback. The mapping assumes ordinary count values; the other imported bits remain untouched. Another firmware build
      or missing translated prompts can change the observed behavior. Moving a language does not rewrite either prompt record.</p>
  </section>
}
