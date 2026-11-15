import type { FieldState, TableState } from '../types'

interface Props {
  field: FieldState | null; tables: Record<string, TableState>
  onChange: (patch: Partial<FieldState>) => void
  onTableChange: (name: string, patch: Partial<TableState>) => void
  onUngroup: (name: string) => void; onDelete: (id: string) => void; onNextUnmapped: () => void
}

export default function Inspector({ field: f, tables, onChange, onTableChange, onUngroup, onDelete, onNextUnmapped }: Props) {
  if (!f) return <div className="insp"><div className="empty">Select a field on the page or in the list.</div></div>

  if (f.table && tables[f.table]) {
    const t = tables[f.table]
    return (
      <div className="insp">
        <label>PDF field id</label><div className="mono">{f.id}</div>
        <p className="hint" style={{ marginTop: 8 }}>Row {/\d+/.exec(f.id)?.[0]} of a detected table. The table maps to one array key; each row object uses the column keys below.</p>
        <label htmlFor="tKey">Table data key</label>
        <input id="tKey" type="text" className="mono" value={t.key} spellCheck={false} onChange={e => onTableChange(t.name, { key: e.target.value.trim() })} />
        <label>Rows in template</label><div className="mono">{t.rows.length} ({t.rows[0]}–{t.rows[t.rows.length - 1]})</div>
        <label>Columns → field pattern</label>
        <div className="mono" style={{ lineHeight: 1.7 }}>{Object.entries(t.columns).map(([k, p]) => <div key={k}>{k} → {p}</div>)}</div>
        <label className="chk"><input type="checkbox" checked={t.required} onChange={e => onTableChange(t.name, { required: e.target.checked })} /> Required — reject the request if the array is missing or empty</label>
        <p className="hint" style={{ marginTop: 8 }}>Requests with more rows than the template can hold are rejected.</p>
        <div className="actions"><button className="btn" onClick={() => onUngroup(t.name)}>Ungroup table</button></div>
      </div>)
  }

  const kinds = f.drawn ? ['text', 'checkbox', 'image', 'table'] : [f.kind]
  const cap = Math.max(0, Math.floor((f.rects[0].rect[3] - f.rects[0].rect[1]) / f.rowHeight) - (f.header ? 1 : 0))
  return (
    <div className="insp">
      <label>PDF field id</label><div className="mono">{f.id}</div>
      {f.tooltip && <><label>Tooltip (508 label)</label><div>{f.tooltip}</div></>}
      <label htmlFor="fKey">Data key</label>
      <input id="fKey" type="text" className="mono" value={f.key} placeholder="e.g. applicant_name" spellCheck={false} autoFocus onChange={e => onChange({ key: e.target.value.trim() })} />
      <label htmlFor="fKind">Type</label>
      <select id="fKind" value={f.kind} disabled={!f.drawn} onChange={e => onChange({ kind: e.target.value as FieldState['kind'] })}>{kinds.map(k => <option key={k}>{k}</option>)}</select>
      {f.options.length > 0 && <><label>Accepted values</label><div className="mono">{f.options.join(' · ')}</div></>}
      {f.kind === 'text' && <>
        <label htmlFor="fMax">Max characters</label>
        <input id="fMax" type="text" className="mono" inputMode="numeric" value={f.maxLength} onChange={e => onChange({ maxLength: Math.max(1, +e.target.value || 1) })} />
        <p className="hint" style={{ margin: '4px 0 0' }}>Requests with longer values are rejected. {f.multiline ? 'Multiline field.' : 'Single-line field.'}</p>
      </>}
      {f.kind === 'image' && <>
        <label htmlFor="fImg">Allowed image types</label>
        <select id="fImg" value={f.imgTypes} onChange={e => onChange({ imgTypes: e.target.value })}><option value="png,jpeg">PNG or JPEG</option><option value="png">PNG only</option><option value="jpeg">JPEG only</option></select>
        <label htmlFor="fKb">Max size (KB)</label>
        <input id="fKb" type="text" className="mono" inputMode="numeric" value={f.imgMaxKb} onChange={e => onChange({ imgMaxKb: Math.max(1, +e.target.value || 1) })} />
        <p className="hint" style={{ margin: '4px 0 0' }}>Images are decoded on the server before rendering; anything that fails to decode is rejected.</p>
      </>}
      {f.kind === 'table' && <>
        <label htmlFor="fCols">Columns (comma-separated keys)</label>
        <input id="fCols" type="text" className="mono" value={f.columns} onChange={e => onChange({ columns: e.target.value })} />
        <label htmlFor="fRh">Row height (pt)</label>
        <input id="fRh" type="text" className="mono" inputMode="numeric" value={f.rowHeight} onChange={e => onChange({ rowHeight: Math.max(8, +e.target.value || 18) })} />
        <label className="chk"><input type="checkbox" checked={f.header} onChange={e => onChange({ header: e.target.checked })} /> Draw a header row with column names</label>
        <p className="hint" style={{ margin: '4px 0 0' }}>Data key receives an array of objects; {cap} rows fit in this box.</p>
      </>}
      {f.kind !== 'table' && <>
        <label htmlFor="fFmt">Format</label>
        <select id="fFmt" value={f.format} onChange={e => onChange({ format: e.target.value })}>
          <option value="">none</option><option value="date:MM/dd/yyyy">date → MM/dd/yyyy</option><option value="number:#,##0">number → 1,234</option><option value="currency:USD">currency → $1,234.00</option><option value="upper">UPPERCASE</option>
        </select>
      </>}
      <label className="chk"><input type="checkbox" checked={f.required} onChange={e => onChange({ required: e.target.checked })} /> Required — reject the request if missing</label>
      <div className="actions">
        {f.drawn && <button className="btn" onClick={() => onDelete(f.id)}>Delete box</button>}
        <button className="btn" onClick={() => onChange({ key: '' })}>Clear key</button>
        <button className="btn" onClick={onNextUnmapped}>Next unmapped</button>
      </div>
      <p className="hint" style={{ marginTop: 14 }}>{f.rects.length > 1 ? `This id appears in ${f.rects.length} places; one value fills all of them.` : `Page ${f.rects[0].page}.`}</p>
    </div>)
}
