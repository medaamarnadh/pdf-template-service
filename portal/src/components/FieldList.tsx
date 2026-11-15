import { useState } from 'react'
import type { FieldState, TableState } from '../types'

interface Props { fields: Record<string, FieldState>; tables: Record<string, TableState>; selected: string | null; onSelect: (id: string) => void }

export default function FieldList({ fields, tables, selected, onSelect }: Props) {
  const [q, setQ] = useState('')
  const ql = q.trim().toLowerCase()
  const items = Object.values(fields)
    .filter(f => !ql || (ql === 'required' ? (f.required || (f.table && tables[f.table]?.required)) : f.id.toLowerCase().includes(ql) || f.key.toLowerCase().includes(ql)))
    .sort((a, b) => a.rects[0].page - b.rects[0].page || a.id.localeCompare(b.id))
  return (
    <aside className="panel">
      <h2>Fields <span>({items.length})</span></h2>
      <input className="search" value={q} onChange={e => setQ(e.target.value)} placeholder="Filter by field or key (type: required)" />
      <div className="list">
        {!Object.keys(fields).length && <div className="empty">No form fields found in this PDF. Use Draw field to add boxes.</div>}
        {items.map(f => {
          const t = f.table ? tables[f.table] : null
          const keyText = t ? `table ${t.key}[]${t.required ? ' · required' : ''}` : f.key ? `${f.key}${f.required ? ' · required' : ''}` : 'no key yet'
          return (
            <div key={f.id} className={'row' + (selected === f.id ? ' sel' : '')} onClick={() => onSelect(f.id)}>
              <span className={'dot' + ((f.key || f.table) ? ' m' : '') + (f.drawn ? ' d' : '') + (f.table ? ' t' : '')} />
              <div><div className="name">{f.id}</div><div className="key">{keyText}</div></div>
              <span className="pg">p{f.rects[0].page}</span>
            </div>)
        })}
      </div>
    </aside>
  )
}
