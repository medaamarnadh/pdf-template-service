import { useState } from 'react'
import { authApi } from '../api'

interface Props { forced: boolean; onDone: () => void; onCancel?: () => void }

export default function ChangePassword({ forced, onDone, onCancel }: Props) {
  const [current, setCurrent] = useState(''); const [next, setNext] = useState(''); const [confirm, setConfirm] = useState('')
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false)
  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setError('')
    if (next.length < 12) return setError('New password must be at least 12 characters.')
    if (next !== confirm) return setError('Passwords do not match.')
    setBusy(true)
    try { await authApi.changePassword(current, next); onDone() }
    catch (err) { setError((err as Error).message) } finally { setBusy(false) }
  }
  return (
    <div className="home" style={{ maxWidth: 460 }}>
      <h1>{forced ? 'Set a new password' : 'Change password'}</h1>
      <p className="lead">{forced ? 'This account was created with a temporary password. Choose a new one to continue.' : 'Use at least 12 characters.'}</p>
      <form className="card insp" onSubmit={submit} style={{ padding: 20 }}>
        <label htmlFor="cur">Current password</label>
        <input id="cur" type="password" autoComplete="current-password" required value={current} onChange={e => setCurrent(e.target.value)} autoFocus />
        <label htmlFor="new">New password</label>
        <input id="new" type="password" autoComplete="new-password" required minLength={12} value={next} onChange={e => setNext(e.target.value)} />
        <label htmlFor="conf">Confirm new password</label>
        <input id="conf" type="password" autoComplete="new-password" required value={confirm} onChange={e => setConfirm(e.target.value)} />
        {error && <div className="err" role="alert">{error}</div>}
        <div className="actions">
          <button className="btn primary" type="submit" disabled={busy}>{busy ? 'Saving…' : 'Save password'}</button>
          {!forced && onCancel && <button className="btn" type="button" onClick={onCancel}>Cancel</button>}
        </div>
      </form>
    </div>)
}
