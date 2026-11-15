import { useEffect, useState } from 'react'
import { api } from '../api'
import type { Application } from '../types'

interface Props { onOpen: (applicationId: string) => void; toast: (s: string) => void }

export default function Applications({ onOpen, toast }: Props) {
  const [items, setItems] = useState<Application[] | null>(null)
  const [error, setError] = useState('')
  const [name, setName] = useState('')

  const load = () => api.listApplications().then(setItems).catch(e => setError(e.message))
  useEffect(() => { load() }, [])

  const create = async () => {
    if (!name.trim()) return toast('Give the application a name')
    try { const a = await api.createApplication(name.trim()); setName(''); toast(`Created ${a.name}`); load() }
    catch (e) { toast('Could not create: ' + (e as Error).message) }
  }

  const rename = async (a: Application) => {
    const next = prompt('New name', a.name)
    if (!next || next === a.name) return
    try { await api.renameApplication(a.application_id, next); load() }
    catch (e) { toast('Could not rename: ' + (e as Error).message) }
  }

  const remove = async (a: Application) => {
    if (!confirm(`Delete "${a.name}"?`)) return
    try { await api.deleteApplication(a.application_id); toast(`Deleted ${a.name}`); load() }
    catch (e) { toast((e as Error).message) }
  }

  const copy = (id: string) => { navigator.clipboard?.writeText(id); toast('Application id copied') }

  return (
    <div className="home">
      <h1>Applications</h1>
      <p className="lead">Templates belong to an application. API keys are assigned to applications, and may render only what they are assigned to.</p>

      <div className="card">
        <div className="actions">
          <input value={name} placeholder="New application name" onChange={e => setName(e.target.value)}
                 onKeyDown={e => { if (e.key === 'Enter') create() }} />
          <button className="btn primary" onClick={create}>Create</button>
        </div>
      </div>

      <div className="card">
        {error && <div className="err">{error} — is the API running on port 5080?</div>}
        {items === null && !error && <div className="empty">Loading…</div>}
        {items?.length === 0 && <div className="empty">No applications yet. Create one to upload templates into it.</div>}
        {items?.map(a => (
          <div key={a.application_id} className="tpl">
            <div>
              <div className="n">{a.name}</div>
              <div className="s">{a.template_count} template{a.template_count === 1 ? '' : 's'} · <span className="mono">{a.application_id}</span>
                {' '}<button className="link" onClick={() => copy(a.application_id)}>copy id</button></div>
            </div>
            <button className="btn primary" onClick={() => onOpen(a.application_id)}>Templates</button>
            <button className="btn" onClick={() => rename(a)}>Rename</button>
            <button className="btn" onClick={() => remove(a)}>Delete</button>
          </div>))}
      </div>
    </div>)
}
