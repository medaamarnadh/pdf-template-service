import { useCallback, useEffect, useState } from 'react'
import TemplateList from './components/TemplateList'
import Editor from './components/Editor'
import Login from './components/Login'
import ChangePassword from './components/ChangePassword'
import Applications from './components/Applications'
import Keys from './components/Keys'
import { authApi, session, type Me } from './api'

export default function App() {
  const [me, setMe] = useState<Me | null | undefined>(undefined)   // undefined = checking session
  const [templateId, setTemplateId] = useState<string | null>(() => new URLSearchParams(location.search).get('t'))
  const [screen, setScreen] = useState<'apps' | 'keys'>('apps')
  const [applicationId, setApplicationId] = useState<string | null>(null)
  const [changing, setChanging] = useState(false)
  const [msg, setMsg] = useState(''); const [show, setShow] = useState(false)
  const toast = useCallback((s: string) => { setMsg(s); setShow(true); setTimeout(() => setShow(false), 2600) }, [])
  const open = (id: string | null) => { setTemplateId(id); history.replaceState(null, '', id ? `?t=${id}` : location.pathname) }

  useEffect(() => {
    if (session.token) authApi.me().then(setMe).catch(() => setMe(null)); else setMe(null)
    return session.onChange(() => { if (!session.token) { setMe(null); setTemplateId(null); setApplicationId(null); setScreen('apps') } })
  }, [])

  if (me === undefined) return <div className="home"><div className="empty">Checking session…</div></div>
  if (!me) return <><Login onLogin={setMe} /><div className={'toast' + (show ? ' show' : '')}>{msg}</div></>
  if (me.must_change_password || changing)
    return <ChangePassword forced={me.must_change_password} onCancel={() => setChanging(false)} onDone={() => { setChanging(false); setMe({ ...me, must_change_password: false }); toast('Password updated') }} />

  return (
    <>
      <div className="userbar">
        <span>{me.email} · {me.role}</span>
        <button className="link" onClick={() => setChanging(true)}>Change password</button>
        <button className="link" onClick={() => { authApi.logout(); toast('Signed out') }}>Sign out</button>
      </div>
      <nav className="topnav">
        <button className="link" onClick={() => { setScreen('apps'); setApplicationId(null); open(null) }}>Applications</button>
        <button className="link" onClick={() => { setScreen('keys'); setApplicationId(null); open(null) }}>API keys</button>
      </nav>
      {screen === 'keys'
        ? <Keys toast={toast} />
        : templateId
          ? <Editor templateId={templateId} onBack={() => open(null)} toast={toast} />
          : applicationId
            ? <TemplateList applicationId={applicationId} onBack={() => setApplicationId(null)} onOpen={open} toast={toast} />
            : <Applications onOpen={setApplicationId} toast={toast} />}
      <div className={'toast' + (show ? ' show' : '')}>{msg}</div>
    </>)
}
