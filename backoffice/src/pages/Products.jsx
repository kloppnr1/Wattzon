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

      {loading ? (
        <div className="bg-white rounded-2xl shadow-sm border border-slate-100 p-14 text-center">
          <div className="inline-block w-8 h-8 border-[3px] border-amber-100 border-t-amber-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 mt-3 font-medium">Loading products...</p>
        </div>
      ) : products.length === 0 ? (
        <div className="bg-white rounded-2xl shadow-sm border border-slate-100 p-14 text-center">
          <div className="w-14 h-14 rounded-2xl bg-slate-50 flex items-center justify-center mx-auto mb-3">
            <svg className="w-7 h-7 text-slate-300" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z" />
            </svg>
          </div>
          <p className="text-sm font-semibold text-slate-500">No products configured</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 stagger">
          {products.map((p) => (
            <div
              key={p.id}
              className="card-lift bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up opacity-0"
            >
              {/* Gradient top strip */}
              <div className="h-1 bg-gradient-to-r from-amber-500 via-orange-500 to-red-500" />
              <div className="p-5">
                <div className="flex items-start justify-between mb-3">
                  <div className="flex items-center gap-3">
                    <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-amber-500 to-orange-500 flex items-center justify-center shadow-md shadow-amber-500/20">
                      <svg className="w-5 h-5 text-white" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z" />
                      </svg>
                    </div>
                    <div>
                      <h3 className="text-base font-semibold text-slate-900">{p.name}</h3>
                      <span className="inline-flex items-center px-2 py-0.5 rounded-md text-[11px] font-semibold bg-amber-50 text-amber-600 mt-0.5">
                        {p.energyModel}
                      </span>
                    </div>
                  </div>
                  {p.green_energy && (
                    <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-xs font-semibold bg-emerald-50 text-emerald-600 border border-emerald-200">
                      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                      </svg>
                      Green
                    </span>
                  )}
                </div>

                {p.description && (
                  <p className="text-sm text-slate-500 mb-4">{p.description}</p>
                )}

                <div className="grid grid-cols-3 gap-3">
                  <div className="bg-slate-50 rounded-xl p-3 text-center">
                    <p className="text-lg font-bold text-slate-900">{p.margin_ore_per_kwh}</p>
                    <p className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mt-0.5">ore/kWh margin</p>
                  </div>
                  <div className="bg-slate-50 rounded-xl p-3 text-center">
                    <p className="text-lg font-bold text-slate-900">{p.supplement_ore_per_kwh ?? '—'}</p>
                    <p className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mt-0.5">ore/kWh suppl.</p>
                  </div>
                  <div className="bg-slate-50 rounded-xl p-3 text-center">
                    <p className="text-lg font-bold text-slate-900">{p.subscription_kr_per_month}</p>
                    <p className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mt-0.5">kr/month</p>
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {!loading && products.length > 0 && (
        <p className="text-xs text-slate-400 mt-4 px-1 font-medium">{products.length} product{products.length !== 1 ? 's' : ''}</p>
      )}
    </div>
  );
}
