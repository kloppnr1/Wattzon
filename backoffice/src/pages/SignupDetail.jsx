import { useState, useEffect } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { api } from '../api';

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
    <span className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {status}
    </span>
  );
}

export default function SignupDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [signup, setSignup] = useState(null);
  const [events, setEvents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [cancelling, setCancelling] = useState(false);

  useEffect(() => {
    setLoading(true);
    Promise.all([api.getSignup(id), api.getSignupEvents(id)])
      .then(([s, e]) => { setSignup(s); setEvents(e); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  async function handleCancel() {
    if (!signup || !confirm('Cancel this signup?')) return;
    setCancelling(true);
    try {
      await api.cancelSignup(signup.signupNumber);
      const s = await api.getSignup(id);
      setSignup(s);
    } catch (e) {
      setError(e.message);
    } finally {
      setCancelling(false);
    }
  }

  function handleCreateCorrected() {
    // Navigate to new signup with pre-fill data from this rejected signup
    const params = new URLSearchParams({
      correctedFromId: signup.id,
      correctedFromNumber: signup.signupNumber,
      darId: signup.darId,
      gsrn: signup.gsrn,
      productId: signup.productId,
      productName: signup.productName,
      customerName: signup.customerName,
      cprCvr: signup.cprCvr,
      contactType: signup.contactType,
      type: signup.type,
    });
    navigate(`/signups/new?${params.toString()}`);
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">Loading...</p>
        </div>
      </div>
    );
  }
  if (error) return <div className="p-8"><div className="bg-rose-50 border border-rose-200 rounded-xl px-4 py-3 text-sm text-rose-600">{error}</div></div>;
  if (!signup) return <div className="p-8"><p className="text-sm text-slate-500">Signup not found.</p></div>;

  const canCancel = signup.status === 'registered' || signup.status === 'processing';
  const correctionChain = signup.correctionChain || [];

  return (
    <div className="p-8 max-w-3xl mx-auto">
      <Link to="/signups" className="inline-flex items-center gap-1.5 text-sm text-slate-400 hover:text-teal-600 mb-4 transition-colors font-medium">
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
        </svg>
        Back to signups
      </Link>

      <div className="flex items-center justify-between mb-6 animate-fade-in-up">
        <div className="flex items-center gap-3">
          <h1 className="text-3xl font-bold text-slate-900 tracking-tight">{signup.signupNumber}</h1>
          <StatusBadge status={signup.status} />
        </div>
        {canCancel && (
          <button
            onClick={handleCancel}
            disabled={cancelling}
            className="inline-flex items-center gap-1.5 px-4 py-2 bg-white text-rose-600 text-sm font-semibold rounded-xl border-2 border-rose-200 hover:bg-rose-50 hover:border-rose-300 disabled:opacity-40 transition-all"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
            </svg>
            {cancelling ? 'Cancelling...' : 'Cancel'}
          </button>
        )}
      </div>

      {/* Correction origin banner */}
      {signup.correctedFromId && signup.correctedFromSignupNumber && (
        <div className="mb-5 bg-teal-50 border border-teal-200 rounded-2xl p-4 flex items-center gap-3 animate-fade-in-up">
          <div className="w-8 h-8 rounded-lg bg-teal-500 flex items-center justify-center shrink-0">
            <svg className="w-4 h-4 text-white" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 12c0-1.232-.046-2.453-.138-3.662a4.006 4.006 0 0 0-3.7-3.7 48.678 48.678 0 0 0-7.324 0 4.006 4.006 0 0 0-3.7 3.7c-.017.22-.032.441-.046.662M19.5 12l3-3m-3 3-3-3m-12 3c0 1.232.046 2.453.138 3.662a4.006 4.006 0 0 0 3.7 3.7 48.656 48.656 0 0 0 7.324 0 4.006 4.006 0 0 0 3.7-3.7c.017-.22.032-.441.046-.662M4.5 12l3 3m-3-3-3 3" />
            </svg>
          </div>
          <div>
            <p className="text-sm font-semibold text-teal-800">Correction of rejected signup</p>
            <p className="text-xs text-teal-600 mt-0.5">
              This signup corrects{' '}
              <Link to={`/signups/${signup.correctedFromId}`} className="font-semibold underline hover:text-teal-800">
                {signup.correctedFromSignupNumber}
              </Link>
            </p>
          </div>
        </div>
      )}

      {/* Rejection banner with create corrected button */}
      {signup.status === 'rejected' && signup.rejectionReason && (
        <div className="mb-6 bg-rose-50 border border-rose-200 rounded-2xl p-5 animate-fade-in-up">
          <div className="flex items-start gap-3">
            <div className="w-9 h-9 rounded-xl bg-rose-500 flex items-center justify-center shrink-0 shadow-md shadow-rose-500/20">
              <svg className="w-4 h-4 text-white" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z" />
              </svg>
            </div>
            <div className="flex-1">
              <p className="text-sm font-semibold text-rose-800">Rejected by DataHub</p>
              <p className="text-sm text-rose-600 mt-1">{signup.rejectionReason}</p>
              <button
                onClick={handleCreateCorrected}
                className="inline-flex items-center gap-1.5 mt-3 px-4 py-2 bg-teal-500 text-white text-sm font-semibold rounded-xl shadow-md shadow-teal-500/20 hover:shadow-lg hover:-translate-y-0.5 transition-all duration-200"
              >
                Create corrected signup
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
                </svg>
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Correction chain */}
      {correctionChain.length > 1 && (
        <div className="mb-5 bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up">
          <div className="px-5 py-3.5 border-b border-slate-100 flex items-center gap-2">
            <div className="w-1 h-4 rounded-full bg-teal-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Correction History</h3>
          </div>
          <div className="p-4">
            <div className="flex items-center gap-2 flex-wrap">
              {correctionChain.map((link, i) => (
                <div key={link.id} className="flex items-center gap-2">
                  {i > 0 && (
                    <svg className="w-4 h-4 text-slate-300 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
                    </svg>
                  )}
                  {link.id === signup.id ? (
                    <span className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-teal-50 text-teal-700 border-2 border-teal-300">
                      <StatusDot status={link.status} />
                      {link.signupNumber}
                    </span>
                  ) : (
                    <Link
                      to={`/signups/${link.id}`}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-50 text-slate-600 border border-slate-200 hover:border-teal-300 hover:text-teal-700 transition-colors"
                    >
                      <StatusDot status={link.status} />
                      {link.signupNumber}
                    </Link>
                  )}
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden mb-5 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center gap-2">
          <div className="w-1 h-4 rounded-full bg-teal-500" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Details</h3>
        </div>
        <div className="grid grid-cols-2 divide-y divide-slate-100">
          <Field label="Type" value={signup.type === 'move_in' ? 'Move-in (BRS-009)' : 'Supplier switch (BRS-001)'} />
          <Field label="Effective Date" value={signup.effectiveDate} />
          <Field label="GSRN" value={<span className="font-mono text-xs bg-slate-100 px-2 py-1 rounded-md text-slate-600">{signup.gsrn}</span>} />
          <Field label="DAR ID" value={<span className="font-mono text-xs text-slate-400">{signup.darId}</span>} />
          <Field label="Product" value={signup.productName} />
          <Field label="Created" value={new Date(signup.createdAt).toLocaleString('da-DK')} />
        </div>
      </div>

      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden mb-5 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center gap-2">
          <div className="w-1 h-4 rounded-full bg-teal-500" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Customer</h3>
        </div>
        <div className="grid grid-cols-2 divide-y divide-slate-100">
          <Field
            label="Name"
            value={
              <Link to={`/customers/${signup.customerId}`} className="font-semibold text-teal-600 hover:text-teal-800 transition-colors">
                {signup.customerName}
              </Link>
            }
          />
          <Field label="CPR/CVR" value={<span className="font-mono text-xs bg-slate-100 px-2 py-1 rounded-md text-slate-500">{signup.cprCvr}</span>} />
          <Field label="Contact Type" value={signup.contactType} />
        </div>
      </div>

      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center gap-2">
          <div className="w-1 h-4 rounded-full bg-teal-500" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Process Timeline</h3>
        </div>
        {events.length === 0 ? (
          <div className="p-10 text-center">
            <div className="w-12 h-12 rounded-2xl bg-slate-50 flex items-center justify-center mx-auto mb-2">
              <svg className="w-6 h-6 text-slate-300" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 6v6h4.5m4.5 0a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
              </svg>
            </div>
            <p className="text-sm text-slate-400 font-medium">No events yet</p>
          </div>
        ) : (
          <div className="p-5">
            <div className="relative">
              <div className="absolute left-[14px] top-2 bottom-2 w-px bg-teal-200" />
              <div className="space-y-4">
                {events.map((evt, i) => (
                  <div key={i} className="flex gap-4 relative animate-slide-in opacity-0" style={{ animationDelay: `${i * 80}ms` }}>
                    <div className={`w-7 h-7 rounded-full flex items-center justify-center shrink-0 z-10 ${
                      i === 0 ? 'bg-teal-500 shadow-md shadow-teal-500/25' : 'bg-slate-100 border-2 border-slate-200'
                    }`}>
                      <div className={`w-2 h-2 rounded-full ${i === 0 ? 'bg-white' : 'bg-slate-400'}`} />
                    </div>
                    <div className="pb-1 -mt-0.5">
                      <div className="flex items-baseline gap-2">
                        <span className="text-sm font-semibold text-slate-900">{evt.eventType}</span>
                        {evt.source && (
                          <span className="text-[11px] text-slate-400 bg-slate-100 px-2 py-0.5 rounded-md font-medium">{evt.source}</span>
                        )}
                      </div>
                      <p className="text-xs text-slate-400 mt-0.5 font-medium">
                        {new Date(evt.occurredAt).toLocaleString('da-DK')}
                      </p>
                      {evt.payload && (
                        <pre className="text-xs text-slate-600 bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 mt-2 overflow-x-auto">{evt.payload}</pre>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function StatusDot({ status }) {
  const cfg = statusStyles[status] || statusStyles.registered;
  return <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />;
}

function Field({ label, value }) {
  return (
    <div className="px-5 py-3.5">
      <dt className="text-[11px] font-bold text-slate-400 uppercase tracking-wider">{label}</dt>
      <dd className="text-sm text-slate-700 mt-1 font-medium">{value}</dd>
    </div>
  );
}
