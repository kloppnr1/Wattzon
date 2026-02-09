import { useState, useEffect } from 'react';
import { api } from '../api';

export default function Products() {
  const [products, setProducts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    api.getProducts()
      .then(setProducts)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">Loading products...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-6xl mx-auto">
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">Products</h1>
        <p className="text-base text-slate-500 mt-1">Energy products available for customer signups.</p>
      </div>

      {error && (
        <div className="mb-5 bg-rose-50 border border-rose-200 rounded-xl px-4 py-3 text-sm text-rose-600 flex items-center gap-2">
          <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
          </svg>
          {error}
        </div>
      )}

      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        {products.length === 0 ? (
          <div className="p-14 text-center">
            <div className="w-14 h-14 rounded-2xl bg-slate-50 flex items-center justify-center mx-auto mb-3">
              <svg className="w-7 h-7 text-slate-300" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z" />
              </svg>
            </div>
            <p className="text-sm font-semibold text-slate-500">No products configured</p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-50 bg-slate-50/50">
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Product</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Model</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Green</th>
                <th className="text-right text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Margin</th>
                <th className="text-right text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Supplement</th>
                <th className="text-right text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Subscription</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Description</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {products.map((p, i) => (
                <tr key={p.id} className={`transition-colors duration-150 animate-slide-in opacity-0 ${i % 2 === 0 ? 'bg-white hover:bg-teal-50/30' : 'bg-slate-50 hover:bg-teal-50/50'}`} style={{ animationDelay: `${i * 40}ms` }}>
                  <td className="px-4 py-1.5">
                    <span className="text-xs font-semibold text-slate-900">{p.name}</span>
                  </td>
                  <td className="px-4 py-1.5">
                    <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[11px] font-medium bg-teal-50 text-teal-600">
                      {p.energyModel}
                    </span>
                  </td>
                  <td className="px-4 py-1.5">
                    {p.green_energy ? (
                      <svg className="w-4 h-4 text-emerald-500" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                      </svg>
                    ) : (
                      <span className="text-slate-300">—</span>
                    )}
                  </td>
                  <td className="px-4 py-1.5 text-right">
                    <span className="text-xs font-semibold text-slate-900">{p.margin_ore_per_kwh}</span>
                    <span className="text-[10px] text-slate-400 ml-1">øre</span>
                  </td>
                  <td className="px-4 py-1.5 text-right">
                    <span className="text-xs font-semibold text-slate-900">{p.supplement_ore_per_kwh ?? '—'}</span>
                    {p.supplement_ore_per_kwh && <span className="text-[10px] text-slate-400 ml-1">øre</span>}
                  </td>
                  <td className="px-4 py-1.5 text-right">
                    <span className="text-xs font-semibold text-slate-900">{p.subscription_kr_per_month}</span>
                    <span className="text-[10px] text-slate-400 ml-1">kr</span>
                  </td>
                  <td className="px-4 py-1.5">
                    <span className="text-xs text-slate-500">{p.description || '—'}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {!loading && products.length > 0 && (
        <p className="text-xs text-slate-400 mt-4 px-1 font-medium">{products.length} product{products.length !== 1 ? 's' : ''}</p>
      )}
    </div>
  );
}
