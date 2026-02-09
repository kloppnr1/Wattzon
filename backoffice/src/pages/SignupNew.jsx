import { useState, useEffect, useRef } from 'react';
import { useNavigate, Link, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useDawaSearch } from '../hooks/useDawaSearch';

const STEPS = [
  { key: 'address', label: 'Address', desc: 'Look up metering point' },
  { key: 'product', label: 'Product', desc: 'Choose energy product' },
  { key: 'customer', label: 'Customer', desc: 'Customer details' },
  { key: 'confirm', label: 'Confirm', desc: 'Review and submit' },
];

export default function SignupNew() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [step, setStep] = useState(0);
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
      if (result.length === 1) setSelectedGsrn(result[0].gsrn);
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
    <div className="p-8 max-w-2xl mx-auto">
      {/* Breadcrumb */}
      <Link to="/signups" className="inline-flex items-center gap-1.5 text-sm text-slate-400 hover:text-teal-600 mb-6 transition-colors font-medium">
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
        </svg>
        Back to signups
      </Link>

      {/* Page header */}
      <h1 className="text-3xl font-bold text-slate-900 tracking-tight mb-2 animate-fade-in-up">
        {correctedFromId ? 'Corrected Signup' : 'New Signup'}
      </h1>

      {/* Correction banner */}
      {correctedFromId && correctedFromNumber && (
        <div className="mb-6 bg-teal-50 border border-teal-200 rounded-xl px-4 py-3 flex items-center gap-2.5 animate-fade-in-up">
          <svg className="w-4 h-4 text-teal-500 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 12c0-1.232-.046-2.453-.138-3.662a4.006 4.006 0 0 0-3.7-3.7 48.678 48.678 0 0 0-7.324 0 4.006 4.006 0 0 0-3.7 3.7c-.017.22-.032.441-.046.662M19.5 12l3-3m-3 3-3-3m-12 3c0 1.232.046 2.453.138 3.662a4.006 4.006 0 0 0 3.7 3.7 48.656 48.656 0 0 0 7.324 0 4.006 4.006 0 0 0 3.7-3.7c.017-.22.032-.441.046-.662M4.5 12l3 3m-3-3-3 3" />
          </svg>
          <p className="text-sm text-teal-700 font-medium">
            Correcting rejected signup <span className="font-bold">{correctedFromNumber}</span> — review and adjust the data below.
          </p>
        </div>
      )}

      {/* Step indicator */}
      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 px-6 py-5 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="flex items-start">
          {STEPS.map((s, i) => (
            <div key={s.key} className={`flex flex-col items-center text-center ${i < STEPS.length - 1 ? 'flex-1' : ''}`}>
              {/* Circle row with connector */}
              <div className="flex items-center w-full">
                {i > 0 && <div className="flex-1" />}
                <div
                  className={`w-9 h-9 rounded-full flex items-center justify-center text-xs font-bold shrink-0 transition-all duration-300 ${
                    i < step
                      ? 'bg-emerald-500 text-white shadow-md shadow-emerald-500/25'
                      : i === step
                      ? 'bg-teal-500 text-white shadow-lg shadow-teal-500/30'
                      : 'bg-slate-100 text-slate-400'
                  }`}
                >
                  {i < step ? (
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                    </svg>
                  ) : (
                    i + 1
                  )}
                </div>
                {i < STEPS.length - 1 && (
                  <div className={`flex-1 h-0.5 mx-2 rounded-full transition-colors duration-300 ${i < step ? 'bg-teal-400' : 'bg-slate-100'}`} />
                )}
              </div>
              {/* Label below circle */}
              <p className={`text-xs font-semibold mt-2 ${i <= step ? 'text-slate-900' : 'text-slate-400'}`}>
                {s.label}
              </p>
              <p className="text-[11px] text-slate-400 hidden sm:block">{s.desc}</p>
            </div>
          ))}
        </div>
      </div>

      {error && (
        <div className="mb-6 bg-rose-50 border border-rose-200 rounded-xl px-4 py-3 flex items-start gap-3 animate-fade-in-up">
          <svg className="w-5 h-5 text-rose-500 shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
          </svg>
          <p className="text-sm text-rose-600">{error}</p>
        </div>
      )}

      {/* Step content card */}
      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 p-6 animate-fade-in-up" style={{ animationDelay: '120ms' }}>

        {/* Step 0: Address */}
        {step === 0 && (
          <div className="space-y-5">
            <div>
              <h3 className="text-lg font-semibold text-slate-900 mb-1">Address Lookup</h3>
              <p className="text-sm text-slate-500">Search for an address or enter a DAR ID directly.</p>
            </div>

            <div className="inline-flex bg-slate-100 rounded-xl p-1">
              <button
                onClick={() => { setAddressMode('search'); clearAddress(); }}
                className={`px-4 py-2 text-xs font-semibold rounded-lg transition-all duration-200 ${
                  addressMode === 'search' ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500 hover:text-slate-700'
                }`}
              >
                Search address
              </button>
              <button
                onClick={() => { setAddressMode('dar'); clearAddress(); }}
                className={`px-4 py-2 text-xs font-semibold rounded-lg transition-all duration-200 ${
                  addressMode === 'dar' ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500 hover:text-slate-700'
                }`}
              >
                DAR ID
              </button>
            </div>

            {addressMode === 'search' && (
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">Address</label>
                <div className="relative" ref={suggestionsRef}>
                  {selectedAddress ? (
                    <div className="flex items-center gap-2 rounded-xl border-2 border-emerald-200 bg-emerald-50 px-4 py-3">
                      <svg className="w-5 h-5 text-emerald-500 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
                        <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 10.5c0 7.142-7.5 11.25-7.5 11.25S4.5 17.642 4.5 10.5a7.5 7.5 0 0 1 15 0Z" />
                      </svg>
                      <span className="text-sm text-slate-700 flex-1 font-medium">{selectedAddress.text}</span>
                      <button onClick={clearAddress} className="text-slate-400 hover:text-rose-500 transition-colors">
                        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
                        </svg>
                      </button>
                    </div>
                  ) : (
                    <>
                      <div className="relative">
                        <svg className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
                        </svg>
                        <input
                          type="text"
                          value={addressQuery}
                          onChange={(e) => { setAddressQuery(e.target.value); setShowSuggestions(true); }}
                          onFocus={() => setShowSuggestions(true)}
                          placeholder="Start typing an address, e.g. Vestergade 10, 8000 Aarhus"
                          className="w-full rounded-xl border-2 border-slate-200 bg-white pl-10 pr-4 py-3 text-sm text-slate-900 placeholder:text-slate-400 focus:outline-none focus:border-teal-400 focus:ring-4 focus:ring-teal-500/10 transition-all"
                        />
                        {searchingAddress && (
                          <div className="absolute right-3.5 top-1/2 -translate-y-1/2 w-4 h-4 border-2 border-teal-100 border-t-teal-500 rounded-full animate-spin" />
                        )}
                      </div>
                      {showSuggestions && addressSuggestions.length > 0 && (
                        <div className="absolute z-10 mt-1.5 w-full bg-white rounded-xl shadow-xl border border-slate-200 overflow-hidden">
                          {addressSuggestions.map((s, i) => (
                            <button
                              key={i}
                              onClick={() => selectAddress(s)}
                              className="w-full text-left px-4 py-3 text-sm text-slate-700 hover:bg-teal-50 hover:text-teal-700 flex items-center gap-2.5 transition-colors"
                            >
                              <svg className="w-4 h-4 text-slate-400 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
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
                  <p className="text-xs text-slate-400 mt-1.5 font-medium">DAR ID: {selectedAddress.darId}</p>
                )}
              </div>
            )}

            {addressMode === 'dar' && (
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">DAR ID</label>
                <div className="flex gap-2">
                  <input
                    type="text"
                    value={darId}
                    onChange={(e) => setDarId(e.target.value)}
                    placeholder="0a3f50a0-75eb-32b8-e044-0003ba298018"
                    className="flex-1 rounded-xl border-2 border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 placeholder:text-slate-400 focus:outline-none focus:border-teal-400 focus:ring-4 focus:ring-teal-500/10 transition-all"
                  />
                  <button
                    onClick={() => lookupGsrn(darId)}
                    disabled={!darId || lookingUp}
                    className="px-5 py-3 bg-slate-900 text-white text-sm font-semibold rounded-xl hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition-colors shadow-sm"
                  >
                    {lookingUp ? 'Searching...' : 'Look up'}
                  </button>
                </div>
              </div>
            )}

            {lookingUp && (
              <div className="flex items-center gap-2 text-sm text-slate-500">
                <div className="w-4 h-4 border-2 border-teal-100 border-t-teal-500 rounded-full animate-spin" />
                Looking up metering points...
              </div>
            )}

            {meteringPoints && meteringPoints.length === 0 && (
              <div className="bg-teal-50 border border-teal-200 rounded-xl px-4 py-3 text-sm text-teal-700">
                No metering points found for this address.
              </div>
            )}

            {meteringPoints && meteringPoints.length > 0 && (
              <div>
                <p className="text-sm font-semibold text-slate-700 mb-2">
                  {meteringPoints.length === 1
                    ? 'Metering point found:'
                    : `${meteringPoints.length} metering points — select one:`}
                </p>
                <div className="space-y-2">
                  {meteringPoints.map((mp) => (
                    <label
                      key={mp.gsrn}
                      className={`flex items-center gap-3 border-2 rounded-xl px-4 py-3.5 cursor-pointer transition-all duration-200 ${
                        selectedGsrn === mp.gsrn
                          ? 'border-teal-400 bg-teal-50 shadow-sm shadow-teal-500/10'
                          : 'border-slate-200 hover:border-slate-300 hover:bg-slate-50'
                      }`}
                    >
                      <input
                        type="radio"
                        name="gsrn"
                        checked={selectedGsrn === mp.gsrn}
                        onChange={() => setSelectedGsrn(mp.gsrn)}
                        className="accent-teal-500"
                      />
                      <div>
                        <span className="font-mono text-sm text-slate-900 font-medium">{mp.gsrn}</span>
                        <span className="ml-3 text-xs text-slate-500">{mp.type} / Grid area {mp.grid_area_code}</span>
                      </div>
                    </label>
                  ))}
                </div>
              </div>
            )}

            <div className="flex justify-end pt-2">
              <button
                onClick={() => { setError(null); setStep(1); }}
                disabled={!selectedGsrn}
                className="px-6 py-2.5 bg-teal-500 text-white text-sm font-semibold rounded-xl shadow-lg shadow-teal-500/25 hover:shadow-xl hover:shadow-teal-500/30 hover:-translate-y-0.5 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:transform-none disabled:hover:shadow-lg transition-all duration-200"
              >
                Continue
              </button>
            </div>
          </div>
        )}

        {/* Step 1: Product */}
        {step === 1 && (
          <div className="space-y-5">
            <div>
              <h3 className="text-lg font-semibold text-slate-900 mb-1">Select Product</h3>
              <p className="text-sm text-slate-500">Choose the energy product for this customer.</p>
            </div>
            <div className="space-y-2">
              {products.map((p) => (
                <label
                  key={p.id}
                  className={`block border-2 rounded-xl px-4 py-4 cursor-pointer transition-all duration-200 ${
                    selectedProduct === p.id
                      ? 'border-teal-400 bg-teal-50 shadow-sm shadow-teal-500/10'
                      : 'border-slate-200 hover:border-slate-300 hover:bg-slate-50'
                  }`}
                >
                  <div className="flex items-start gap-3">
                    <input
                      type="radio"
                      name="product"
                      checked={selectedProduct === p.id}
                      onChange={() => setSelectedProduct(p.id)}
                      className="accent-teal-500 mt-0.5"
                    />
                    <div className="flex-1">
                      <div className="flex items-center gap-2">
                        <span className="text-sm font-semibold text-slate-900">{p.name}</span>
                        {p.green_energy && (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold bg-emerald-50 text-emerald-600 border border-emerald-200">
                            Green
                          </span>
                        )}
                      </div>
                      <div className="flex gap-4 mt-1.5 text-xs text-slate-500 font-medium">
                        <span className="px-2 py-0.5 bg-slate-100 rounded-md">{p.energyModel}</span>
                        <span>{p.margin_ore_per_kwh} ore/kWh margin</span>
                        <span>{p.subscription_kr_per_month} kr/mo</span>
                      </div>
                      {p.description && <p className="text-xs text-slate-400 mt-1.5">{p.description}</p>}
                    </div>
                  </div>
                </label>
              ))}
            </div>
            <div className="flex justify-between pt-2">
              <button onClick={() => setStep(0)} className="px-4 py-2 text-sm font-medium text-slate-400 hover:text-slate-700 transition-colors">Back</button>
              <button
                onClick={() => { setError(null); setStep(2); }}
                disabled={!selectedProduct}
                className="px-6 py-2.5 bg-teal-500 text-white text-sm font-semibold rounded-xl shadow-lg shadow-teal-500/25 hover:shadow-xl hover:-translate-y-0.5 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:transform-none transition-all duration-200"
              >
                Continue
              </button>
            </div>
          </div>
        )}

        {/* Step 2: Customer */}
        {step === 2 && (
          <div className="space-y-5">
            <div>
              <h3 className="text-lg font-semibold text-slate-900 mb-1">Customer Details</h3>
              <p className="text-sm text-slate-500">Enter the customer information.</p>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="col-span-2">
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">Full name</label>
                <input type="text" value={customerName} onChange={(e) => setCustomerName(e.target.value)}
                  className="w-full rounded-xl border-2 border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 placeholder:text-slate-400 focus:outline-none focus:border-teal-400 focus:ring-4 focus:ring-teal-500/10 transition-all" />
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">CPR/CVR</label>
                <input type="text" value={cprCvr} onChange={(e) => setCprCvr(e.target.value)} placeholder="0101901234"
                  className="w-full rounded-xl border-2 border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 placeholder:text-slate-400 focus:outline-none focus:border-teal-400 focus:ring-4 focus:ring-teal-500/10 transition-all" />
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">Contact type</label>
                <select value={contactType} onChange={(e) => setContactType(e.target.value)}
                  className="w-full rounded-xl border-2 border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 focus:outline-none focus:border-teal-400 focus:ring-4 focus:ring-teal-500/10 transition-all">
                  <option value="private">Private</option>
                  <option value="business">Business</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">Email</label>
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)}
                  className="w-full rounded-xl border-2 border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 placeholder:text-slate-400 focus:outline-none focus:border-teal-400 focus:ring-4 focus:ring-teal-500/10 transition-all" />
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">Phone</label>
                <input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+4512345678"
                  className="w-full rounded-xl border-2 border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 placeholder:text-slate-400 focus:outline-none focus:border-teal-400 focus:ring-4 focus:ring-teal-500/10 transition-all" />
              </div>
            </div>
            <div className="flex justify-between pt-2">
              <button onClick={() => setStep(1)} className="px-4 py-2 text-sm font-medium text-slate-400 hover:text-slate-700 transition-colors">Back</button>
              <button
                onClick={() => { setError(null); setStep(3); }}
                disabled={!customerName || !cprCvr || !email}
                className="px-6 py-2.5 bg-teal-500 text-white text-sm font-semibold rounded-xl shadow-lg shadow-teal-500/25 hover:shadow-xl hover:-translate-y-0.5 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:transform-none transition-all duration-200"
              >
                Continue
              </button>
            </div>
          </div>
        )}

        {/* Step 3: Confirm */}
        {step === 3 && (
          <div className="space-y-5">
            <div>
              <h3 className="text-lg font-semibold text-slate-900 mb-1">Review & Submit</h3>
              <p className="text-sm text-slate-500">Choose the process type and confirm all details.</p>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">Process type</label>
                <select value={type} onChange={(e) => setType(e.target.value)}
                  className="w-full rounded-xl border-2 border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 focus:outline-none focus:border-teal-400 focus:ring-4 focus:ring-teal-500/10 transition-all">
                  <option value="switch">Supplier switch (BRS-001)</option>
                  <option value="move_in">Move-in (BRS-009)</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">Effective date</label>
                <input type="date" value={effectiveDate} onChange={(e) => setEffectiveDate(e.target.value)}
                  className="w-full rounded-xl border-2 border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 focus:outline-none focus:border-teal-400 focus:ring-4 focus:ring-teal-500/10 transition-all" />
              </div>
            </div>

            <div className="bg-slate-50 border border-slate-200 rounded-xl overflow-hidden">
              <div className="px-4 py-3 border-b border-slate-200 bg-white">
                <div className="flex items-center gap-2">
                  <div className="w-1 h-4 rounded-full bg-teal-500" />
                  <p className="text-xs font-bold uppercase tracking-wider text-slate-500">Summary</p>
                </div>
              </div>
              <div className="divide-y divide-slate-200">
                {correctedFromNumber && (
                  <SummaryRow label="Corrects" value={
                    <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-xs font-semibold bg-rose-50 text-rose-600 border border-rose-200">
                      {correctedFromNumber}
                    </span>
                  } />
                )}
                {selectedAddress && <SummaryRow label="Address" value={selectedAddress.text} />}
                <SummaryRow label="GSRN" value={<span className="font-mono bg-slate-200/50 px-2 py-0.5 rounded-md text-xs">{selectedGsrn}</span>} />
                <SummaryRow label="Product" value={selectedProductObj?.name} />
                <SummaryRow label="Customer" value={`${customerName} (${cprCvr})`} />
                <SummaryRow label="Contact" value={`${contactType} · ${email} · ${phone || '—'}`} />
                <SummaryRow label="Type" value={type === 'move_in' ? 'Move-in (BRS-009)' : 'Supplier switch (BRS-001)'} />
                <SummaryRow label="Effective" value={effectiveDate || 'Not set'} highlight={!effectiveDate} />
              </div>
            </div>

            <div className="flex justify-between pt-2">
              <button onClick={() => setStep(2)} className="px-4 py-2 text-sm font-medium text-slate-400 hover:text-slate-700 transition-colors">Back</button>
              <button
                onClick={handleSubmit}
                disabled={!effectiveDate || submitting}
                className="px-7 py-2.5 bg-teal-500 text-white text-sm font-bold rounded-xl shadow-lg shadow-teal-500/25 hover:shadow-xl hover:-translate-y-0.5 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:transform-none transition-all duration-200"
              >
                {submitting ? (
                  <span className="flex items-center gap-2">
                    <span className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                    Creating...
                  </span>
                ) : correctedFromId ? (
                  'Create Corrected Signup'
                ) : (
                  'Create Signup'
                )}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function SummaryRow({ label, value, highlight }) {
  return (
    <div className="flex px-4 py-3">
      <span className="w-24 text-xs font-bold text-slate-400 uppercase shrink-0">{label}</span>
      <span className={`text-sm ${highlight ? 'text-teal-600 italic font-medium' : 'text-slate-700'}`}>{value}</span>
    </div>
  );
}
