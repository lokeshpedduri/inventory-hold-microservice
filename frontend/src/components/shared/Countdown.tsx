import { useState, useEffect } from 'react';

interface CountdownResult {
  display: string;
  isUrgent: boolean;
  isCritical: boolean;
  isExpired: boolean;
}

function computeRemaining(expiresAtUtc: string): number {
  return Math.max(0, new Date(expiresAtUtc).getTime() - Date.now());
}

/** Returns live countdown state, updating every second. */
export function useCountdown(expiresAtUtc: string): CountdownResult {
  const [remaining, setRemaining] = useState(() => computeRemaining(expiresAtUtc));

  useEffect(() => {
    // Recompute immediately when expiresAtUtc changes.
    setRemaining(computeRemaining(expiresAtUtc));

    const id = setInterval(() => {
      const r = computeRemaining(expiresAtUtc);
      setRemaining(r);
      if (r === 0) clearInterval(id);
    }, 1000);

    return () => clearInterval(id);
  }, [expiresAtUtc]);

  const totalSeconds = Math.floor(remaining / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;

  return {
    display: `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`,
    isUrgent: remaining < 120_000 && remaining > 0,   // < 2 min
    isCritical: remaining < 30_000 && remaining > 0,  // < 30 s
    isExpired: remaining === 0,
  };
}

interface Props {
  expiresAtUtc: string;
}

/** Self-contained countdown display component. */
export function Countdown({ expiresAtUtc }: Props) {
  const { display, isUrgent, isCritical, isExpired } = useCountdown(expiresAtUtc);

  const cls = ['countdown-display', isCritical ? 'critical' : isUrgent ? 'urgent' : '']
    .filter(Boolean)
    .join(' ');

  return (
    <div className="countdown">
      <span className={cls}>{isExpired ? '00:00' : display}</span>
      <span className="countdown-label">remaining</span>
    </div>
  );
}
