import { useEffect, useRef, useState } from 'react'
import { api } from '../api'
import { loadPdf } from '../pdf'
import type { FieldState, TableState, ValidationProblem } from '../types'
import { sampleData } from '../mapping'

interface Props { applicationId: string; templateId: string; fields: Record<string, FieldState>; tables: Record<string, TableState>; dirty: boolean; toast: (s: string) => void }

/** Sends the payload to the real API: POST /validate for a dry run, POST /render for the PDF. */
export default function TestPanel({ applicationId, templateId, fields, tables, dirty, toast }: Props) {
  const [data, setData] = useState('{}')
  const [flatten, setFlatten] = useState(false)
  const [strict, setStrict] = useState(true)
  const [problems, setProblems] = useState<ValidationProblem[] | null>(null)
  const [preview, setPreview] = useState<Uint8Array | null>(null)
  const sig = useRef<HTMLCanvasElement>(null); const dirtySig = useRef(false)
  const sigKeys = Object.values(fields).filter(f => f.kind === 'image' && f.key).map(f => f.key)
  const [sigKey, setSigKey] = useState('')

  useEffect(() => { // signature pad
    const c = sig.current!; const ctx = c.getContext('2d')!; let down = false
    ctx.lineWidth = 3; ctx.lineCap = 'round'; ctx.lineJoin = 'round'; ctx.strokeStyle = '#14287A'
    const pos = (e: PointerEvent) => { const r = c.getBoundingClientRect(); return [(e.clientX - r.left) * c.width / r.width, (e.clientY - r.top) * c.height / r.height] }
    const d = (e: PointerEvent) => { down = true; dirtySig.current = true; const [x, y] = pos(e); ctx.beginPath(); ctx.moveTo(x, y); c.setPointerCapture(e.pointerId) }
    const m = (e: PointerEvent) => { if (!down) return; const [x, y] = pos(e); ctx.lineTo(x, y); ctx.stroke() }
    const u = () => { down = false }
    c.addEventListener('pointerdown', d); c.addEventListener('pointermove', m); c.addEventListener('pointerup', u); c.addEventListener('pointercancel', u)
    return () => { c.removeEventListener('pointerdown', d); c.removeEventListener('pointermove', m); c.removeEventListener('pointerup', u); c.removeEventListener('pointercancel', u) }
  }, [])

  const parse = () => { try { return JSON.parse(data || '{}') } catch (e) { toast('Request data is not valid JSON: ' + (e as Error).message); return null } }
  const gen = () => { setData(JSON.stringify(sampleData(fields, tables), null, 2)); setProblems(null); toast('Sample data generated — edit it, then Validate or Render') }
  const insertSig = () => {
    if (!sigKey) return toast('Map a signature field first (type: image)')
    if (!dirtySig.current) return toast('Draw a signature first')
    const d = parse() ?? {}; d[sigKey] = sig.current!.toDataURL('image/png'); setData(JSON.stringify(d, null, 2)); toast(`Signature inserted into "${sigKey}"`)
  }
  const validate = async () => {
    const d = parse(); if (!d) return
    if (dirty) toast('Note: unsaved mapping changes are not used by the server — save first')
    try { const r = await api.validate(applicationId, templateId, d, strict); setProblems(r.problems); toast(r.valid ? 'Payload is valid' : `${r.problems.filter(p => p.severity === 'error').length} problem(s) found`) }
    catch (e) { toast((e as Error).message) }
  }
  const render = async () => {
    const d = parse(); if (!d) return
    if (dirty) toast('Note: unsaved mapping changes are not used by the server — save first')
    try { const bytes = await api.render(applicationId, templateId, d, strict, flatten); setProblems(null); setPreview(bytes) }
    catch (e) { const err = e as Error & { problems?: ValidationProblem[] }; if (err.problems) { setProblems(err.problems); toast(`Payload rejected by the API (${err.problems.length} problems)`) } else toast(err.message) }
  }

  return (
    <div className="insp">
      <p className="hint">Paste the JSON your app will send to <span className="mono">POST /api/v1/render</span> (application_id + template_id). Keys must match the saved mapping.</p>
      <div className="actions"><button className="btn" onClick={gen}>Generate sample data</button><button className="btn" onClick={validate}>Validate</button><button className="btn primary" onClick={render}>Render via API</button></div>
      <label htmlFor="data">Request data</label>
      <textarea id="data" value={data} spellCheck={false} onChange={e => setData(e.target.value)} />
      {problems && problems.length > 0 && <div className="problems"><b>{problems.some(p => p.severity === 'error') ? 'Request rejected' : 'Warnings'}</b>
        {problems.map((p, i) => <div key={i}>{p.severity === 'warning' ? '⚠ ' : ''}{p.key}: {p.code} — {p.message}</div>)}</div>}
      <label>Draw a signature, then insert it into a signature key</label>
      <canvas ref={sig} className="sigpad" width={600} height={180} />
      <div className="actions">
        <select value={sigKey} onChange={e => setSigKey(e.target.value)}><option value="">{sigKeys.length ? 'choose signature key' : 'no signature keys yet'}</option>{sigKeys.map(k => <option key={k}>{k}</option>)}</select>
        <button className="btn" onClick={insertSig}>Insert into key</button>
        <button className="btn" onClick={() => { sig.current!.getContext('2d')!.clearRect(0, 0, 600, 180); dirtySig.current = false }}>Clear</button>
      </div>
      <label className="chk"><input type="checkbox" checked={strict} onChange={e => setStrict(e.target.checked)} /> Strict — reject keys that are not in the mapping</label>
      <label className="chk"><input type="checkbox" checked={flatten} onChange={e => setFlatten(e.target.checked)} /> Read-only output (fields locked)</label>
      {preview && <Preview bytes={preview} onClose={() => setPreview(null)} />}
    </div>)
}

function Preview({ bytes, onClose }: { bytes: Uint8Array; onClose: () => void }) {
  const host = useRef<HTMLDivElement>(null)
  const [stat, setStat] = useState('')
  useEffect(() => {
    let cancelled = false
    ;(async () => {
      const d = await loadPdf(bytes.slice()); if (cancelled) return
      host.current!.innerHTML = ''; setStat(`${d.numPages} pages · ${Math.round(bytes.length / 1024)} KB`)
      for (let n = 1; n <= d.numPages; n++) { const p = await d.getPage(n); const vp = p.getViewport({ scale: 1.3 }); const c = document.createElement('canvas'); c.width = vp.width; c.height = vp.height; host.current!.appendChild(c); await p.render({ canvasContext: c.getContext('2d')!, viewport: vp }).promise }
    })()
    return () => { cancelled = true }
  }, [bytes])
  const download = () => { const url = URL.createObjectURL(new Blob([bytes as BlobPart], { type: 'application/pdf' })); const a = document.createElement('a'); a.href = url; a.download = 'rendered.pdf'; a.click(); URL.revokeObjectURL(url) }
  return (
    <div className="preview show" onClick={e => { if (e.target === e.currentTarget) onClose() }}>
      <div className="pv"><header><h1>Rendered by the API</h1><span className="stat">{stat}</span><div className="spacer" /><button className="btn" onClick={download}>Download</button><button className="btn" onClick={onClose}>Close</button></header>
        <div className="pages" ref={host} /></div>
    </div>)
}
