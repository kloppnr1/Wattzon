import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';

const PAGE_SIZE = 50;

const statusStyles = {
  processed: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  dead_lettered: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
  sent: { dot: 'bg-teal-400', badge: 'bg-teal-50 text-teal-700' },
  acknowledged: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  acknowledged_error: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
};

function StatusBadge({ status }) {
  const cfg = statusStyles[status] || { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' };
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {status.replace('_', ' ')}
    </span>
  );
}

export default function Messages() {
  const [tab, setTab] = useState('inbound');
  const [stats, setStats] = useState(null);
  const [data, setData] = useState(null);
  const [page, setPage] = useState(1);
  const [filters, setFilters] = useState({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  // Fetch stats
  useEffect(() => {
    api.getMessageStats()
      .then(setStats)
      .catch(() => {});
  }, []);

  // Fetch data based on tab
  const fetchData = useCallback((currentTab, p, f) => {
    setError(null);
    const params = { page: p, pageSize: PAGE_SIZE, ...f };

    const promise = currentTab === 'inbound'
      ? api.getInboundMessages(params)
      : currentTab === 'outbound'
      ? api.getOutboundRequests(params)
      : api.getDeadLetters(params);

    promise
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    setPage(1);
    setFilters({});
    setData(null);
    fetchData(tab, 1, {});
  }, [tab, fetchData]);

  useEffect(() => {
    fetchData(tab, page, filters);
  }, [page, filters, tab, fetchData]);

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  if (loading && !stats) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">Loading messages...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-6xl mx-auto">
      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">Messages</h1>
        <p className="text-base text-slate-500 mt-1">Monitor DataHub messaging and integration logs.</p>
      </div>

      {/* Stats cards */}
      {stats && (
        <div className="grid grid-cols-4 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
          <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
            <div className="text-sm font-medium text-slate-500 mb-1">Total Inbound</div>
            <div className="text-3xl font-bold text-slate-900">{stats.totalInbound}</div>
          </div>
          <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-5 shadow-sm border border-emerald-100/50">
            <div className="text-sm font-medium text-emerald-600 mb-1">Processed</div>
            <div className="text-3xl font-bold text-emerald-700">{stats.processedCount}</div>
          </div>
          <div className="bg-gradient-to-br from-white to-rose-50/30 rounded-xl p-5 shadow-sm border border-rose-100/50">
            <div className="text-sm font-medium text-rose-600 mb-1">Dead Letters</div>
            <div className="text-3xl font-bold text-rose-700">{stats.deadLetterCount}</div>
          </div>
          <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
            <div className="text-sm font-medium text-teal-600 mb-1">Pending Outbound</div>
            <div className="text-3xl font-bold text-teal-700">{stats.pendingOutbound}</div>
          </div>
        </div>
      )}

      {/* Tabs */}
      <div className="flex items-center gap-1 mb-5 bg-white rounded-xl p-1.5 w-fit shadow-sm border border-slate-100 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        {['inbound', 'outbound', 'dead-letters'].map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`px-4 py-2 text-sm font-semibold rounded-lg transition-all duration-200 ${
              tab === t
                ? 'bg-teal-500 text-white shadow-md'
                : 'text-slate-600 hover:text-slate-900 hover:bg-slate-50'
            }`}
          >
            {t === 'dead-letters' ? 'Dead Letters' : t.charAt(0).toUpperCase() + t.slice(1)}
          </button>
        ))}
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        {error && (
          <div className="p-4 bg-rose-50 border-b border-rose-100 text-rose-700 text-sm">
            Error: {error}
          </div>
        )}

        <div className="overflow-x-auto">
          {tab === 'inbound' && (
            <table className="min-w-full divide-y divide-slate-200">
              <thead className="bg-slate-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Message Type</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Correlation ID</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Queue</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Status</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Received At</th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-slate-100">
                {loading && items.length === 0 ? (
                  <tr><td colSpan="5" className="px-6 py-12 text-center text-slate-500">Loading...</td></tr>
                ) : items.length === 0 ? (
                  <tr><td colSpan="5" className="px-6 py-12 text-center text-slate-500">No messages found.</td></tr>
                ) : (
                  items.map((msg) => (
                    <tr key={msg.id} className="hover:bg-slate-50 transition-colors">
                      <td className="px-6 py-4 whitespace-nowrap">
                        <Link to={`/messages/inbound/${msg.id}`} className="text-teal-600 font-medium hover:text-teal-700">
                          {msg.messageType}
                        </Link>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm font-mono text-slate-500">{msg.correlationId || '-'}</td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-700">{msg.queueName}</td>
                      <td className="px-6 py-4 whitespace-nowrap"><StatusBadge status={msg.status} /></td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">{new Date(msg.receivedAt).toLocaleString()}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          )}

          {tab === 'outbound' && (
            <table className="min-w-full divide-y divide-slate-200">
              <thead className="bg-slate-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Process Type</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">GSRN</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Correlation ID</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Status</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Sent At</th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-slate-100">
                {loading && items.length === 0 ? (
                  <tr><td colSpan="5" className="px-6 py-12 text-center text-slate-500">Loading...</td></tr>
                ) : items.length === 0 ? (
                  <tr><td colSpan="5" className="px-6 py-12 text-center text-slate-500">No requests found.</td></tr>
                ) : (
                  items.map((req) => (
                    <tr key={req.id} className="hover:bg-slate-50 transition-colors">
                      <td className="px-6 py-4 whitespace-nowrap">
                        <Link to={`/messages/outbound/${req.id}`} className="text-teal-600 font-medium hover:text-teal-700">
                          {req.processType}
                        </Link>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-700">{req.gsrn}</td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm font-mono text-slate-500">{req.correlationId || '-'}</td>
                      <td className="px-6 py-4 whitespace-nowrap"><StatusBadge status={req.status} /></td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">{new Date(req.sentAt).toLocaleString()}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          )}

          {tab === 'dead-letters' && (
            <table className="min-w-full divide-y divide-slate-200">
              <thead className="bg-slate-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Queue</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Error Reason</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Resolved</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">Failed At</th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-slate-100">
                {loading && items.length === 0 ? (
                  <tr><td colSpan="4" className="px-6 py-12 text-center text-slate-500">Loading...</td></tr>
                ) : items.length === 0 ? (
                  <tr><td colSpan="4" className="px-6 py-12 text-center text-slate-500">No dead letters found.</td></tr>
                ) : (
                  items.map((dl) => (
                    <tr key={dl.id} className="hover:bg-slate-50 transition-colors">
                      <td className="px-6 py-4 whitespace-nowrap">
                        <Link to={`/messages/dead-letters/${dl.id}`} className="text-teal-600 font-medium hover:text-teal-700">
                          {dl.queueName}
                        </Link>
                      </td>
                      <td className="px-6 py-4 text-sm text-slate-700 max-w-md truncate">{dl.errorReason}</td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <span className={`inline-flex px-2 py-1 text-xs font-medium rounded-full ${
                          dl.resolved ? 'bg-emerald-100 text-emerald-700' : 'bg-rose-100 text-rose-700'
                        }`}>
                          {dl.resolved ? 'Yes' : 'No'}
                        </span>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">{new Date(dl.failedAt).toLocaleString()}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          )}
        </div>

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="px-6 py-4 bg-slate-50 border-t border-slate-200 flex items-center justify-between">
            <div className="text-sm text-slate-600">
              Showing <span className="font-medium">{(page - 1) * PAGE_SIZE + 1}</span> to{' '}
              <span className="font-medium">{Math.min(page * PAGE_SIZE, totalCount)}</span> of{' '}
              <span className="font-medium">{totalCount}</span> items
            </div>
            <div className="flex gap-2">
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page === 1}
                className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                Previous
              </button>
              <button
                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                disabled={page === totalPages}
                className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
