import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';

const statusStyles = {
  processed: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  dead_lettered: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
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

export default function InboundMessageDetail() {
  const { id } = useParams();
  const [message, setMessage] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    api.getInboundMessage(id)
      .then(setMessage)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">Loading message...</p>
        </div>
      </div>
    );
  }

  if (error || !message) {
    return (
      <div className="p-8 max-w-6xl mx-auto">
        <div className="text-center text-rose-600">Error: {error || 'Message not found'}</div>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-6xl mx-auto">
      {/* Breadcrumb */}
      <div className="mb-4 flex items-center gap-2 text-sm text-slate-500">
        <Link to="/messages" className="hover:text-teal-600">Messages</Link>
        <span>/</span>
        <span className="text-slate-900 font-medium">Inbound Message</span>
      </div>

      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">Inbound Message</h1>
        <p className="text-base text-slate-500 mt-1">{message.messageType}</p>
      </div>

      {/* Message metadata card */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">Message Metadata</h2>
        <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
          <div>
            <dt className="text-sm font-medium text-slate-500">Message ID</dt>
            <dd className="text-base font-mono text-slate-900 mt-1">{message.id}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">DataHub Message ID</dt>
            <dd className="text-base font-mono text-slate-900 mt-1">{message.datahubMessageId}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">Message Type</dt>
            <dd className="text-base font-semibold text-slate-900 mt-1">{message.messageType}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">Status</dt>
            <dd className="mt-1">
              <StatusBadge status={message.status} />
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">Correlation ID</dt>
            <dd className="text-base font-mono text-slate-700 mt-1">{message.correlationId || '-'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">Queue Name</dt>
            <dd className="text-base text-slate-900 mt-1">{message.queueName}</dd>
          </div>
        </dl>
      </div>

      {/* Timestamps */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">Timestamps</h2>
        <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
          <div>
            <dt className="text-sm font-medium text-slate-500">Received At</dt>
            <dd className="text-base text-slate-900 mt-1">{new Date(message.receivedAt).toLocaleString()}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">Processed At</dt>
            <dd className="text-base text-slate-900 mt-1">
              {message.processedAt ? new Date(message.processedAt).toLocaleString() : '-'}
            </dd>
          </div>
        </dl>
      </div>

      {/* Payload info */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">Payload Information</h2>
        <div>
          <dt className="text-sm font-medium text-slate-500">Payload Size</dt>
          <dd className="text-base text-slate-900 mt-1">{message.rawPayloadSize.toLocaleString()} bytes</dd>
        </div>
      </div>

      {/* Error details */}
      {message.errorDetails && (
        <div className="bg-white rounded-xl shadow-sm border border-rose-200 p-6 animate-fade-in-up" style={{ animationDelay: '240ms' }}>
          <h2 className="text-lg font-semibold text-rose-900 mb-4">Error Details</h2>
          <div className="p-4 bg-rose-50 border border-rose-200 rounded-lg">
            <pre className="text-sm text-rose-700 whitespace-pre-wrap font-mono">{message.errorDetails}</pre>
          </div>
        </div>
      )}
    </div>
  );
}
