"use client";

import { useEffect, useRef, useState } from "react";

const DURATION_MS = 600;

/**
 * Anima un número desde su valor anterior hasta `target` con easing.
 * Respeta prefers-reduced-motion: en ese caso devuelve el valor directo.
 */
export function useCountUp(target: number): number {
  const [display, setDisplay] = useState<number>(target);
  const previousRef = useRef<number>(target);

  useEffect(() => {
    const from = previousRef.current;
    previousRef.current = target;

    if (from === target) return;

    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      setDisplay(target);
      return;
    }

    let frame = 0;
    const start = performance.now();

    const tick = (now: number): void => {
      const progress = Math.min((now - start) / DURATION_MS, 1);
      const eased = 1 - Math.pow(1 - progress, 3); // easeOutCubic
      setDisplay(Math.round(from + (target - from) * eased));
      if (progress < 1) frame = requestAnimationFrame(tick);
    };

    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [target]);

  return display;
}
