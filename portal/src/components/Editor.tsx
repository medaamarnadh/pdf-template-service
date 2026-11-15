import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api'
import { loadPdf, widgetRects, type PDFDocumentProxy } from '../pdf'
import { applyMapping, detectTables, fieldFromDiscovered, newDrawnField, snake, toMapping } from '../mapping'
import type { FieldState, TableState, TemplateDetail } from '../types'
import PdfCanvas from './PdfCanvas'
import FieldList from './FieldList'
import Inspector from './Inspector'
import TestPanel from './TestPanel'

interface Props { templateId: string; onBack: () => void; toast: (s: string) => void }

export default function Editor({ templateId, onBack, toast }: Props) {
  const [tpl, setTpl] = useState<TemplateDetail | null>(null)
  const [doc, setDoc] = useState<PDFDocumentProxy | null>(null)
  const [fields, setFields] = useState<Record<string, FieldState>>({})
  const [tables, setTables] = useState<Record<string, TableState>>({})
  const [selected, setSelected] = useState<string | null>(null)
  const [drawMode, setDrawMode] = useState(false)
  const [tab, setTab] = useState<'field' | 'test' | 'json'>('field')
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [seq, setSeq] = useState(1)

  // Load template metadata (fields, mapping) from the API and the PDF bytes for pdf.js
  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const [t, bytes] = await Promise.all([api.getTemplate(templateId), api.templateFile(templateId)])
        const d = await loadPdf(new Uint8Array(bytes)); if (cancelled) return
        const rects = await widgetRects(d)
        const fs: Record<string, FieldState> = {}
        for (const df of t.fields) fs[df.id] = fieldFromDiscovered(df, rects[df.id] ?? [])
        const ts = applyMapping(t.mapping?.fields ?? {}, fs)
        setTpl(t); setDoc(d); setFields(fs); setTables(ts); setDirty(false)
        setSeq(Object.keys(fs).filter(k => k.startsWith('Box_')).length + 1)
        toast(`${t.fields.length} form fields · ${Object.keys(t.mapping?.fields ?? {}).length} keys mapped`)
      } catch (e) { toast((e as Error).message) }
    })()
    return () => { cancelled = true }
  }, [templateId])

  const mapping = useMemo(() => toMapping(fields, tables), [fields, tables])
  const mutate = (fn: (fs: Record<string, FieldState>) => void) => { setFields(prev => { const next = { ...prev }; for (const k in next) next[k] = { ...next[k] }; fn(next); return next }); setDirty(true) }
  const patchField = (id: string, patch: Partial<FieldState>) => mutate(fs => { Object.assign(fs[id], patch); if (patch.kind) fs[id].kind = patch.kind })
  const patchTable = (name: string, patch: Partial<TableState>) => { setTables(prev => ({ ...prev, [name]: { ...prev[name], ...patch } })); setDirty(true) }

  const runDetect = () => {
    let count = 0
    setFields(prev => { const next: Record<string, FieldState> = {}; for (const k in prev) next[k] = { ...prev[k] }; const ts = detectTables(next); count = Object.keys(ts).length; setTables(t => ({ ...t, ...ts })); return next })
    setDirty(true); setTimeout(() => toast(count ? `${count} table(s) detected — each maps to one array key` : 'No repeating rows found (expects ids like Budget1_Category)'), 0)
  }
  const ungroup = (name: string) => { mutate(fs => { for (const id of tables[name]?.fieldIds ?? []) if (fs[id]) fs[id].table = null }); setTables(prev => { const n = { ...prev }; delete n[name]; return n }) }
  const suggest = () => { let n = 0; mutate(fs => { for (const f of Object.values(fs)) if (!f.key && !f.table) { f.key = snake(f.id); n++ } }); setTimeout(() => toast(n ? `Suggested keys for ${n} fields — review before saving` : 'Every field already has a key'), 0) }
  const onDraw = (page: number, rect: number[]) => { const id = `Box_${seq}`; setSeq(seq + 1); mutate(fs => { fs[id] = newDrawnField(id, page, rect) }); setSelected(id); setTab('field') }
  const nextUnmapped = () => { const nx = Object.values(fields).find(f => !f.key && !f.table); nx ? select(nx.id) : toast('All fields are mapped') }
  const select = useCallback((id: string) => { setSelected(id); setTab('field'); setTimeout(() => document.querySelector('.tag.sel')?.scrollIntoView({ block: 'center', behavior: 'smooth' }), 0) }, [])

  const save = async () => {
    setSaving(true)
    try { const r = await api.saveMapping(templateId, mapping); setDirty(false); toast(`Mapping saved — ${r.mappedKeys} keys`) }
    catch (e) { toast('Save failed: ' + (e as Error).message) } finally { setSaving(false) }
  }

  const all = Object.values(fields); const mapped = all.filter(f => f.key || f.table).length
  const req = all.filter(f => f.required && !f.table).length + Object.values(tables).filter(t => t.required).length
  const tn = Object.keys(tables).length

  return (
    <div className="app">
      <header>
        <button className="link" onClick={onBack}>← Templates</button>
        <h1>{tpl?.name ?? '…'} <span>· {tpl?.pageCount ?? '–'} pages</span></h1>
        <span className="stat">{all.length ? `${mapped} of ${all.length} mapped${tn ? ` · ${tn} table${tn > 1 ? 's' : ''}` : ''}${req ? ` · ${req} required` : ''}` : ''}</span>
        <div className="spacer" />
        <div className="legend"><span><i style={{ background: 'var(--open)' }} />Unmapped</span><span><i style={{ background: 'var(--mapped)' }} />Mapped</span><span><i style={{ background: 'var(--drawn)' }} />Drawn box</span><span><i style={{ background: 'var(--tbl)' }} />Table row</span></div>
        <button className={'btn' + (drawMode ? ' on' : '')} onClick={() => { setDrawMode(!drawMode); if (!drawMode) toast('Drag on a page to draw a box; set its type in the Field panel') }}>Draw field</button>
        <button className="btn" onClick={runDetect}>Detect tables</button>
        <button className="btn" onClick={suggest}>Suggest keys</button>
        <button className="btn primary" onClick={save} disabled={saving || !dirty}>{saving ? 'Saving…' : dirty ? 'Save mapping' : 'Saved'}</button>
      </header>
      <main>
        <FieldList fields={fields} tables={tables} selected={selected} onSelect={select} />
        <PdfCanvas doc={doc} fields={fields} tables={tables} selected={selected} drawMode={drawMode} onSelect={select} onDraw={onDraw} />
        <aside className="panel right">
          <div className="tabs">{(['field', 'test', 'json'] as const).map(t => <button key={t} className={tab === t ? 'on' : ''} onClick={() => setTab(t)}>{{ field: 'Field', test: 'Test render', json: 'Mapping JSON' }[t]}</button>)}</div>
          {tab === 'field' && <Inspector field={selected ? fields[selected] ?? null : null} tables={tables}
            onChange={p => selected && patchField(selected, p)} onTableChange={patchTable} onUngroup={ungroup}
            onDelete={id => { mutate(fs => { delete fs[id] }); setSelected(null) }} onNextUnmapped={nextUnmapped} />}
          {tab === 'test' && tpl && <TestPanel applicationId={tpl.applicationId} templateId={templateId} fields={fields} tables={tables} dirty={dirty} toast={toast} />}
          {tab === 'json' && <div className="insp">
            <p className="hint">Stored with the template via <span className="mono">PUT /api/v1/templates/{'{id}'}/mapping</span>; the engine validates every render request against it.</p>
            <textarea readOnly value={JSON.stringify({ fields: mapping }, null, 2)} style={{ minHeight: 480 }} spellCheck={false} /></div>}
        </aside>
      </main>
    </div>)
}
