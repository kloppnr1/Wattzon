import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';

export default function CustomerDetail() {
  const { id } = useParams();
  const [customer, setCustomer] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    api.getCustomer(id)
      .then(setCustomer)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">Loading customer...</p>
        </div>
      </div>
    );
  }
  if (error) return <div className="p-8"><div className="bg-rose-50 border border-rose-200 rounded-xl px-4 py-3 text-sm text-rose-600">{error}</div></div>;
  if (!customer) return <div className="p-8"><p className="text-sm text-slate-500">Customer not found.</p></div>;

  return (
    <div className="p-8 max-w-4xl mx-auto">
      <Link to="/customers" className="inline-flex items-center gap-1.5 text-sm text-slate-400 hover:text-teal-600 mb-4 transition-colors font-medium">
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
        </svg>
        Back to customers
      </Link>

      <div className="flex items-center gap-4 mb-6 animate-fade-in-up">
        <div className="w-14 h-14 rounded-2xl bg-teal-500 flex items-center justify-center shadow-lg shadow-teal-500/25">
          <span className="text-base font-bold text-white">
            {customer.name.split(' ').map(n => n[0]).join('').slice(0, 2).toUpperCase()}
          </span>
        </div>
        <div>
          <h1 className="text-3xl font-bold text-slate-900 tracking-tight">{customer.name}</h1>
          <p className="text-sm text-slate-500 font-medium">
            {customer.contactType} &middot; <span className="font-mono text-xs bg-slate-100 px-2 py-0.5 rounded-md">{customer.cprCvr}</span>
          </p>
        </div>
        <span className={`ml-auto inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold ${
          customer.status === 'active' ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-slate-100 text-slate-500'
        }`}>
          <span className={`w-1.5 h-1.5 rounded-full ${customer.status === 'active' ? 'bg-emerald-400' : 'bg-slate-400'}`} />
          {customer.status}
        </span>
      </div>

      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden mb-5 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-1 h-4 rounded-full bg-teal-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Contracts</h3>
          </div>
          <span className="text-xs font-semibold text-teal-600 bg-teal-50 px-2.5 py-0.5 rounded-full">{customer.contracts.length}</span>
        </div>
        {customer.contracts.length === 0 ? (
          <div className="p-10 text-center">
            <p className="text-sm text-slate-400 font-medium">No contracts yet.</p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-50 bg-slate-50/50">
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">GSRN</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Billing</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Payment</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Start</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {customer.contracts.map((c, i) => (
                <tr key={c.id} className={`transition-colors duration-150 animate-slide-in opacity-0 ${i % 2 === 0 ? 'bg-white hover:bg-teal-50/30' : 'bg-slate-50 hover:bg-teal-50/50'}`} style={{ animationDelay: `${i * 40}ms` }}>
                  <td className="px-4 py-1.5">
                    <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">{c.gsrn}</span>
                  </td>
                  <td className="px-4 py-1.5 text-xs text-slate-600 capitalize font-medium">{c.billingFrequency}</td>
                  <td className="px-4 py-1.5 text-xs text-slate-600 capitalize font-medium">{c.paymentModel}</td>
                  <td className="px-4 py-1.5 text-xs text-slate-500">{c.startDate}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-1 h-4 rounded-full bg-teal-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Metering Points</h3>
          </div>
          <span className="text-xs font-semibold text-teal-600 bg-teal-50 px-2.5 py-0.5 rounded-full">{customer.meteringPoints.length}</span>
        </div>
        {customer.meteringPoints.length === 0 ? (
          <div className="p-10 text-center">
            <p className="text-sm text-slate-400 font-medium">No metering points linked.</p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-50 bg-slate-50/50">
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">GSRN</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Type</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Settlement</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Grid Area</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Status</th>
                <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">Supply Period</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {customer.meteringPoints.map((mp, i) => (
                <tr key={mp.gsrn} className={`transition-colors duration-150 animate-slide-in opacity-0 ${i % 2 === 0 ? 'bg-white hover:bg-teal-50/30' : 'bg-slate-50 hover:bg-teal-50/50'}`} style={{ animationDelay: `${i * 40}ms` }}>
                  <td className="px-4 py-1.5">
                    <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">{mp.gsrn}</span>
                  </td>
                  <td className="px-4 py-1.5 text-xs text-slate-600 font-medium">{mp.type}</td>
                  <td className="px-4 py-1.5 text-xs text-slate-600 font-medium">{mp.settlementMethod}</td>
                  <td className="px-4 py-1.5 text-xs text-slate-600">{mp.gridAreaCode} <span className="text-slate-400">({mp.priceArea})</span></td>
                  <td className="px-4 py-1.5">
                    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${
                      mp.connectionStatus === 'connected' ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'
                    }`}>
                      <span className={`w-1.5 h-1.5 rounded-full ${mp.connectionStatus === 'connected' ? 'bg-emerald-400' : 'bg-slate-400'}`} />
                      {mp.connectionStatus}
                    </span>
                  </td>
                  <td className="px-4 py-1.5 text-xs text-slate-500">
                    {mp.supplyStart
                      ? `${mp.supplyStart}${mp.supplyEnd ? ` – ${mp.supplyEnd}` : ' – ongoing'}`
                      : <span className="text-slate-300">—</span>}
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
