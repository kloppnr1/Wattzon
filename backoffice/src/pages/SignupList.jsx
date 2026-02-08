import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';

const PAGE_SIZE = 50;

const STATUS_OPTIONS = ['all', 'registered', 'processing', 'active', 'rejected', 'cancelled'];

const statusStyles = {
  registered: { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' },
  processing: { dot: 'bg-teal-400', badge: 'bg-teal-50 text-teal-700' },
  active:     { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  rejected:   { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
  cancelled:  { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-500' },
};

function StatusBadge({ status }) {
  const cfg = statusStyles[status] || statusStyles.registered;
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {status}
    </span>
  );
}

export default function SignupList() {
  const [data, setData] = useState(null);
  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState('all');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const fetchPage = useCallback((p, status) => {
    setLoading(true);
    setError(null);
    api.getSignups({ status: status === 'all' ? undefined : status, page: p, pageSize: PAGE_SIZE })
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { fetchPage(page, filter); }, [page, filter, fetchPage]);

  function changeFilter(f) {
    setFilter(f);
    setPage(1);
  }

  const signups = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 1;

  return (
    <div className="p-8 max-w-6xl mx-auto">
      {/* Page header */}
      <div className="flex items-center justify-between mb-6 animate-fade-in-up">
        <div>
          <h1 className="text-3xl font-bold text-slate-900 tracking-tight">Signups</h1>
          <p className="text-base text-slate-500 mt-1">Manage customer onboarding requests.</p>
        </div>
        <Link
          to="/signups/new"
          className="inline-flex items-center gap-2 px-5 py-2.5 bg-teal-500 text-white text-sm font-semibold rounded-xl shadow-lg shadow-teal-500/25 hover:shadow-xl hover:shadow-teal-500/30 hover:-translate-y-0.5 transition-all duration-200"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
          New Signup
        </Link>
      </div>

      {/* Filter bar */}
      <div className="flex items-center gap-1 mb-5 bg-white rounded-xl p-1.5 w-fit shadow-sm border border-slate-100 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        {STATUS_OPTIONS.map((s) => (
          <button
            key={s}
            onClick={() => changeFilter(s)}
            className={`px-3.5 py-1.5 text-xs font-semibold rounded-lg transition-all duration-200 ${
              filter === s
                ? 'bg-teal-500 text-white shadow-md shadow-teal-500/20'
                : 'text-slate-500 hover:text-slate-700 hover:bg-slate-50'
            }`}
          >
            {s.charAt(0).toUpperCase() + s.slice(1)}
          </button>
        ))}
      </div>

      {error && (
        <div className="mb-5 bg-rose-50 border border-rose-200 rounded-xl px-4 py-3 text-sm text-rose-600 flex items-center gap-2">
          <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
          </svg>
          {error}
        </div>
      )}

      {/* Table */}
      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        {loading ? (
          <div className="p-14 text-center">
            <div className="inline-block w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
            <p className="text-sm text-slate-400 mt-3 font-medium">Loading signups...</p>
          </div>
        ) : signups.length === 0 ? (
          <div className="p-14 text-center">
            <div className="w-14 h-14 rounded-2xl bg-slate-50 flex items-center justify-center mx-auto mb-3">
              <svg className="w-7 h-7 text-slate-300" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" />
              </svg>
            </div>
            <p className="text-sm font-semibold text-slate-500">No signups found</p>
            <p className="text-xs text-slate-400 mt-1">
              {filter !== 'all' ? 'Try a different filter.' : 'Create one to get started.'}
            </p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-50 bg-slate-50/50">
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Signup</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Customer</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">GSRN</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Type</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Effective</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Status</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Created</th>
                <th className="px-4 py-2"><span className="sr-only">View</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {signups.map((s, i) => (
                <tr key={s.id} className={`transition-colors duration-150 ${i % 2 === 0 ? 'bg-white hover:bg-teal-50/30' : 'bg-slate-50 hover:bg-teal-50/50'}`}>
                  <td className="px-4 py-1.5">
                    <Link to={`/signups/${s.id}`} className="text-xs font-semibold text-teal-600 hover:text-teal-800 transition-colors">
                      {s.signupNumber}
                    </Link>
                  </td>
                  <td className="px-4 py-1.5 text-xs text-slate-700 font-medium">{s.customerName}</td>
                  <td className="px-4 py-1.5">
                    <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">
                      {s.gsrn}
                    </span>
                  </td>
                  <td className="px-4 py-1.5 text-xs text-slate-500">
                    {s.type === 'move_in' ? 'Move-in' : 'Switch'}
                  </td>
                  <td className="px-4 py-1.5 text-xs text-slate-500">{s.effectiveDate}</td>
                  <td className="px-4 py-1.5"><StatusBadge status={s.status} /></td>
                  <td className="px-4 py-1.5 text-xs text-slate-400">
                    {new Date(s.createdAt).toLocaleDateString('da-DK')}
                  </td>
                  <td className="px-4 py-1.5 text-right">
                    <Link to={`/signups/${s.id}`} className="text-slate-300 hover:text-teal-500 transition-colors">
                      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
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

      {/* Pagination */}
      {!loading && totalPages > 1 && (
        <div className="flex items-center justify-between mt-4 px-1">
          <p className="text-xs text-slate-400 font-medium">
            {totalCount.toLocaleString('da-DK')} signup{totalCount !== 1 ? 's' : ''}
          </p>
          <div className="flex items-center gap-1">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="px-3 py-1.5 text-xs font-medium text-slate-600 bg-white border border-slate-200 rounded-lg hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            >
              Previous
            </button>
            <span className="px-3 py-1.5 text-xs font-semibold text-slate-700">
              {page} / {totalPages}
            </span>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="px-3 py-1.5 text-xs font-medium text-slate-600 bg-white border border-slate-200 rounded-lg hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            >
              Next
            </button>
          </div>
        </div>
      )}

      {!loading && totalPages <= 1 && totalCount > 0 && (
        <p className="text-xs text-slate-400 mt-3 px-1 font-medium">
          {totalCount.toLocaleString('da-DK')} signup{totalCount !== 1 ? 's' : ''}
        </p>
      )}
    </div>
  );
}
