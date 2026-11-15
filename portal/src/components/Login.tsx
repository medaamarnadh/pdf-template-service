import { useState } from 'react'
import { authApi, type Me } from '../api'

interface Props { onLogin: (me: Me) => void }

export default function Login({ onLogin }: Props) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setError('')
    try { onLogin(await authApi.login(email.trim(), password)) }
    catch (err) { setError((err as Error).message === 'Invalid credentials' ? 'Email or password is incorrect.' : (err as Error).message) }
    finally { setBusy(false) }
  }
  return (
    <div className="home" style={{ maxWidth: 420 }}>
      <h1>Sign in</h1>
      <p className="lead">PDF Template Service portal</p>
      <form className="card insp" onSubmit={submit} style={{ padding: 20 }}>
        <label htmlFor="email">Email</label>
        <input id="email" type="email" autoComplete="username" required value={email} onChange={e => setEmail(e.target.value)} autoFocus />
        <label htmlFor="password">Password</label>
        <input id="password" type="password" autoComplete="current-password" required value={password} onChange={e => setPassword(e.target.value)} />
        {error && <div className="err" role="alert">{error}</div>}
        <div className="actions"><button className="btn primary" type="submit" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button></div>
      </form>
    </div>)
}
