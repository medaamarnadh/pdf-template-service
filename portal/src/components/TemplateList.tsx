import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api'
import type { TemplateSummary } from '../types'

interface Props { applicationId: string; onBack: () => void; onOpen: (id: string) => void; toast: (s: string) => void }

export default function TemplateList({ applicationId, onBack, onOpen, toast }: Props) {
  const [items, setItems] = useState<TemplateSummary[] | null>(null)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState<{ name: string; message: string; detail?: string } | null>(null)
  const [over, setOver] = useState(false)
  const file = useRef<HTMLInputElement>(null)

  const load = useCallback(() => api.listTemplates(applicationId).then(setItems).catch(e => setError(e.message)), [applicationId])
  useEffect(() => { load() }, [load])

  const upload = async (f: File | undefined) => {
    if (!f) return
    setFailed(null)
    if (f.type !== 'application/pdf' && !f.name.toLowerCase().endsWith('.pdf')) return toast('Please choose a PDF file')
    try { const t = await api.uploadTemplate(applicationId, f); toast(`Uploaded — ${t.fields.length} form fields detected`); onOpen(t.id) }
    catch (e) {
      // The toast is one line and fades; the API's detail (the underlying exception) goes in the panel below.
      const err = e as Error & { detail?: string }
      toast('Upload failed: ' + err.message)
      setFailed({ name: f.name, message: err.message, detail: err.detail })
    }
  }
  const remove = async (t: TemplateSummary) => { if (!confirm(`Delete "${t.name}"?`)) return; await api.deleteTemplate(t.id); load() }

  return (
    <div className="home">
      <button className="link" onClick={onBack}>← Applications</button>
      <h1>Templates</h1>
      <p className="lead">Upload a fillable PDF, map its fields to data keys, then render documents with <span className="mono">POST /api/v1/render</span> (application_id + template_id).</p>
      <div className={'card drop' + (over ? ' on' : '')} onClick={() => file.current?.click()}
           onDragOver={e => { e.preventDefault(); setOver(true) }} onDragLeave={() => setOver(false)}
           onDrop={e => { e.preventDefault(); setOver(false); upload(e.dataTransfer.files[0]) }}>
        Drop a PDF here or click to choose one
        <input ref={file} type="file" accept="application/pdf" onChange={e => upload(e.target.files?.[0])} />
      </div>
      {failed && <div className="problems"><b>Could not upload {failed.name}</b>
        <div>{failed.message}</div>
        {failed.detail && <div className="detail">{failed.detail}</div>}</div>}
      <div className="card">
        {error && <div className="err">{error} — is the API running on port 5080?</div>}
        {items === null && !error && <div className="empty">Loading…</div>}
        {items?.length === 0 && <div className="empty">No templates yet.</div>}
        {items?.map(t => (
          <div key={t.id} className="tpl">
            <div><div className="n">{t.name}</div><div className="s">{t.fileName} · {t.pageCount} pages · {t.mappedKeys} keys mapped · <span className="mono">{t.id}</span>
              {' '}<button className="link" onClick={e => { e.stopPropagation(); navigator.clipboard?.writeText(t.id); toast('Template id copied') }}>copy id</button></div></div>
            <button className="btn primary" onClick={() => onOpen(t.id)}>Open</button>
            <button className="btn" onClick={() => remove(t)}>Delete</button>
          </div>))}
      </div>
    </div>)
}
