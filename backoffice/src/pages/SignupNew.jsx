import { useState, useEffect, useRef } from 'react';
import { useNavigate, Link, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useDawaSearch } from '../hooks/useDawaSearch';
import { useTranslation } from '../i18n/LanguageContext';

const PAGE_STYLES = `
  .signup-grid-bg {
    background-image: radial-gradient(circle, rgb(148 163 184 / 0.07) 1px, transparent 1px);
    background-size: 24px 24px;
  }
  @keyframes wizard-enter {
    from { opacity: 0; transform: translateY(20px); }
    to { opacity: 1; transform: translateY(0); }
  }
  .wizard-enter {
    animation: wizard-enter 0.5s cubic-bezier(0.22, 1, 0.36, 1) both;
  }
  .step-watermark {
    font-size: 12rem;
    font-weight: 900;
    line-height: 0.8;
    letter-spacing: -0.06em;
    color: rgb(148 163 184 / 0.045);
    pointer-events: none;
    user-select: none;
  }
`;

export default function SignupNew() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [step, setStep] = useState(0);

  const STEPS = [
    { key: 'address', label: t('signupNew.stepAddress'), desc: t('signupNew.stepAddressDesc') },
    { key: 'product', label: t('signupNew.stepProduct'), desc: t('signupNew.stepProductDesc') },
    { key: 'customer', label: t('signupNew.stepCustomer'), desc: t('signupNew.stepCustomerDesc') },
    { key: 'confirm', label: t('signupNew.stepConfirm'), desc: t('signupNew.stepConfirmDesc') },
  ];
  const [products, setProducts] = useState([]);
  const [error, setError] = useState(null);
  const [submitting, setSubmitting] = useState(false);

  // Correction context from URL params (when creating from a rejected signup)
  const correctedFromId = searchParams.get('correctedFromId');
  const correctedFromNumber = searchParams.get('correctedFromNumber');

  // Step 0: Address
  const [addressMode, setAddressMode] = useState('search');
  const [addressQuery, setAddressQuery] = useState('');
  const [selectedAddress, setSelectedAddress] = useState(null);
  const [showSuggestions, setShowSuggestions] = useState(false);
  const [darId, setDarId] = useState('');
  const [gsrnInput, setGsrnInput] = useState('');
  const [meteringPoints, setMeteringPoints] = useState(null);
  const [selectedGsrn, setSelectedGsrn] = useState(null);
  const [lookingUp, setLookingUp] = useState(false);
  const suggestionsRef = useRef(null);

  const { results: addressSuggestions, loading: searchingAddress } = useDawaSearch(addressQuery, {
    enabled: addressMode === 'search' && !selectedAddress,
  });

  // Pre-fill from correction params
  const prefilled = useRef(false);
  useEffect(() => {
    if (prefilled.current) return;
    prefilled.current = true;

    const pfDarId = searchParams.get('darId');
    const pfGsrn = searchParams.get('gsrn');
    const pfProductId = searchParams.get('productId');
    const pfCustomerName = searchParams.get('customerName');
    const pfCprCvr = searchParams.get('cprCvr');
    const pfContactType = searchParams.get('contactType');
    const pfType = searchParams.get('type');

    if (pfDarId && pfGsrn) {
      setAddressMode('dar');
      setDarId(pfDarId);
      setSelectedGsrn(pfGsrn);
      setMeteringPoints([{ gsrn: pfGsrn, type: 'E17', grid_area_code: '—' }]);
    }
    if (pfProductId) setSelectedProduct(pfProductId);
    if (pfCustomerName) setCustomerName(pfCustomerName);
    if (pfCprCvr) setCprCvr(pfCprCvr);
    if (pfContactType) setContactType(pfContactType);
    if (pfType) setType(pfType);
  }, [searchParams]);

  useEffect(() => {
    function handleClick(e) {
      if (suggestionsRef.current && !suggestionsRef.current.contains(e.target)) {
        setShowSuggestions(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, []);

  function selectAddress(suggestion) {
    setSelectedAddress(suggestion);
    setAddressQuery(suggestion.text);
    setDarId(suggestion.darId);
    setShowSuggestions(false);
    lookupGsrn(suggestion.darId);
  }

  function clearAddress() {
    setSelectedAddress(null);
    setAddressQuery('');
    setDarId('');
    setGsrnInput('');
    setMeteringPoints(null);
    setSelectedGsrn(null);
  }

  async function lookupGsrn(id) {
    setLookingUp(true);
    setError(null);
    setMeteringPoints(null);
    setSelectedGsrn(null);
    try {
      const result = await api.lookupAddress(id);
      setMeteringPoints(result);
      const available = result.filter((mp) => !mp.has_active_process);
      if (available.length === 1) setSelectedGsrn(available[0].gsrn);
      else if (result.length === 1 && !result[0].has_active_process) setSelectedGsrn(result[0].gsrn);
    } catch (e) {
      setError(e.message);
    } finally {
      setLookingUp(false);
    }
  }

  async function validateGsrn(gsrn) {
    setLookingUp(true);
    setError(null);
    setMeteringPoints(null);
    setSelectedGsrn(null);
    setDarId('');
    try {
      const result = await api.validateGsrn(gsrn);
      setMeteringPoints(result);
      const available = result.filter((mp) => !mp.has_active_process);
      if (available.length === 1) setSelectedGsrn(available[0].gsrn);
    } catch (e) {
      setError(e.message);
    } finally {
      setLookingUp(false);
    }
  }

  // Step 1: Product
  const [selectedProduct, setSelectedProduct] = useState(null);

  // Step 2: Customer
  const [customerName, setCustomerName] = useState('');
  const [cprCvr, setCprCvr] = useState('');
  const [contactType, setContactType] = useState('private');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');

  // Step 3: Process
  const [type, setType] = useState('switch');
  const [effectiveDate, setEffectiveDate] = useState('');

  useEffect(() => {
    api.getProducts().then(setProducts).catch(() => {});
  }, []);

  async function handleSubmit() {
    setSubmitting(true);
    setError(null);
    try {
      const payload = {
        darId,
        gsrn: selectedGsrn,
        customerName,
        cprCvr,
        contactType,
        email,
        phone,
        productId: selectedProduct,
        type,
        effectiveDate,
      };
      if (correctedFromId) {
        payload.correctedFromId = correctedFromId;
      }
      await api.createSignup(payload);
      navigate('/signups');
    } catch (e) {
      setError(e.message);
      setSubmitting(false);
    }
  }

  const selectedProductObj = products.find((p) => p.id === selectedProduct);

  return (
    <div className="signup-grid-bg min-h-full">
      <style>{PAGE_STYLES}</style>

      <div className="px-6 py-10 max-w-4xl mx-auto">

        {/* Breadcrumb */}
        <Link to="/signups" className="group inline-flex items-center gap-2 text-sm text-slate-400 hover:text-teal-600 mb-8 transition-colors font-medium">
          <svg className="w-4 h-4 transition-transform group-hover:-translate-x-0.5" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
          </svg>
          {t('signupNew.backToSignups')}
        </Link>

        {/* Header */}
        <div className="mb-8 wizard-enter">
          <h1 className="text-4xl font-extrabold text-slate-900 tracking-tight">
            {correctedFromId ? t('signupNew.titleCorrected') : t('signupNew.title')}
          </h1>
          <p className="text-slate-400 text-lg mt-1">{STEPS[step]?.desc}</p>
        </div>

        {/* Correction banner */}
        {correctedFromId && correctedFromNumber && (
          <div className="mb-8 border-l-4 border-teal-500 bg-teal-50/60 rounded-r-lg px-5 py-4 flex items-center gap-3 wizard-enter">
            <svg className="w-5 h-5 text-teal-600 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 12c0-1.232-.046-2.453-.138-3.662a4.006 4.006 0 0 0-3.7-3.7 48.678 48.678 0 0 0-7.324 0 4.006 4.006 0 0 0-3.7 3.7c-.017.22-.032.441-.046.662M19.5 12l3-3m-3 3-3-3m-12 3c0 1.232.046 2.453.138 3.662a4.006 4.006 0 0 0 3.7 3.7 48.656 48.656 0 0 0 7.324 0 4.006 4.006 0 0 0 3.7-3.7c.017-.22.032-.441.046-.662M4.5 12l3 3m-3-3-3 3" />
            </svg>
            <p className="text-sm text-teal-800 font-semibold">
              {t('signupNew.correctionBanner', { number: correctedFromNumber })}
            </p>
          </div>
        )}

        {/* Step indicator — segmented progress bar */}
        <div className="flex rounded-xl overflow-hidden border border-slate-200 shadow-sm mb-8 wizard-enter" style={{ animationDelay: '80ms' }}>
          {STEPS.map((s, i) => (
            <div
              key={s.key}
              className={`flex-1 flex items-center justify-center gap-2.5 px-3 py-3.5 text-xs font-bold tracking-wide transition-all duration-300 ${
                i < step
                  ? 'bg-teal-600 text-white'
                  : i === step
                  ? 'bg-teal-500 text-white'
                  : 'bg-slate-50 text-slate-400'
              } ${i > 0 && i > step ? 'border-l border-slate-200' : ''}`}
            >
              <span className={`w-5 h-5 rounded-full flex items-center justify-center text-[10px] font-black shrink-0 ${
                i <= step ? 'bg-white/20 text-white' : 'bg-slate-200/80 text-slate-400'
              }`}>
                {i < step ? (
                  <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" strokeWidth={3} stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                  </svg>
                ) : (
                  i + 1
                )}
              </span>
              <span className="hidden sm:inline">{s.label}</span>
            </div>
          ))}
        </div>

        {/* Error banner */}
        {error && (
          <div className="mb-8 border-l-4 border-rose-500 bg-rose-50/60 rounded-r-lg px-5 py-4 flex items-start gap-3 wizard-enter">
            <svg className="w-5 h-5 text-rose-500 shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
            </svg>
            <p className="text-sm text-rose-700 font-medium">{error}</p>
          </div>
        )}

        {/* Main content card */}
        <div className="bg-white rounded-2xl shadow-sm border border-slate-100 relative wizard-enter" style={{ animationDelay: '160ms' }}>

          {/* Decorative watermark */}
          <div className="absolute inset-0 overflow-hidden rounded-2xl pointer-events-none">
            <div className="step-watermark absolute right-6 -top-4 select-none">
              {String(step + 1).padStart(2, '0')}
            </div>
          </div>

          <div className="relative p-8 sm:p-10">

            {/* ─── Step 0: Address ─── */}
            {step === 0 && (
              <div className="space-y-7">
                <div>
                  <h2 className="text-2xl font-bold text-slate-900 tracking-tight">{t('signupNew.addressLookup')}</h2>
                  <p className="text-slate-500 mt-1">{t('signupNew.addressLookupDesc')}</p>
                </div>

                {/* Mode toggle — pill buttons */}
                <div className="flex gap-2">
                  <button
                    onClick={() => { setAddressMode('search'); clearAddress(); }}
                    className={`px-4 py-2 text-xs font-bold rounded-lg border-2 transition-all duration-200 ${
                      addressMode === 'search'
                        ? 'bg-teal-600 text-white border-teal-600 shadow-sm'
                        : 'bg-white text-slate-500 border-slate-200 hover:border-slate-300 hover:text-slate-700'
                    }`}
                  >
                    {t('signupNew.searchAddress')}
                  </button>
                  <button
                    onClick={() => { setAddressMode('dar'); clearAddress(); }}
                    className={`px-4 py-2 text-xs font-bold rounded-lg border-2 transition-all duration-200 ${
                      addressMode === 'dar'
                        ? 'bg-teal-600 text-white border-teal-600 shadow-sm'
                        : 'bg-white text-slate-500 border-slate-200 hover:border-slate-300 hover:text-slate-700'
                    }`}
                  >
                    {t('signupNew.darId')}
                  </button>
                  <button
                    onClick={() => { setAddressMode('gsrn'); clearAddress(); }}
                    className={`px-4 py-2 text-xs font-bold rounded-lg border-2 transition-all duration-200 ${
                      addressMode === 'gsrn'
                        ? 'bg-teal-600 text-white border-teal-600 shadow-sm'
                        : 'bg-white text-slate-500 border-slate-200 hover:border-slate-300 hover:text-slate-700'
                    }`}
                  >
                    {t('signupNew.gsrn')}
                  </button>
                </div>

                {/* Search mode */}
                {addressMode === 'search' && (
                  <div>
                    <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.address')}</label>
                    <div className="relative" ref={suggestionsRef}>
                      {selectedAddress ? (
                        <div className="flex items-center gap-3 rounded-lg border-2 border-emerald-300 bg-emerald-50/50 px-4 py-3.5">
                          <svg className="w-5 h-5 text-emerald-500 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" d="M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
                            <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 10.5c0 7.142-7.5 11.25-7.5 11.25S4.5 17.642 4.5 10.5a7.5 7.5 0 0 1 15 0Z" />
                          </svg>
                          <span className="text-sm text-slate-800 flex-1 font-medium">{selectedAddress.text}</span>
                          <button onClick={clearAddress} className="p-1.5 text-slate-400 hover:text-rose-500 hover:bg-rose-50 rounded-md transition-all">
                            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
                            </svg>
                          </button>
                        </div>
                      ) : (
                        <>
                          <div className="relative">
                            <svg className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                              <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
                            </svg>
                            <input
                              type="text"
                              value={addressQuery}
                              onChange={(e) => { setAddressQuery(e.target.value); setShowSuggestions(true); }}
                              onFocus={() => setShowSuggestions(true)}
                              placeholder={t('signupNew.addressPlaceholder')}
                              className="w-full rounded-lg border border-slate-200 bg-white pl-11 pr-4 py-3.5 text-sm text-slate-800 placeholder:text-slate-300 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all"
                            />
                            {searchingAddress && (
                              <div className="absolute right-4 top-1/2 -translate-y-1/2 w-4 h-4 border-2 border-teal-200 border-t-teal-600 rounded-full animate-spin" />
                            )}
                          </div>
                          {showSuggestions && addressSuggestions.length > 0 && (
                            <div className="absolute z-10 mt-2 w-full bg-white rounded-lg shadow-xl border border-slate-200 overflow-hidden">
                              {addressSuggestions.map((s, i) => (
                                <button
                                  key={i}
                                  onClick={() => selectAddress(s)}
                                  className="w-full text-left px-4 py-3 text-sm text-slate-700 hover:bg-teal-50 hover:text-teal-700 flex items-center gap-3 transition-colors border-b border-slate-50 last:border-b-0"
                                >
                                  <svg className="w-4 h-4 text-slate-300 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" d="M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
                                    <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 10.5c0 7.142-7.5 11.25-7.5 11.25S4.5 17.642 4.5 10.5a7.5 7.5 0 0 1 15 0Z" />
                                  </svg>
                                  {s.text}
                                </button>
                              ))}
                            </div>
                          )}
                        </>
                      )}
                    </div>
                    {selectedAddress && (
                      <p className="text-xs text-slate-400 mt-2 font-mono">DAR: {selectedAddress.darId}</p>
                    )}
                  </div>
                )}

                {/* DAR ID mode */}
                {addressMode === 'dar' && (
                  <div>
                    <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.darId')}</label>
                    <div className="flex gap-3">
                      <input
                        type="text"
                        value={darId}
                        onChange={(e) => setDarId(e.target.value)}
                        placeholder={t('signupNew.darIdPlaceholder')}
                        className="flex-1 rounded-lg border border-slate-200 bg-white px-4 py-3.5 text-sm font-mono text-slate-800 placeholder:text-slate-300 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all"
                      />
                      <button
                        onClick={() => lookupGsrn(darId)}
                        disabled={!darId || lookingUp}
                        className="px-6 py-3.5 bg-slate-900 text-white text-sm font-bold rounded-lg hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition-all shadow-sm"
                      >
                        {lookingUp ? t('signupNew.searching') : t('signupNew.lookUp')}
                      </button>
                    </div>
                  </div>
                )}

                {/* GSRN mode */}
                {addressMode === 'gsrn' && (
                  <div>
                    <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.gsrn')}</label>
                    <div className="flex gap-3">
                      <input
                        type="text"
                        value={gsrnInput}
                        onChange={(e) => setGsrnInput(e.target.value)}
                        placeholder={t('signupNew.gsrnPlaceholder')}
                        className="flex-1 rounded-lg border border-slate-200 bg-white px-4 py-3.5 text-sm font-mono text-slate-800 placeholder:text-slate-300 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all"
                      />
                      <button
                        onClick={() => validateGsrn(gsrnInput)}
                        disabled={!gsrnInput || lookingUp}
                        className="px-6 py-3.5 bg-slate-900 text-white text-sm font-bold rounded-lg hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition-all shadow-sm"
                      >
                        {lookingUp ? t('signupNew.validating') : t('signupNew.validate')}
                      </button>
                    </div>
                    <p className="text-[11px] text-slate-400 mt-1.5 font-medium">{t('signupNew.gsrnHelper')}</p>
                  </div>
                )}

                {lookingUp && (
                  <div className="flex items-center gap-3 text-sm text-slate-500 py-2">
                    <div className="w-4 h-4 border-2 border-teal-200 border-t-teal-600 rounded-full animate-spin" />
                    {t('signupNew.lookingUp')}
                  </div>
                )}

                {meteringPoints && meteringPoints.length === 0 && (
                  <div className="border-l-4 border-amber-400 bg-amber-50/50 rounded-r-lg px-5 py-3.5 text-sm text-amber-800 font-medium">
                    {t('signupNew.noMeteringPoints')}
                  </div>
                )}

                {/* Metering points */}
                {meteringPoints && meteringPoints.length > 0 && (
                  <div>
                    <p className="text-[11px] font-black text-slate-400 uppercase tracking-widest mb-3">
                      {meteringPoints.length === 1
                        ? t('signupNew.meteringPointFound')
                        : t('signupNew.meteringPointsSelect', { count: meteringPoints.length })}
                    </p>

                    {/* All blocked banner */}
                    {meteringPoints.every((mp) => mp.has_active_process) && (
                      <div className="border-l-4 border-amber-400 bg-amber-50/50 rounded-r-lg px-5 py-3.5 text-sm text-amber-800 font-medium mb-3">
                        {t('signupNew.allMeteringPointsBlocked')}
                      </div>
                    )}

                    <div className="space-y-2">
                      {meteringPoints.map((mp) => {
                        const blocked = mp.has_active_process;
                        return (
                        <label
                          key={mp.gsrn}
                          className={`group flex items-center gap-4 rounded-lg px-5 py-4 transition-all duration-200 border-2 ${
                            blocked
                              ? 'border-slate-200 bg-slate-50/50 opacity-60 cursor-not-allowed'
                              : selectedGsrn === mp.gsrn
                              ? 'border-teal-500 bg-teal-50/40 shadow-sm cursor-pointer'
                              : 'border-slate-200 hover:border-slate-300 hover:bg-slate-50/50 cursor-pointer'
                          }`}
                        >
                          <input
                            type="radio"
                            name="gsrn"
                            checked={selectedGsrn === mp.gsrn}
                            onChange={() => !blocked && setSelectedGsrn(mp.gsrn)}
                            disabled={blocked}
                            className="accent-teal-600"
                          />
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-2">
                              <p className="font-mono text-base font-bold text-slate-900 tracking-wide">{mp.gsrn}</p>
                              {blocked && (
                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold bg-amber-100 text-amber-700 uppercase tracking-wider border border-amber-200">
                                  <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z" />
                                  </svg>
                                  {t('signupNew.activeProcess')}
                                </span>
                              )}
                            </div>
                            <div className="flex items-center gap-2 mt-1">
                              <span className="inline-flex px-2 py-0.5 rounded text-[10px] font-bold bg-slate-100 text-slate-500 uppercase tracking-wider">
                                {mp.type}
                              </span>
                              <span className="text-slate-300 text-xs">·</span>
                              <span className="text-xs text-slate-400 font-medium">
                                {t('signupNew.gridArea')} {mp.grid_area_code}
                              </span>
                            </div>
                          </div>
                          {!blocked && selectedGsrn === mp.gsrn && (
                            <svg className="w-5 h-5 text-teal-500 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                              <path fillRule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm3.857-9.809a.75.75 0 0 0-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 1 0-1.06 1.061l2.5 2.5a.75.75 0 0 0 1.137-.089l4-5.5Z" clipRule="evenodd" />
                            </svg>
                          )}
                        </label>
                        );
                      })}
                    </div>
                  </div>
                )}

                {/* Navigation */}
                <div className="flex justify-end pt-6 mt-2 border-t border-slate-100">
                  <button
                    onClick={() => { setError(null); setStep(1); }}
                    disabled={!selectedGsrn || (meteringPoints && meteringPoints.find((mp) => mp.gsrn === selectedGsrn)?.has_active_process)}
                    className="inline-flex items-center gap-2 px-6 py-3 bg-teal-600 text-white text-sm font-bold rounded-lg shadow-md shadow-teal-600/20 hover:bg-teal-700 hover:-translate-y-0.5 hover:shadow-lg disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:translate-y-0 disabled:hover:shadow-md transition-all duration-200"
                  >
                    {t('common.continue')}
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
                    </svg>
                  </button>
                </div>
              </div>
            )}

            {/* ─── Step 1: Product ─── */}
            {step === 1 && (
              <div className="space-y-7">
                <div>
                  <h2 className="text-2xl font-bold text-slate-900 tracking-tight">{t('signupNew.selectProduct')}</h2>
                  <p className="text-slate-500 mt-1">{t('signupNew.selectProductDesc')}</p>
                </div>

                <div className="space-y-3">
                  {products.map((p) => (
                    <label
                      key={p.id}
                      className={`group block rounded-xl cursor-pointer transition-all duration-200 overflow-hidden border-2 ${
                        selectedProduct === p.id
                          ? 'border-teal-500 shadow-md shadow-teal-500/10'
                          : 'border-slate-200 hover:border-slate-300 hover:shadow-sm'
                      }`}
                    >
                      <div className="flex">
                        {/* Accent bar */}
                        <div className={`w-1.5 shrink-0 transition-colors duration-200 ${
                          selectedProduct === p.id ? 'bg-teal-500' : 'bg-transparent group-hover:bg-slate-200'
                        }`} />

                        <div className={`flex-1 p-5 transition-colors ${
                          selectedProduct === p.id ? 'bg-teal-50/30' : 'bg-white'
                        }`}>
                          <input type="radio" name="product" checked={selectedProduct === p.id} onChange={() => setSelectedProduct(p.id)} className="hidden" />

                          {/* Header: name + badges + checkmark */}
                          <div className="flex items-start justify-between mb-3">
                            <div className="flex items-center gap-3 flex-wrap">
                              <span className="text-lg font-bold text-slate-900">{p.name}</span>
                              <span className="inline-flex px-2.5 py-0.5 rounded-md text-[10px] font-black bg-slate-100 text-slate-500 uppercase tracking-widest border border-slate-200">
                                {p.energyModel}
                              </span>
                              {p.green_energy && (
                                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md text-[10px] font-black bg-emerald-50 text-emerald-600 uppercase tracking-wider border border-emerald-200">
                                  <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" d="M9.813 15.904 9 18.75l-.813-2.846a4.5 4.5 0 0 0-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 0 0 3.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 0 0 3.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 0 0-3.09 3.09ZM18.259 8.715 18 9.75l-.259-1.035a3.375 3.375 0 0 0-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 0 0 2.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 0 0 2.455 2.456L21.75 6l-1.036.259a3.375 3.375 0 0 0-2.455 2.456Z" />
                                  </svg>
                                  {t('signupNew.green')}
                                </span>
                              )}
                            </div>
                            {selectedProduct === p.id && (
                              <svg className="w-6 h-6 text-teal-500 shrink-0 ml-3" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm3.857-9.809a.75.75 0 0 0-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 1 0-1.06 1.061l2.5 2.5a.75.75 0 0 0 1.137-.089l4-5.5Z" clipRule="evenodd" />
                              </svg>
                            )}
                          </div>

                          {/* Pricing */}
                          <div className="flex items-baseline gap-6 mb-2">
                            <div className="flex items-baseline gap-1.5">
                              <span className="text-xl font-black text-slate-900">{p.margin_ore_per_kwh}</span>
                              <span className="text-xs text-slate-400 font-medium">{t('signupNew.marginLabel')}</span>
                            </div>
                            <div className="w-px h-5 bg-slate-200 self-center" />
                            <div className="flex items-baseline gap-1.5">
                              <span className="text-xl font-black text-slate-900">{p.subscription_kr_per_month}</span>
                              <span className="text-xs text-slate-400 font-medium">{t('signupNew.subscriptionLabel')}</span>
                            </div>
                          </div>

                          {/* Description */}
                          {p.description && (
                            <p className="text-sm text-slate-400 leading-relaxed">{p.description}</p>
                          )}
                        </div>
                      </div>
                    </label>
                  ))}
                </div>

                {/* Navigation */}
                <div className="flex items-center justify-between pt-6 mt-2 border-t border-slate-100">
                  <button
                    onClick={() => setStep(0)}
                    className="inline-flex items-center gap-2 px-5 py-3 text-sm font-semibold text-slate-500 bg-white border-2 border-slate-200 rounded-lg hover:bg-slate-50 hover:text-slate-700 hover:border-slate-300 transition-all duration-200"
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M10.5 19.5 3 12m0 0 7.5-7.5M3 12h18" />
                    </svg>
                    {t('common.back')}
                  </button>
                  <button
                    onClick={() => { setError(null); setStep(2); }}
                    disabled={!selectedProduct}
                    className="inline-flex items-center gap-2 px-6 py-3 bg-teal-600 text-white text-sm font-bold rounded-lg shadow-md shadow-teal-600/20 hover:bg-teal-700 hover:-translate-y-0.5 hover:shadow-lg disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:translate-y-0 disabled:hover:shadow-md transition-all duration-200"
                  >
                    {t('common.continue')}
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
                    </svg>
                  </button>
                </div>
              </div>
            )}

            {/* ─── Step 2: Customer ─── */}
            {step === 2 && (
              <div className="space-y-8">
                <div>
                  <h2 className="text-2xl font-bold text-slate-900 tracking-tight">{t('signupNew.customerDetails')}</h2>
                  <p className="text-slate-500 mt-1">{t('signupNew.customerDetailsDesc')}</p>
                </div>

                {/* Personal Info section */}
                <div>
                  <div className="flex items-center gap-3 mb-5">
                    <div className="w-8 h-px bg-teal-500" />
                    <h3 className="text-[11px] font-black uppercase tracking-widest text-slate-400">{t('signupNew.personalInfo')}</h3>
                    <div className="flex-1 h-px bg-slate-100" />
                  </div>
                  <div className="space-y-4">
                    <div>
                      <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.fullName')}</label>
                      <input type="text" value={customerName} onChange={(e) => setCustomerName(e.target.value)}
                        className="w-full rounded-lg border border-slate-200 bg-white px-4 py-3.5 text-sm text-slate-800 placeholder:text-slate-300 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all" />
                    </div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                      <div>
                        <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.cprCvr')}</label>
                        <input type="text" value={cprCvr} onChange={(e) => setCprCvr(e.target.value)} placeholder="0101901234"
                          className="w-full rounded-lg border border-slate-200 bg-white px-4 py-3.5 text-sm text-slate-800 placeholder:text-slate-300 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all" />
                        <p className="text-[11px] text-slate-400 mt-1.5 font-medium">{t('signupNew.cprCvrHelper')}</p>
                      </div>
                      <div>
                        <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.contactType')}</label>
                        <select value={contactType} onChange={(e) => setContactType(e.target.value)}
                          className="w-full rounded-lg border border-slate-200 bg-white px-4 py-3.5 text-sm text-slate-800 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all">
                          <option value="private">{t('signupNew.contactPrivate')}</option>
                          <option value="business">{t('signupNew.contactBusiness')}</option>
                        </select>
                      </div>
                    </div>
                  </div>
                </div>

                {/* Contact Info section */}
                <div>
                  <div className="flex items-center gap-3 mb-5">
                    <div className="w-8 h-px bg-teal-500" />
                    <h3 className="text-[11px] font-black uppercase tracking-widest text-slate-400">{t('signupNew.contactInfo')}</h3>
                    <div className="flex-1 h-px bg-slate-100" />
                  </div>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.email')}</label>
                      <input type="email" value={email} onChange={(e) => setEmail(e.target.value)}
                        className="w-full rounded-lg border border-slate-200 bg-white px-4 py-3.5 text-sm text-slate-800 placeholder:text-slate-300 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all" />
                    </div>
                    <div>
                      <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.phone')}</label>
                      <input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+4512345678"
                        className="w-full rounded-lg border border-slate-200 bg-white px-4 py-3.5 text-sm text-slate-800 placeholder:text-slate-300 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all" />
                    </div>
                  </div>
                </div>

                {/* Navigation */}
                <div className="flex items-center justify-between pt-6 mt-2 border-t border-slate-100">
                  <button
                    onClick={() => setStep(1)}
                    className="inline-flex items-center gap-2 px-5 py-3 text-sm font-semibold text-slate-500 bg-white border-2 border-slate-200 rounded-lg hover:bg-slate-50 hover:text-slate-700 hover:border-slate-300 transition-all duration-200"
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M10.5 19.5 3 12m0 0 7.5-7.5M3 12h18" />
                    </svg>
                    {t('common.back')}
                  </button>
                  <button
                    onClick={() => { setError(null); setStep(3); }}
                    disabled={!customerName || !cprCvr || !email}
                    className="inline-flex items-center gap-2 px-6 py-3 bg-teal-600 text-white text-sm font-bold rounded-lg shadow-md shadow-teal-600/20 hover:bg-teal-700 hover:-translate-y-0.5 hover:shadow-lg disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:translate-y-0 disabled:hover:shadow-md transition-all duration-200"
                  >
                    {t('common.continue')}
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
                    </svg>
                  </button>
                </div>
              </div>
            )}

            {/* ─── Step 3: Confirm ─── */}
            {step === 3 && (
              <div className="space-y-7">
                <div>
                  <h2 className="text-2xl font-bold text-slate-900 tracking-tight">{t('signupNew.reviewSubmit')}</h2>
                  <p className="text-slate-500 mt-1">{t('signupNew.reviewSubmitDesc')}</p>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.processType')}</label>
                    <select value={type} onChange={(e) => setType(e.target.value)}
                      className="w-full rounded-lg border border-slate-200 bg-white px-4 py-3.5 text-sm text-slate-800 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all">
                      <option value="switch">{t('signupNew.supplierSwitch')}</option>
                      <option value="move_in">{t('signupNew.moveIn')}</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-semibold text-slate-700 mb-2">{t('signupNew.effectiveDate')}</label>
                    <input type="date" value={effectiveDate} onChange={(e) => setEffectiveDate(e.target.value)}
                      className="w-full rounded-lg border border-slate-200 bg-white px-4 py-3.5 text-sm text-slate-800 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all" />
                  </div>
                </div>

                {/* Summary — receipt style with dark header */}
                <div className="rounded-xl border border-slate-200 overflow-hidden">
                  <div className="h-1 bg-gradient-to-r from-teal-400 to-teal-600" />
                  <div className="px-6 py-3.5 bg-slate-800">
                    <h3 className="text-[11px] font-black uppercase tracking-widest text-slate-300">{t('signupNew.summary')}</h3>
                  </div>
                  <div className="divide-y divide-slate-100 bg-white">
                    {correctedFromNumber && (
                      <SummaryRow label={t('signupNew.summaryCorrects')} value={
                        <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-bold bg-rose-50 text-rose-600 border border-rose-200">
                          {correctedFromNumber}
                        </span>
                      } />
                    )}
                    {selectedAddress && <SummaryRow label={t('signupNew.summaryAddress')} value={selectedAddress.text} onEdit={() => setStep(0)} />}
                    <SummaryRow label={t('signupNew.summaryGsrn')} value={<span className="font-mono text-xs font-bold bg-slate-100 px-2.5 py-1 rounded">{selectedGsrn}</span>} onEdit={() => setStep(0)} />
                    <SummaryRow label={t('signupNew.summaryProduct')} value={selectedProductObj?.name} onEdit={() => setStep(1)} />
                    <SummaryRow label={t('signupNew.summaryCustomer')} value={`${customerName} (${cprCvr})`} onEdit={() => setStep(2)} />
                    <SummaryRow label={t('signupNew.summaryContact')} value={`${contactType} · ${email} · ${phone || '—'}`} onEdit={() => setStep(2)} />
                    <SummaryRow label={t('signupNew.summaryType')} value={type === 'move_in' ? t('signupNew.moveIn') : t('signupNew.supplierSwitch')} />
                    <SummaryRow label={t('signupNew.summaryEffective')} value={effectiveDate || t('signupNew.notSet')} highlight={!effectiveDate} />
                  </div>
                </div>

                {/* Navigation */}
                <div className="flex items-center justify-between pt-6 mt-2 border-t border-slate-100">
                  <button
                    onClick={() => setStep(2)}
                    className="inline-flex items-center gap-2 px-5 py-3 text-sm font-semibold text-slate-500 bg-white border-2 border-slate-200 rounded-lg hover:bg-slate-50 hover:text-slate-700 hover:border-slate-300 transition-all duration-200"
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M10.5 19.5 3 12m0 0 7.5-7.5M3 12h18" />
                    </svg>
                    {t('common.back')}
                  </button>
                  <button
                    onClick={handleSubmit}
                    disabled={!effectiveDate || submitting}
                    className="inline-flex items-center gap-2.5 px-8 py-3.5 bg-teal-600 text-white text-sm font-bold rounded-lg shadow-lg shadow-teal-600/25 hover:bg-teal-700 hover:-translate-y-0.5 hover:shadow-xl disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:translate-y-0 disabled:hover:shadow-lg transition-all duration-200"
                  >
                    {submitting ? (
                      <>
                        <span className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                        {t('signupNew.creating')}
                      </>
                    ) : (
                      <>
                        {correctedFromId ? t('signupNew.createCorrectedSignup') : t('signupNew.createSignup')}
                        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                        </svg>
                      </>
                    )}
                  </button>
                </div>
              </div>
            )}

          </div>
        </div>
      </div>
    </div>
  );
}

function SummaryRow({ label, value, highlight, onEdit }) {
  return (
    <div className="flex items-center px-6 py-3.5 group">
      <span className="w-36 text-[11px] font-bold text-slate-400 uppercase tracking-widest shrink-0">{label}</span>
      <span className={`text-sm flex-1 min-w-0 ${highlight ? 'text-amber-600 font-semibold italic' : 'text-slate-800'}`}>{value}</span>
      {onEdit && (
        <button
          onClick={onEdit}
          className="opacity-0 group-hover:opacity-100 text-[11px] font-bold text-teal-600 hover:text-teal-800 transition-all duration-200 ml-4 shrink-0 uppercase tracking-widest"
        >
          Edit
        </button>
      )}
    </div>
  );
}
