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

  return (
    <div>
      {error && (
        <div className="mb-6 bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      <div className="bg-white rounded-xl shadow-sm ring-1 ring-slate-200 overflow-hidden">
        {loading ? (
          <div className="p-12 text-center">
            <div className="inline-block w-6 h-6 border-2 border-slate-200 border-t-indigo-500 rounded-full animate-spin" />
            <p className="text-sm text-slate-400 mt-3">Loading products...</p>
          </div>
        ) : products.length === 0 ? (
          <div className="p-12 text-center">
            <svg className="w-10 h-10 text-slate-300 mx-auto mb-3" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z" />
            </svg>
            <p className="text-sm font-medium text-slate-500">No products configured</p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-100">
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Product</th>
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Model</th>
                <th className="text-right text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Margin</th>
                <th className="text-right text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Supplement</th>
                <th className="text-right text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Subscription</th>
                <th className="text-center text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Green</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {products.map((p) => (
                <tr key={p.id} className="hover:bg-slate-50/60 transition-colors">
                  <td className="px-5 py-3.5">
                    <div>
                      <p className="text-sm font-medium text-slate-800">{p.name}</p>
                      {p.description && <p className="text-xs text-slate-400 mt-0.5">{p.description}</p>}
                    </div>
                  </td>
                  <td className="px-5 py-3.5">
                    <span className="inline-flex px-2.5 py-1 rounded-full text-xs font-medium bg-slate-100 text-slate-600 ring-1 ring-inset ring-slate-200">
                      {p.energyModel}
                    </span>
                  </td>
                  <td className="px-5 py-3.5 text-right">
                    <span className="text-sm font-mono text-slate-700">{p.margin_ore_per_kwh}</span>
                    <span className="text-xs text-slate-400 ml-1">ore/kWh</span>
                  </td>
                  <td className="px-5 py-3.5 text-right">
                    {p.supplement_ore_per_kwh != null ? (
                      <>
                        <span className="text-sm font-mono text-slate-700">{p.supplement_ore_per_kwh}</span>
                        <span className="text-xs text-slate-400 ml-1">ore/kWh</span>
                      </>
                    ) : (
                      <span className="text-slate-300">\u2014</span>
                    )}
                  </td>
                  <td className="px-5 py-3.5 text-right">
                    <span className="text-sm font-mono text-slate-700">{p.subscription_kr_per_month}</span>
                    <span className="text-xs text-slate-400 ml-1">kr/mo</span>
                  </td>
                  <td className="px-5 py-3.5 text-center">
                    {p.green_energy ? (
                      <span className="inline-flex items-center justify-center w-6 h-6 rounded-full bg-emerald-50">
                        <svg className="w-4 h-4 text-emerald-500" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                        </svg>
                      </span>
                    ) : (
                      <span className="text-slate-300">\u2014</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {!loading && products.length > 0 && (
        <p className="text-xs text-slate-400 mt-3 px-1">{products.length} product{products.length !== 1 ? 's' : ''}</p>
      )}
    </div>
  );
}
