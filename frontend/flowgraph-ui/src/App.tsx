import { useEffect, useMemo, useRef, useState } from 'react'
import ForceGraph2D from 'react-force-graph-2d'
import './App.css'

function App() {
  const [tab, setTab] = useState<'repos' | 'search' | 'trace' | 'impact' | 'jobs'>('repos')
  const [traceId, setTraceId] = useState('')
  const [selectedConnection, setSelectedConnection] = useState('')
  const { data: graphConnections } = useJson<GraphConnectionInfo[]>('/graph/connections', [])
  const defaultConnection = graphConnections?.find(c => c.isDefault)?.name ?? graphConnections?.[0]?.name ?? 'Default'
  const activeConnection = selectedConnection || defaultConnection

  return (
    <>
      <header className="header">
        <div className="brand">
          <div className="title">FlowGraph</div>
          <div className="subtitle">System interaction graph</div>
        </div>
        <div className="headerControls">
          <nav className="tabs">
            <Tab label="Repos" active={tab === 'repos'} onClick={() => setTab('repos')} />
            <Tab label="Jobs" active={tab === 'jobs'} onClick={() => setTab('jobs')} />
            <Tab label="Search" active={tab === 'search'} onClick={() => setTab('search')} />
            <Tab label="Trace" active={tab === 'trace'} onClick={() => setTab('trace')} />
            <Tab label="Impact" active={tab === 'impact'} onClick={() => setTab('impact')} />
          </nav>
          <label className="connectionSelect">
            Graph
            <select value={activeConnection} onChange={(e) => setSelectedConnection(e.target.value)}>
              {graphConnections?.map(c => (
                <option key={c.name} value={c.name}>{c.name}</option>
              )) ?? <option value={activeConnection}>{activeConnection}</option>}
            </select>
          </label>
        </div>
      </header>

      <main className="main">
        {tab === 'repos' && <ReposPage connection={activeConnection} />}
        {tab === 'jobs' && <JobsPage />}
        {tab === 'search' && <SearchPage connection={activeConnection} onTrace={(id) => { setTraceId(id); setTab('trace'); }} />}
        {tab === 'trace' && <TracePage connection={activeConnection} initialId={traceId} />}
        {tab === 'impact' && <ImpactPage connection={activeConnection} />}
      </main>
    </>
  )
}

export default App

function Tab(props: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button className={props.active ? 'tab active' : 'tab'} onClick={props.onClick}>
      {props.label}
    </button>
  )
}

type RepoState = {
  repoName: string
  remoteUrl: string | null
  branch: string | null
  solutionPath: string | null
  lastIndexedCommit: string | null
  lastIndexedAt: string | null
  status: string
  includePatterns?: string[]
}

type IndexingJob = {
  id: number
  repoName: string
  status: string
  statusMessage: string | null
  startedAt: string
  completedAt: string | null
  changedFilesCount: number
}

type GraphEntity = { kind: string; id: string; properties: Record<string, unknown> }
type GraphTriple = { source: GraphEntity; relation: number; target: GraphEntity; properties: Record<string, unknown> }
type GraphConnectionInfo = { name: string; isDefault: boolean }

function withConnection(path: string, connection: string) {
  const separator = path.includes('?') ? '&' : '?'
  return `${path}${separator}connection=${encodeURIComponent(connection)}`
}

function useJson<T>(url: string, deps: unknown[] = [], enabled = true) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!enabled) return
    let cancelled = false
    setLoading(true)
    setError(null)
    fetch(url)
      .then(async (r) => {
        if (!r.ok) {
          const text = await r.text().catch(() => 'no body')
          throw new Error(`${r.status} ${r.statusText}: ${text}`)
        }
        try {
          const text = await r.text()
          if (!text) return [] as unknown as T
          return JSON.parse(text) as T
        } catch (e: any) {
          throw new Error(`Data format error (JSON): ${e.message}`)
        }
      })
      .then((j) => {
        if (!cancelled) setData(j)
      })
      .catch((e) => {
        if (!cancelled) {
          console.error('Fetch error:', e)
          setError(String(e?.message ?? e))
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)

  return { data, error, loading }
}

function RepoSelector({ selected, onChange }: { selected: string[], onChange: (repos: string[]) => void }) {
  const { data: repos } = useJson<RepoState[]>('/repos', []);
  if (!repos || repos.length === 0) return null;

  return (
    <div className="repo-selector">
      <div className="repo-selector-title">Filter by Repository:</div>
      <div className="repo-selector-list">
        {repos.map(r => (
          <label key={r.repoName} className="repo-checkbox">
            <input 
              type="checkbox" 
              checked={selected.length === 0 || selected.includes(r.repoName)} 
              onChange={e => {
                let next: string[];
                if (e.target.checked) {
                  next = [...selected, r.repoName];
                  // If all selected, just clear it to mean "all"
                  if (next.length === repos.length) next = [];
                } else {
                  // If currently "all" (empty), and we uncheck one, we need to select all others
                  if (selected.length === 0) {
                    next = repos.map(x => x.repoName).filter(x => x !== r.repoName);
                  } else {
                    next = selected.filter(x => x !== r.repoName);
                  }
                }
                onChange(next);
              }}
            />
            {r.repoName}
          </label>
        ))}
      </div>
    </div>
  );
}

function ReposPage({ connection }: { connection: string }) {
  const [refreshKey, setRefreshKey] = useState(0)
  const { data, error, loading } = useJson<RepoState[]>('/repos', [refreshKey])

  const [repoName, setRepoName] = useState('')
  const [remoteUrl, setRemoteUrl] = useState('')
  const [branch, setBranch] = useState('main')
  const [mode, setMode] = useState<'incremental' | 'full'>('incremental')
  const [solutionPath, setSolutionPath] = useState<string>('')
  const [includes, setIncludes] = useState<string>('')
  const [localLoading, setLocalLoading] = useState(false)

  const formRef = useRef<HTMLDivElement>(null)

  async function submit() {
    setLocalLoading(true)
    const patterns = includes.split(',').map(s => s.trim()).filter(x => !!x);
    try {
      await fetch(`/repos/${encodeURIComponent(repoName)}/reindex`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          remoteUrl,
          branch,
          mode,
          solutionPath: solutionPath.trim() ? solutionPath.trim() : null,
          includePatterns: patterns,
          GraphConnection: connection,
        }),
      })
      setRefreshKey((k) => k + 1)
    } catch (e: any) {
      alert(`Reindex failed: ${e.message}`)
    } finally {
      setLocalLoading(false)
    }
  }

  function selectRepo(r: RepoState) {
    setRepoName(r.repoName)
    setRemoteUrl(r.remoteUrl ?? '')
    setBranch(r.branch ?? 'main')
    setSolutionPath(r.solutionPath ?? '')
    setIncludes((r.includePatterns ?? []).join(', '))
    formRef.current?.scrollIntoView({ behavior: 'smooth' })
  }

  async function deleteRepo(name: string, e: React.MouseEvent) {
    e.stopPropagation()
    if (!window.confirm(`Are you sure you want to delete repository '${name}'? This will remove all associated graph data and jobs.`)) {
      return
    }
    
    setLocalLoading(true)
    try {
      const r = await fetch(withConnection(`/repos/${encodeURIComponent(name)}`, connection), { method: 'DELETE' })
      if (!r.ok) throw new Error(await r.text())
      setRefreshKey(k => k + 1)
    } catch (e: any) {
      alert(`Delete failed: ${e.message}`)
    } finally {
      setLocalLoading(false)
    }
  }

  return (
    <section className="panel">
      <h2>Repositories</h2>
      <p className="muted">Add a repo by URL and trigger an incremental/full reindex.</p>

      <div className="form" ref={formRef}>
        <label>
          Repo name
          <input value={repoName} onChange={(e) => setRepoName(e.target.value)} />
        </label>
        <label>
          Remote URL (git)
          <input placeholder="https://github.com/org/repo.git" value={remoteUrl} onChange={(e) => setRemoteUrl(e.target.value)} />
        </label>
        <div className="row">
          <label>
            Branch
            <input value={branch} onChange={(e) => setBranch(e.target.value)} />
          </label>
          <label>
            Mode
            <select value={mode} onChange={(e) => setMode(e.target.value as any)}>
              <option value="incremental">incremental</option>
              <option value="full">full</option>
            </select>
          </label>
        </div>
        <label>
          Solution path (optional)
          <input placeholder="path/to/repo.sln" value={solutionPath} onChange={(e) => setSolutionPath(e.target.value)} />
        </label>
        <label>
          Include Patterns (optional, comma-separated)
          <input placeholder="MyNamespace, MyProject" value={includes} onChange={e => setIncludes(e.target.value)} />
        </label>
        <div className="row">
          <button className="primary" onClick={submit} disabled={!repoName || !remoteUrl || localLoading}>
            Reindex
          </button>
          <button onClick={() => setRefreshKey((k) => k + 1)} disabled={localLoading}>Refresh</button>
        </div>
      </div>

      <div className="spacer" />

      {loading || localLoading ? <p>Loading…</p> : null}
      {error ? <p className="error">{error}</p> : null}
      {data ? (
        <div className="list">
          {data.map((r) => (
            <div key={r.repoName} className="listItem clickable" onClick={() => selectRepo(r)}>
              <div className="row spaceBetween" style={{ marginBottom: 6 }}>
                <div className="listTitle" style={{ margin: 0 }}>{r.repoName}</div>
                <button className="danger small" onClick={(e) => deleteRepo(r.repoName, e)} disabled={localLoading}>Delete</button>
              </div>
              <div className="listMeta">
                <span>Status: {r.status}</span>
                <span>Last commit: {r.lastIndexedCommit ?? '—'}</span>
                <span>Last indexed: {r.lastIndexedAt ? new Date(r.lastIndexedAt).toLocaleString() : '—'}</span>
                {r.includePatterns && r.includePatterns.length > 0 && (
                  <span className="badge">Scope: {r.includePatterns.join(', ')}</span>
                )}
              </div>
            </div>
          ))}
        </div>
      ) : null}
    </section>
  )
}

function JobsPage() {
  const [refreshKey, setRefreshKey] = useState(0)
  const { data, error, loading } = useJson<IndexingJob[]>(`/jobs?take=50`, [refreshKey])

  useEffect(() => {
    if (data && data.some(j => j.status === 'RUNNING')) {
      const interval = setInterval(() => {
        setRefreshKey(k => k + 1)
      }, 2000)
      return () => clearInterval(interval)
    }
  }, [data])

  return (
    <section className="panel">
      <div className="row spaceBetween">
        <div>
          <h2>Indexing jobs</h2>
          <p className="muted">Latest jobs from SQLite.</p>
        </div>
        <button onClick={() => setRefreshKey((k) => k + 1)}>Refresh</button>
      </div>
      {loading ? <p>Loading…</p> : null}
      {error ? <p className="error">{error}</p> : null}
      {data ? (
        <div className="list">
          {data.map((j) => (
            <div key={j.id} className="listItem">
              <div className="listTitle">
                #{j.id} — {j.repoName}
              </div>
              <div className="listMeta">
                <span className={j.status === 'RUNNING' ? 'status-running' : ''}>Status: {j.status}</span>
                {j.statusMessage && <span className="status-message">Step: {j.statusMessage}</span>}
                <span>Changed files: {j.changedFilesCount}</span>
                <span>Started: {new Date(j.startedAt).toLocaleString()}</span>
                <span>Completed: {j.completedAt ? new Date(j.completedAt).toLocaleString() : '—'}</span>
              </div>
            </div>
          ))}
        </div>
      ) : null}
    </section>
  )
}

function SearchPage({ connection, onTrace }: { connection: string; onTrace: (id: string) => void }) {
  const [q, setQ] = useState('')
  const [repos, setRepos] = useState<string[]>([])
  
  const queryParams = new URLSearchParams({ query: q });
  repos.forEach(r => queryParams.append('repos', r));
  
  const { data, error, loading } = useJson<GraphEntity[]>(withConnection(`/graph/search?${queryParams.toString()}`, connection), [q, repos, connection], !!q)

  return (
    <section className="panel">
      <h2>Global Search</h2>
      <p className="muted">Search for any entity (class, method, topic) across your indexed repos.</p>
      
      <div className="form">
        <input placeholder="Search for 'OrderService' or '/orders'..." value={q} onChange={e => setQ(e.target.value)} />
      </div>

      <RepoSelector selected={repos} onChange={setRepos} />

      {loading ? <p>Searching…</p> : null}
      {error ? <p className="error">{error}</p> : null}
      {data ? (
        <div className="list">
          {data.map(e => (
            <div key={e.id} className="listItem">
              <div className="row spaceBetween">
                <div className="listTitle">{e.id}</div>
                <button className="small primary" onClick={() => onTrace(e.id)}>Trace</button>
              </div>
              <div className="listMeta">
                <span>Kind: {e.kind}</span>
                {Object.entries(e.properties).map(([k, v]) => (
                  <span key={k}>{k}: {String(v)}</span>
                ))}
              </div>
            </div>
          ))}
          {data.length === 0 && q && !loading ? <p>No results found.</p> : null}
        </div>
      ) : null}
    </section>
  )
}

type TraceResult = { startNode: GraphEntity | null; triples: GraphTriple[] }

function TracePage({ connection, initialId }: { connection: string; initialId: string }) {
  const [startId, setStartId] = useState(initialId)
  const [depth, setDepth] = useState(3)
  const [repos, setRepos] = useState<string[]>([])
  const [view, setView] = useState<'graph' | 'json'>('graph')
  
  const queryParams = new URLSearchParams({ start: startId, maxDepth: String(depth) });
  repos.forEach(r => queryParams.append('repos', r));

  const { data, error, loading } = useJson<TraceResult>(withConnection(`/graph/trace?${queryParams.toString()}`, connection), [startId, depth, repos, connection], !!startId)

  useEffect(() => {
    if (initialId) setStartId(initialId);
  }, [initialId]);

  return (
    <section className="panel">
      <div className="row spaceBetween">
        <div>
          <h2>Trace & Flow</h2>
          <p className="muted">Explore bidirectional interactions (Up/Down) from a starting entity.</p>
        </div>
        {data && (
          <div className="tabs">
            <Tab label="Graph" active={view === 'graph'} onClick={() => setView('graph')} />
            <Tab label="JSON" active={view === 'json'} onClick={() => setView('json')} />
          </div>
        )}
      </div>
      
      <div className="form">
        <label>
          Root Entity ID
          <input className="grow" placeholder="Fully qualified method/message ID" value={startId} onChange={(e) => setStartId(e.target.value)} />
        </label>
        <label>
          Exploration Depth
          <input
            style={{ width: 90 }}
            type="number"
            min={1}
            max={20}
            value={depth}
            onChange={(e) => setDepth(Number(e.target.value))}
          />
        </label>
      </div>

      <RepoSelector selected={repos} onChange={setRepos} />

      {loading ? <p>Tracing interaction graph bidirectional…</p> : null}
      {error ? <p className="error">{error}</p> : null}
      {data ? (
        view === 'graph' ? (
          <GraphVisualization triples={data.triples} startNode={data.startNode} onNodeClick={(nodeId) => { setStartId(nodeId); }} />
        ) : (
          <pre className="code">{JSON.stringify(data, null, 2)}</pre>
        )
      ) : null}
    </section>
  )
}

function GraphVisualization({ triples, startNode, onNodeClick }: { triples: GraphTriple[], startNode?: GraphEntity | null, onNodeClick?: (id: string) => void }) {
  const graphData = useMemo(() => {
    const nodesMap = new Map<string, any>()
    const links: any[] = []

    if (startNode) {
      nodesMap.set(startNode.id, { id: startNode.id, kind: startNode.kind, name: (startNode.properties.name as string) || startNode.id.split('.').pop() })
    }

    const RelationLabels = ['CALLS', 'PUBLISHES', 'CONSUMES', 'HANDLES', 'DEPENDS_ON', 'USES_TOPIC', 'TRIGGERS', 'IMPLEMENTS']

    triples.forEach((t) => {
      if (!nodesMap.has(t.source.id)) {
        nodesMap.set(t.source.id, { id: t.source.id, kind: t.source.kind, name: (t.source.properties.name as string) || t.source.id.split('.').pop() })
      }
      if (!nodesMap.has(t.target.id)) {
        nodesMap.set(t.target.id, { id: t.target.id, kind: t.target.kind, name: (t.target.properties.name as string) || t.target.id.split('.').pop() })
      }
      links.push({
        source: t.source.id,
        target: t.target.id,
        label: RelationLabels[t.relation] || 'REL'
      })
    })

    return {
      nodes: Array.from(nodesMap.values()),
      links
    }
  }, [triples])

  const containerRef = useRef<HTMLDivElement>(null)
  const [dimensions, setDimensions] = useState({ width: 800, height: 600 })

  useEffect(() => {
    if (containerRef.current) {
      setDimensions({
        width: containerRef.current.clientWidth,
        height: containerRef.current.clientHeight
      })
    }
  }, [])

  return (
    <div className="graphContainer" ref={containerRef}>
      <ForceGraph2D
        graphData={graphData}
        width={dimensions.width}
        height={dimensions.height}
        nodeLabel={(node: any) => `${node.kind}: ${node.id}`}
        nodeAutoColorBy="kind"
        linkDirectionalArrowLength={6}
        linkDirectionalArrowRelPos={1}
        linkCurvature={0.25}
        nodeCanvasObject={(node: any, ctx, globalScale) => {
          const label = node.name
          const fontSize = 12 / globalScale
          ctx.font = `${fontSize}px Sans-Serif`
          const textWidth = ctx.measureText(label).width
          const bckgDimensions = [textWidth, fontSize].map((n) => n + fontSize * 0.4) as [number, number]

          const isDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches
          
          // Draw node circle first
          ctx.beginPath()
          ctx.arc(node.x!, node.y!, 4 / globalScale, 0, 2 * Math.PI, false)
          ctx.fillStyle = node.color
          ctx.fill()

          // Draw label background
          ctx.fillStyle = isDark ? 'rgba(22, 23, 29, 0.9)' : 'rgba(255, 255, 255, 0.9)'
          ctx.fillRect(node.x! - bckgDimensions[0] / 2, node.y! - bckgDimensions[1] - (6 / globalScale), bckgDimensions[0], bckgDimensions[1])

          // Draw label text
          ctx.textAlign = 'center'
          ctx.textBaseline = 'middle'
          ctx.fillStyle = isDark ? '#f3f4f6' : '#08060d'
          ctx.fillText(label, node.x!, node.y! - bckgDimensions[1] / 2 - (6 / globalScale))

          node.__bckgDimensions = bckgDimensions // to use in nodePointerAreaPaint
        }}
        nodePointerAreaPaint={(node: any, color, ctx, globalScale) => {
          ctx.fillStyle = color
          const bckgDimensions = node.__bckgDimensions
          if (bckgDimensions) {
            ctx.fillRect(node.x! - bckgDimensions[0] / 2, node.y! - bckgDimensions[1] - (6 / globalScale), bckgDimensions[0], bckgDimensions[1])
          }
        }}
        onNodeClick={(node: any) => onNodeClick?.(node.id)}
      />
      <div className="graphOverlay">
        Scroll to zoom • Drag to pan • Click node to Trace • Hover for details
      </div>
    </div>
  )
}

function ImpactPage({ connection }: { connection: string }) {
  const [change, setChange] = useState('')
  const [depth, setDepth] = useState(3)
  const [repos, setRepos] = useState<string[]>([])
  
  const queryParams = new URLSearchParams({ change: change, maxDepth: String(depth) });
  repos.forEach(r => queryParams.append('repos', r));
  
  const { data, error, loading } = useJson<GraphEntity[]>(withConnection(`/graph/impact?${queryParams.toString()}`, connection), [change, depth, repos, connection], !!change)

  return (
    <section className="panel">
      <h2>Impact Analysis</h2>
      <p className="muted">Find all entities potentially affected by a change to the starting entity.</p>
      
      <div className="form">
        <label>
          Starting Entity ID
          <input placeholder="Message/Method/Class ID" value={change} onChange={(e) => setChange(e.target.value)} />
        </label>
        <label>
          Radius
          <input
            style={{ width: 90 }}
            type="number"
            min={1}
            max={10}
            value={depth}
            onChange={(e) => setDepth(Number(e.target.value))}
          />
        </label>
      </div>

      <RepoSelector selected={repos} onChange={setRepos} />

      {loading ? <p>Analyzing…</p> : null}
      {error ? <p className="error">{error}</p> : null}
      {data ? (
        <div className="list">
          {data.map((e) => (
            <div key={`${e.kind}:${e.id}`} className="listItem">
              <div className="listTitle">{e.id}</div>
              <div className="listMeta">
                <span>Kind: {e.kind}</span>
                {Object.entries(e.properties).map(([k, v]) => (
                  <span key={k}>{k}: {String(v)}</span>
                ))}
              </div>
            </div>
          ))}
          {data.length === 0 && change && !loading ? <p>No impacted entities found within this radius.</p> : null}
        </div>
      ) : null}
    </section>
  )
}
