import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';

export default function CustomerList() {
  const [customers, setCustomers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    api.getCustomers()
      .then(setCustomers)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  return (
    <div className="p-8 max-w-6xl mx-auto">
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">Customers</h1>
        <p className="text-base text-slate-500 mt-1">Active portfolio of electricity customers.</p>
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
        {loading ? (
          <div className="p-14 text-center">
            <div className="inline-block w-8 h-8 border-[3px] border-indigo-100 border-t-indigo-500 rounded-full animate-spin" />
            <p className="text-sm text-slate-400 mt-3 font-medium">Loading customers...</p>
          </div>
        ) : customers.length === 0 ? (
          <div className="p-14 text-center">
            <div className="w-14 h-14 rounded-2xl bg-slate-50 flex items-center justify-center mx-auto mb-3">
              <svg className="w-7 h-7 text-slate-300" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z" />
              </svg>
            </div>
            <p className="text-sm font-semibold text-slate-500">No customers yet</p>
            <p className="text-xs text-slate-400 mt-1">Customers appear here after signup activation.</p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-50 bg-slate-50/50">
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">Name</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">CPR/CVR</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">Type</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">Status</th>
                <th className="px-6 py-3"><span className="sr-only">View</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {customers.map((c, i) => (
                <tr key={c.id} className="hover:bg-indigo-50/40 transition-colors duration-150 animate-slide-in opacity-0" style={{ animationDelay: `${i * 40}ms` }}>
                  <td className="px-6 py-3.5">
                    <Link to={`/customers/${c.id}`} className="flex items-center gap-3">
                      <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-indigo-400 to-violet-500 flex items-center justify-center shadow-sm">
                        <span className="text-xs font-bold text-white">
                          {c.name.split(' ').map(n => n[0]).join('').slice(0, 2).toUpperCase()}
                        </span>
                      </div>
                      <span className="text-sm font-semibold text-indigo-600 hover:text-indigo-800 transition-colors">
                        {c.name}
                      </span>
                    </Link>
                  </td>
                  <td className="px-6 py-3.5">
                    <span className="text-xs font-mono text-slate-500 bg-slate-100 px-2 py-1 rounded-md">{c.cprCvr}</span>
                  </td>
                  <td className="px-6 py-3.5 text-sm text-slate-500 capitalize">{c.contactType}</td>
                  <td className="px-6 py-3.5">
                    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium ${
                      c.status === 'active' ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'
                    }`}>
                      <span className={`w-1.5 h-1.5 rounded-full ${c.status === 'active' ? 'bg-emerald-400' : 'bg-slate-400'}`} />
                      {c.status}
                    </span>
                  </td>
                  <td className="px-6 py-3.5 text-right">
                    <Link to={`/customers/${c.id}`} className="text-slate-300 hover:text-indigo-500 transition-colors">
                      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="m8.25 4.5 7.5 7.5-7.5 7.5" />
                      </svg>
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {!loading && customers.length > 0 && (
        <p className="text-xs text-slate-400 mt-3 px-1 font-medium">{customers.length} customer{customers.length !== 1 ? 's' : ''}</p>
      )}
    </div>
  );
}
