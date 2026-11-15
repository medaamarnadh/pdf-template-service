import { useEffect, useState } from 'react'
import { api } from '../api'
import type { ApiKey, Application } from '../types'

interface Props { toast: (s: string) => void }

export default function Keys({ toast }: Props) {
  const [keys, setKeys] = useState<ApiKey[] | null>(null)
  const [apps, setApps] = useState<Application[]>([])
  const [error, setError] = useState('')
  const [name, setName] = useState('')
  const [chosen, setChosen] = useState<string[]>([])
  const [revealed, setRevealed] = useState<{ name: string; secret: string } | null>(null)

  const load = () => Promise.all([api.listKeys(), api.listApplications()])
    .then(([k, a]) => { setKeys(k); setApps(a) })
    .catch(e => setError(e.message))
  useEffect(() => { load() }, [])

  const toggle = (id: string) => setChosen(c => c.includes(id) ? c.filter(x => x !== id) : [...c, id])

  const create = async () => {
    if (!name.trim()) return toast('Give the key a name')
    if (chosen.length === 0) return toast('Assign the key to at least one application')
    try {
      const k = await api.createKey(name.trim(), chosen)
      setRevealed({ name: k.name, secret: k.secret })      // shown once; the API never returns it again
      setName(''); setChosen([]); load()
    } catch (e) { toast('Could not create: ' + (e as Error).message) }
  }

  const revoke = async (k: ApiKey) => {
    if (!confirm(`Revoke "${k.name}"? Calls using it stop working immediately.`)) return
    try { await api.revokeKey(k.api_key_id); toast(`Revoked ${k.name}`); load() }
    catch (e) { toast((e as Error).message) }
  }

  return (
    <div className="home">
      <h1>API keys</h1>
      <p className="lead">Share a key with a development team so their backend or front end can call <span className="mono">POST /api/v1/render</span>. A key reaches only the applications you assign to it.</p>

      {revealed && (
        <div className="card reveal">
          <b>Copy {revealed.name} now — it will not be shown again</b>
          <div className="mono secret">{revealed.secret}</div>
          <div className="actions">
            <button className="btn primary" onClick={() => { navigator.clipboard?.writeText(revealed.secret); toast('Secret copied') }}>Copy</button>
            <button className="btn" onClick={() => setRevealed(null)}>Done</button>
          </div>
        </div>)}

      <div className="card">
        <div className="actions">
          <input value={name} placeholder="New key name, e.g. Backend integration" onChange={e => setName(e.target.value)} />
          <button className="btn primary" onClick={create}>Create key</button>
        </div>
        {apps.length === 0
          ? <div className="empty">Create an application first — a key must be assigned to at least one.</div>
          : <div className="checks">{apps.map(a => (
              <label key={a.application_id}>
                <input type="checkbox" checked={chosen.includes(a.application_id)} onChange={() => toggle(a.application_id)} />
                {a.name}
              </label>))}
            </div>}
      </div>

      <div className="card">
        {error && <div className="err">{error} — is the API running on port 5080?</div>}
        {keys === null && !error && <div className="empty">Loading…</div>}
        {keys?.length === 0 && <div className="empty">No keys yet.</div>}
        {keys?.map(k => (
          <div key={k.api_key_id} className="tpl">
            <div>
              <div className="n">{k.name} {k.revoked_at && <span className="badge">revoked</span>}</div>
              <div className="s"><span className="mono">{k.prefix}…</span> · {k.applications.map(a => a.name).join(', ') || 'no applications'}
                {' '}· last used {k.last_used_at ? new Date(k.last_used_at).toLocaleString() : 'never'}</div>
            </div>
            {!k.revoked_at && <button className="btn" onClick={() => revoke(k)}>Revoke</button>}
          </div>))}
      </div>
    </div>)
}
