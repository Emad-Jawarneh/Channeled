import { useCallback, useLayoutEffect, useRef } from "react";

/**
 * A function identity that never changes but always calls the latest `fn`. Lets memoized children receive
 * callbacks that read current state without re-rendering whenever that state changes.
 */
export function useStableCallback<A extends unknown[], R>(fn: (...args: A) => R): (...args: A) => R {
  const ref = useRef(fn);
  useLayoutEffect(() => {
    ref.current = fn;
  });
  return useCallback((...args: A) => ref.current(...args), []);
}
