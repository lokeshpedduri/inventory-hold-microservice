import { useState } from 'react';
import { InventoryDashboard } from './components/InventoryDashboard';
import { CreateHoldForm } from './components/CreateHoldForm';
import { HoldsList } from './components/HoldsList';

type Tab = 'inventory' | 'new-hold' | 'holds';

const TABS: { id: Tab; label: string }[] = [
  { id: 'inventory', label: 'Inventory' },
  { id: 'new-hold',  label: 'New Hold' },
  { id: 'holds',     label: 'Holds' },
];

export function App() {
  const [tab, setTab] = useState<Tab>('inventory');

  return (
    <div className="app">
      {/* ── Header ── */}
      <header className="header">
        <span className="header-wordmark">Holdkeeper</span>
        <div className="header-sep" />
        <span className="header-subtitle">Inventory Reserve System</span>
        <div className="live-indicator" aria-label="Live data">
          <span className="live-dot" />
          LIVE
        </div>
      </header>

      {/* ── Tab nav ── */}
      <nav className="tab-nav" aria-label="Primary navigation">
        {TABS.map(t => (
          <button
            key={t.id}
            className={`tab-btn${tab === t.id ? ' active' : ''}`}
            onClick={() => setTab(t.id)}
            aria-current={tab === t.id ? 'page' : undefined}
          >
            {t.label}
          </button>
        ))}
      </nav>

      {/* ── Content ── */}
      <main className="main">
        {tab === 'inventory' && <InventoryDashboard />}
        {tab === 'new-hold'  && (
          <CreateHoldForm onHoldCreated={() => setTab('holds')} />
        )}
        {tab === 'holds'     && <HoldsList />}
      </main>
    </div>
  );
}
