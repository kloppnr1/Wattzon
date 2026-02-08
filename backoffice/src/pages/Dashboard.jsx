import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';

export default function Dashboard() {
  const [signups, setSignups] = useState([]);
  const [customers, setCustomers] = useState([]);
  const [products, setProducts] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([
      api.getSignups().catch(() => []),
      api.getCustomers().catch(() => []),
      api.getProducts().catch(() => []),
    ]).then(([s, c, p]) => {
      setSignups(s);
      setCustomers(c);
      setProducts(p);
      setLoading(false);
    });
  }, []);

  const pending = signups.filter((s) => s.status === 'registered' || s.status === 'processing').length;
  const active = customers.filter((c) => c.status === 'active').length;
  const rejected = signups.filter((s) => s.status === 'rejected').length;
  const recent = [...signups].sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt)).slice(0, 5);

  const stats = [
    {
      label: 'Pending Signups',
      value: pending,
      icon: (
        <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 6v6h4.5m4.5 0a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
        </svg>
      ),
      gradient: 'from-amber-400 to-orange-500',
      bg: 'bg-amber-50',
      iconBg: 'bg-gradient-to-br from-amber-400 to-orange-500',
      link: '/signups?status=processing',
    },
    {
      label: 'Active Customers',
      value: active,
      icon: (
        <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z" />
        </svg>
      ),
      gradient: 'from-emerald-400 to-teal-500',
      bg: 'bg-emerald-50',
      iconBg: 'bg-gradient-to-br from-emerald-400 to-teal-500',
      link: '/customers',
    },
    {
      label: 'Rejected',
      value: rejected,
      icon: (
        <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z" />
        </svg>
      ),
      gradient: 'from-rose-400 to-pink-500',
      bg: 'bg-rose-50',
      iconBg: 'bg-gradient-to-br from-rose-400 to-pink-500',
      link: '/signups?status=rejected',
    },
    {
      label: 'Products',
      value: products.length,
      icon: (
        <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z" />
        </svg>
      ),
      gradient: 'from-amber-400 to-orange-500',
      bg: 'bg-amber-50',
      iconBg: 'bg-gradient-to-br from-amber-400 to-orange-500',
      link: '/products',
    },
  ];

  const statusStyles = {
    registered: { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' },
    processing: { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
    active:     { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
    rejected:   { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
    cancelled:  { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-500' },
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-amber-100 border-t-amber-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">Loading dashboard...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-6xl mx-auto">
      {/* Header */}
      <div className="mb-8 animate-fade-in-up">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">Dashboard</h1>
        <p className="text-base text-slate-500 mt-1">Overview of your electricity supplier operations.</p>
      </div>

      {/* Stat cards */}
      <div className="grid grid-cols-4 gap-5 mb-8 stagger">
        {stats.map((s) => (
          <Link
            key={s.label}
            to={s.link}
            className="card-lift group bg-white rounded-2xl p-5 shadow-sm border border-slate-100 hover:border-amber-200/60 animate-fade-in-up opacity-0"
          >
            <div className="flex items-center justify-between mb-4">
              <div className={`w-11 h-11 rounded-xl ${s.iconBg} flex items-center justify-center text-white shadow-lg shadow-${s.gradient.split('-')[1]}-500/25`}>
                {s.icon}
              </div>
              <svg className="w-5 h-5 text-slate-300 group-hover:text-amber-500 group-hover:translate-x-0.5 transition-all duration-200" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 19.5 15-15m0 0H8.25m11.25 0v11.25" />
              </svg>
            </div>
            <p className="text-3xl font-bold text-slate-900">{s.value}</p>
            <p className="text-sm text-slate-500 mt-0.5 font-medium">{s.label}</p>
          </Link>
        ))}
      </div>

      {/* Recent signups */}
      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up">
        <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-1 h-5 rounded-full bg-gradient-to-b from-amber-500 to-orange-500" />
            <h2 className="text-base font-semibold text-slate-900">Recent Signups</h2>
          </div>
          <Link to="/signups" className="text-sm font-medium text-amber-500 hover:text-amber-700 transition-colors flex items-center gap-1">
            View all
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="m8.25 4.5 7.5 7.5-7.5 7.5" />
            </svg>
          </Link>
        </div>
        {recent.length === 0 ? (
          <div className="p-14 text-center">
            <div className="w-14 h-14 rounded-2xl bg-slate-50 flex items-center justify-center mx-auto mb-3">
              <svg className="w-7 h-7 text-slate-300" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" />
              </svg>
            </div>
            <p className="text-sm font-medium text-slate-400">No signups yet</p>
            <p className="text-xs text-slate-400 mt-1">Create one to get started.</p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-50 bg-slate-50/50">
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">Signup</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">Customer</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">GSRN</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">Status</th>
                <th className="text-left text-[11px] font-semibold text-slate-400 uppercase tracking-wider px-6 py-3">Created</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {recent.map((s, i) => (
                <tr key={s.id} className="hover:bg-amber-50/40 transition-colors duration-150 animate-slide-in opacity-0" style={{ animationDelay: `${i * 60}ms` }}>
                  <td className="px-6 py-3.5">
                    <Link to={`/signups/${s.id}`} className="text-sm font-semibold text-amber-600 hover:text-amber-800 transition-colors">
                      {s.signupNumber}
                    </Link>
                  </td>
                  <td className="px-6 py-3.5 text-sm text-slate-700">{s.customerName}</td>
                  <td className="px-6 py-3.5">
                    <span className="text-xs font-mono text-slate-500 bg-slate-100 px-2 py-1 rounded-md">{s.gsrn}</span>
                  </td>
                  <td className="px-6 py-3.5">
                    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium ${(statusStyles[s.status] || statusStyles.registered).badge}`}>
                      <span className={`w-1.5 h-1.5 rounded-full ${(statusStyles[s.status] || statusStyles.registered).dot}`} />
                      {s.status}
                    </span>
                  </td>
                  <td className="px-6 py-3.5 text-sm text-slate-400">
                    {new Date(s.createdAt).toLocaleDateString('da-DK')}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
