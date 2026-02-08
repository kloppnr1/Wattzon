import { useState, useEffect, useRef } from 'react';

const DAWA_URL = 'https://api.dataforsyningen.dk/adresser/autocomplete';

export function useDawaSearch(query, { enabled = true, limit = 8 } = {}) {
  const [results, setResults] = useState([]);
  const [loading, setLoading] = useState(false);
  const abortRef = useRef(null);

  useEffect(() => {
    if (!enabled || !query || query.length < 2) {
      setResults([]);
      return;
    }

    // Debounce
    const timer = setTimeout(() => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      setLoading(true);
      fetch(`${DAWA_URL}?q=${encodeURIComponent(query)}&per_side=${limit}`, {
        signal: controller.signal,
      })
        .then((res) => res.json())
        .then((data) => {
          setResults(
            data.map((item) => ({
              text: item.tekst,
              darId: item.adresse.id,
            }))
          );
        })
        .catch((err) => {
          if (err.name !== 'AbortError') setResults([]);
        })
        .finally(() => setLoading(false));
    }, 250);

    return () => {
      clearTimeout(timer);
      abortRef.current?.abort();
    };
  }, [query, enabled, limit]);

  return { results, loading };
}
