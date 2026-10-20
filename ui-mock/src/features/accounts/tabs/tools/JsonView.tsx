import { memo, type ReactNode } from 'react'

const TOKEN = /("(?:\\.|[^"\\])*")(\s*:)?|\b(true|false|null)\b|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g

/** ระบายสี JSON แบบง่ายด้วยโทเคน: key = primary, string = ok, number = warn, literal = muted */
function tokenize(src: string): ReactNode[] {
  const out: ReactNode[] = []
  let last = 0
  let k = 0
  for (const m of src.matchAll(TOKEN)) {
    const at = m.index ?? 0
    if (at > last) out.push(src.slice(last, at))
    if (m[1] !== undefined) {
      out.push(m[2] !== undefined
        ? <span key={k++}><span className="text-primary">{m[1]}</span>{m[2]}</span>
        : <span key={k++} className="text-ok">{m[1]}</span>)
    } else if (m[3] !== undefined) {
      out.push(<span key={k++} className="text-muted italic">{m[3]}</span>)
    } else {
      out.push(<span key={k++} className="text-warn">{m[4]}</span>)
    }
    last = at + m[0].length
  }
  if (last < src.length) out.push(src.slice(last))
  return out
}

export const JsonView = memo(function JsonView({ text }: { text: string }) {
  return <pre className="m-0 whitespace-pre-wrap break-all font-mono text-[13px] leading-relaxed text-fg">{tokenize(text)}</pre>
})
