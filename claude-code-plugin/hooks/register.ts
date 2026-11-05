import type { EngineInterface, Register } from 'claude-code'

// Writes the rate-limit windows Claude Code receives with each response to
// ~/.claude/usage-limits.json, which the desktop widget (usage-widget.ps1) reads.
// Draws nothing in Claude Code itself.

type Limit = { kind: string; percentUsed: number; resetsAt?: string }

function sharedPath($: EngineInterface) {
  const root = $.plugin.root.replace(/\\/g, '/')
  const home =
    /^([A-Za-z]:\/Users\/[^/]+)/i.exec(root)?.[1] ??
    /^(\/(?:home|Users)\/[^/]+)/.exec(root)?.[1]
  return home ? `${home}/.claude/usage-limits.json` : null
}

async function write($: EngineInterface, limits: readonly Limit[]) {
  if (limits.length === 0) return
  const path = sharedPath($)
  if (!path) return
  const snap = {
    limits: limits.map(l => ({ kind: l.kind, percentUsed: l.percentUsed, resetsAt: l.resetsAt })),
    seenAt: await $.clock.now(),
  }
  try {
    await $.fs.write(path, JSON.stringify(snap))
  } catch {
    // nothing to do: the widget keeps the last figures
  }
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const ran = await next(e)
    await write($, (await $.session.usage()).rateLimits)
    return ran
  })

  on('session.measure', async ($, e, next) => {
    await write($, e.rateLimits)
    return next(e)
  })
}
