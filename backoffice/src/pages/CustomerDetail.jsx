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
      <div className="flex items-center justify-center py-20">
        <div className="w-6 h-6 border-2 border-slate-200 border-t-indigo-500 rounded-full animate-spin" />
      </div>
    );
  }
  if (error) return <div className="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-700">{error}</div>;
  if (!customer) return <p className="text-sm text-slate-500">Customer not found.</p>;

  return (
    <div className="max-w-4xl mx-auto">
      {/* Breadcrumb */}
      <Link to="/customers" className="inline-flex items-center gap-1 text-xs text-slate-400 hover:text-indigo-500 mb-4 transition-colors">
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
        </svg>
        Back to customers
      </Link>

      {/* Header */}
      <div className="flex items-center gap-3 mb-6">
        <div className="w-10 h-10 rounded-full bg-indigo-100 flex items-center justify-center">
          <span className="text-sm font-semibold text-indigo-600">
            {customer.name.split(' ').map(n => n[0]).join('').slice(0, 2).toUpperCase()}
          </span>
        </div>
        <div>
          <h2 className="text-lg font-semibold text-slate-800">{customer.name}</h2>
          <p className="text-xs text-slate-400">
            {customer.contactType} &middot; {customer.cprCvr}
          </p>
        </div>
        <span className={`ml-auto inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium ring-1 ring-inset ${
          customer.status === 'active'
            ? 'bg-emerald-50 text-emerald-700 ring-emerald-200'
            : 'bg-slate-50 text-slate-600 ring-slate-200'
        }`}>
          <span className={`w-1.5 h-1.5 rounded-full ${customer.status === 'active' ? 'bg-emerald-400' : 'bg-slate-400'}`} />
          {customer.status}
        </span>
      </div>

      {/* Contracts */}
      <div className="bg-white rounded-xl shadow-sm ring-1 ring-slate-200 overflow-hidden mb-6">
        <div className="px-5 py-3 bg-slate-50 border-b border-slate-200 flex items-center justify-between">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-500">Contracts</h3>
          <span className="text-xs text-slate-400">{customer.contracts.length}</span>
        </div>
        {customer.contracts.length === 0 ? (
          <div className="p-8 text-center">
            <p className="text-sm text-slate-400">No contracts yet.</p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-100">
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">GSRN</th>
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Billing</th>
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Payment</th>
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Start</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {customer.contracts.map((c) => (
                <tr key={c.id} className="hover:bg-slate-50/60 transition-colors">
                  <td className="px-5 py-3">
                    <span className="text-xs font-mono text-slate-500 bg-slate-100 px-2 py-0.5 rounded">{c.gsrn}</span>
                  </td>
                  <td className="px-5 py-3 text-sm text-slate-600 capitalize">{c.billingFrequency}</td>
                  <td className="px-5 py-3 text-sm text-slate-600 capitalize">{c.paymentModel}</td>
                  <td className="px-5 py-3 text-sm text-slate-600">{c.startDate}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Metering Points */}
      <div className="bg-white rounded-xl shadow-sm ring-1 ring-slate-200 overflow-hidden">
        <div className="px-5 py-3 bg-slate-50 border-b border-slate-200 flex items-center justify-between">
          <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-500">Metering Points</h3>
          <span className="text-xs text-slate-400">{customer.meteringPoints.length}</span>
        </div>
        {customer.meteringPoints.length === 0 ? (
          <div className="p-8 text-center">
            <p className="text-sm text-slate-400">No metering points linked.</p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-100">
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">GSRN</th>
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Type</th>
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Settlement</th>
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Grid Area</th>
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Status</th>
                <th className="text-left text-xs font-medium text-slate-400 uppercase tracking-wider px-5 py-3">Supply Period</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {customer.meteringPoints.map((mp) => (
                <tr key={mp.gsrn} className="hover:bg-slate-50/60 transition-colors">
                  <td className="px-5 py-3">
                    <span className="text-xs font-mono text-slate-500 bg-slate-100 px-2 py-0.5 rounded">{mp.gsrn}</span>
                  </td>
                  <td className="px-5 py-3 text-sm text-slate-600">{mp.type}</td>
                  <td className="px-5 py-3 text-sm text-slate-600">{mp.settlementMethod}</td>
                  <td className="px-5 py-3 text-sm text-slate-600">{mp.gridAreaCode} <span className="text-slate-400">({mp.priceArea})</span></td>
                  <td className="px-5 py-3">
                    <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11px] font-medium ring-1 ring-inset ${
                      mp.connectionStatus === 'connected'
                        ? 'bg-emerald-50 text-emerald-600 ring-emerald-200'
                        : 'bg-slate-50 text-slate-500 ring-slate-200'
                    }`}>
                      <span className={`w-1 h-1 rounded-full ${mp.connectionStatus === 'connected' ? 'bg-emerald-400' : 'bg-slate-400'}`} />
                      {mp.connectionStatus}
                    </span>
                  </td>
                  <td className="px-5 py-3 text-sm text-slate-600">
                    {mp.supplyStart
                      ? `${mp.supplyStart}${mp.supplyEnd ? ` \u2013 ${mp.supplyEnd}` : ' \u2013 ongoing'}`
                      : <span className="text-slate-300">\u2014</span>}
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
