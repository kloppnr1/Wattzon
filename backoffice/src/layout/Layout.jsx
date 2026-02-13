import { useState, useEffect } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useTranslation } from '../i18n/LanguageContext';

const navSections = [
  {
    items: [
      {
        to: '/',
        labelKey: 'nav.dashboard',
        end: true,
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 0 1 6 3.75h2.25A2.25 2.25 0 0 1 10.5 6v2.25a2.25 2.25 0 0 1-2.25 2.25H6a2.25 2.25 0 0 1-2.25-2.25V6ZM3.75 15.75A2.25 2.25 0 0 1 6 13.5h2.25a2.25 2.25 0 0 1 2.25 2.25V18a2.25 2.25 0 0 1-2.25 2.25H6A2.25 2.25 0 0 1 3.75 18v-2.25ZM13.5 6a2.25 2.25 0 0 1 2.25-2.25H18A2.25 2.25 0 0 1 20.25 6v2.25A2.25 2.25 0 0 1 18 10.5h-2.25a2.25 2.25 0 0 1-2.25-2.25V6ZM13.5 15.75a2.25 2.25 0 0 1 2.25-2.25H18a2.25 2.25 0 0 1 2.25 2.25V18A2.25 2.25 0 0 1 18 20.25h-2.25a2.25 2.25 0 0 1-2.25-2.25v-2.25Z" />
          </svg>
        ),
      },
    ],
  },
  {
    labelKey: 'nav.customers',
    items: [
      {
        to: '/signups',
        labelKey: 'nav.signups',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" />
          </svg>
        ),
      },
      {
        to: '/customers',
        labelKey: 'nav.customers',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z" />
          </svg>
        ),
      },
      {
        to: '/products',
        labelKey: 'nav.products',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z" />
          </svg>
        ),
      },
    ],
  },
  {
    labelKey: 'nav.billing',
    items: [
      {
        to: '/billing',
        labelKey: 'nav.settlement',
        end: true,
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25M9 16.5v.75m3-3v3M15 12v5.25m-4.5-15H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" />
          </svg>
        ),
      },
      {
        to: '/invoices',
        labelKey: 'nav.invoices',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" />
          </svg>
        ),
      },
      {
        to: '/payments',
        labelKey: 'nav.payments',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 18.75a60.07 60.07 0 0 1 15.797 2.101c.727.198 1.453-.342 1.453-1.096V18.75M3.75 4.5v.75A.75.75 0 0 1 3 6h-.75m0 0v-.375c0-.621.504-1.125 1.125-1.125H20.25M2.25 6v9m18-10.5v.75c0 .414.336.75.75.75h.75m-1.5-1.5h.375c.621 0 1.125.504 1.125 1.125v9.75c0 .621-.504 1.125-1.125 1.125h-.375m1.5-1.5H21a.75.75 0 0 0-.75.75v.75m0 0H3.75m0 0h-.375a1.125 1.125 0 0 1-1.125-1.125V15m1.5 1.5v-.75A.75.75 0 0 0 3 15h-.75M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Zm3 0h.008v.008H18V10.5Zm-12 0h.008v.008H6V10.5Z" />
          </svg>
        ),
      },
      {
        to: '/spot-prices',
        labelKey: 'nav.spotPrices',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M3 13.125C3 12.504 3.504 12 4.125 12h2.25c.621 0 1.125.504 1.125 1.125v6.75C7.5 20.496 6.996 21 6.375 21h-2.25A1.125 1.125 0 0 1 3 19.875v-6.75ZM9.75 8.625c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125v11.25c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 0 1-1.125-1.125V8.625ZM16.5 4.125c0-.621.504-1.125 1.125-1.125h2.25C20.496 3 21 3.504 21 4.125v15.75c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 0 1-1.125-1.125V4.125Z" />
          </svg>
        ),
      },
    ],
  },
  {
    labelKey: 'nav.datahub',
    items: [
      {
        to: '/datahub/processes',
        labelKey: 'nav.processes',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.325.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 0 1 1.37.49l1.296 2.247a1.125 1.125 0 0 1-.26 1.431l-1.003.827c-.293.241-.438.613-.43.992a7.723 7.723 0 0 1 0 .255c-.008.378.137.75.43.991l1.004.827c.424.35.534.955.26 1.43l-1.298 2.247a1.125 1.125 0 0 1-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.47 6.47 0 0 1-.22.128c-.331.183-.581.495-.644.869l-.213 1.281c-.09.543-.56.94-1.11.94h-2.594c-.55 0-1.019-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 0 1-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 0 0 1-1.369-.49l-1.297-2.247a1.125 1.125 0 0 1 .26-1.431l1.004-.827c.292-.24.437-.613.43-.991a6.932 6.932 0 0 1 0-.255c.007-.38-.138-.751-.43-.992l-1.004-.827a1.125 1.125 0 0 1-.26-1.43l1.297-2.247a1.125 1.125 0 0 1 1.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.086.22-.128.332-.183.582-.495.644-.869l.214-1.28Z" />
            <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
          </svg>
        ),
      },
      {
        to: '/datahub/messages',
        labelKey: 'nav.messages',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M21.75 6.75v10.5a2.25 2.25 0 0 1-2.25 2.25h-15a2.25 2.25 0 0 1-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0 0 19.5 4.5h-15a2.25 2.25 0 0 0-2.25 2.25m19.5 0v.243a2.25 2.25 0 0 1-1.07 1.916l-7.5 4.615a2.25 2.25 0 0 1-2.36 0L3.32 8.91a2.25 2.25 0 0 1-1.07-1.916V6.75" />
          </svg>
        ),
      },
    ],
  },
];

export default function Layout() {
  const { lang, setLang, t } = useTranslation();
  const location = useLocation();
  const [sidebarOpen, setSidebarOpen] = useState(false);

  // Auto-close sidebar on navigation
  useEffect(() => {
    setSidebarOpen(false);
  }, [location.pathname]);

  return (
    <div className="flex h-screen bg-slate-50">
      {/* Mobile top bar */}
      <div className="fixed top-0 left-0 right-0 z-40 flex items-center gap-3 px-4 h-14 md:hidden shadow-lg" style={{ background: 'linear-gradient(135deg, #3d5a6e 0%, #4a6b80 100%)' }}>
        <button
          onClick={() => setSidebarOpen(true)}
          className="p-1.5 rounded-lg text-white/80 hover:text-white hover:bg-white/10 transition-colors"
        >
          <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5" />
          </svg>
        </button>
        <div className="w-8 h-8 rounded-[8px] bg-white/95 flex items-center justify-center relative overflow-hidden">
          <span className="text-[14px] leading-none tracking-tight" style={{ fontFamily: "'Inter', sans-serif", fontWeight: 800, color: '#3d5a6e' }}>Wz</span>
          <span className="absolute bottom-0 left-[15%] right-[15%] h-[2px] rounded-t-sm" style={{ background: '#e67e22' }} />
        </div>
        <div>
          <p className="text-[13px] font-semibold text-white/90 leading-tight" style={{ fontFamily: "'DM Sans', sans-serif" }}>Wattzon</p>
          <p className="text-[9px] text-white/40 font-medium tracking-wide">Energy Platform</p>
        </div>
      </div>

      {/* Dark overlay on mobile */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/40 md:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      {/* Sidebar */}
      <aside className={`
        fixed inset-y-0 left-0 z-50 w-[240px] flex flex-col shrink-0 shadow-2xl overflow-hidden
        transition-transform duration-300 ease-in-out
        ${sidebarOpen ? 'translate-x-0' : '-translate-x-full'}
        md:relative md:translate-x-0
      `} style={{ background: 'linear-gradient(180deg, #3d5a6e 0%, #4a6b80 50%, #5a7d92 100%)', boxShadow: '4px 0 24px rgba(61, 90, 110, 0.15)' }}>
        {/* Decorative orbs */}
        <div className="absolute top-0 right-0 w-32 h-32 bg-white/5 rounded-full -translate-y-1/2 translate-x-1/2 blur-2xl" />
        <div className="absolute bottom-20 left-0 w-24 h-24 rounded-full -translate-x-1/2 blur-xl" style={{ background: 'rgba(90, 125, 146, 0.15)' }} />

        {/* Brand */}
        <div className="px-5 pt-7 pb-6 relative z-10">
          <div className="flex items-center gap-3">
            <div className="w-[42px] h-[42px] rounded-[10px] bg-white/95 flex items-center justify-center relative overflow-hidden flex-shrink-0">
              <span className="leading-none tracking-tight" style={{ fontFamily: "'Inter', sans-serif", fontWeight: 800, fontSize: '18px', color: '#3d5a6e' }}>Wz</span>
              <span className="absolute bottom-0 left-[15%] right-[15%] h-[3px] rounded-t-sm" style={{ background: '#e67e22' }} />
            </div>
            <div>
              <p className="text-[14px] font-semibold text-white/95 leading-tight" style={{ fontFamily: "'DM Sans', sans-serif" }}>Wattzon</p>
              <p className="text-[11px] text-white/40 mt-0.5" style={{ fontFamily: "'DM Sans', sans-serif" }}>Energy Platform</p>
            </div>
          </div>
        </div>

        {/* Navigation */}
        <nav className="flex-1 px-3 pb-4 space-y-5 overflow-y-auto relative z-10">
          {navSections.map((section, idx) => (
            <div key={section.labelKey || idx}>
              {section.labelKey && (
                <p className="px-3 mb-1.5 text-[10px] font-semibold uppercase tracking-[0.12em] text-white/35">
                  {t(section.labelKey)}
                </p>
              )}
              <div className="space-y-0.5">
                {section.items.map(({ to, labelKey, icon, end }) => (
                  <NavLink
                    key={to}
                    to={to}
                    end={end || to === '/signups'}
                    onClick={() => setSidebarOpen(false)}
                    className={({ isActive }) =>
                      `group relative flex items-center gap-3 px-3 py-2.5 rounded-xl text-[13px] font-medium transition-all duration-200 ${
                        isActive
                          ? 'bg-white/15 text-white shadow-lg shadow-black/10 backdrop-blur-sm'
                          : 'text-white/70 hover:bg-white/10 hover:text-white'
                      }`
                    }
                  >
                    {({ isActive }) => (
                      <>
                        {isActive && (
                          <span className="absolute left-0 top-1/2 -translate-y-1/2 w-[3px] h-5 rounded-r-full bg-white shadow-sm shadow-white/50" />
                        )}
                        <span className={`transition-transform duration-200 ${isActive ? '' : 'group-hover:scale-110'}`}>
                          {icon}
                        </span>
                        {t(labelKey)}
                      </>
                    )}
                  </NavLink>
                ))}
              </div>
            </div>
          ))}
        </nav>

        {/* Footer */}
        <div className="px-5 py-4 border-t border-white/10 relative z-10">
          <div className="flex items-center justify-between mb-2">
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse shadow-sm shadow-emerald-400/50" />
              <span className="text-[11px] text-white/50 font-medium">{t('nav.development')}</span>
            </div>
            <div className="flex rounded-lg overflow-hidden border border-white/20">
              <button
                onClick={() => setLang('en')}
                className={`px-2 py-0.5 text-[10px] font-bold transition-colors ${
                  lang === 'en' ? 'bg-white/20 text-white' : 'text-white/40 hover:text-white hover:bg-white/10'
                }`}
              >
                EN
              </button>
              <button
                onClick={() => setLang('da')}
                className={`px-2 py-0.5 text-[10px] font-bold transition-colors ${
                  lang === 'da' ? 'bg-white/20 text-white' : 'text-white/40 hover:text-white hover:bg-white/10'
                }`}
              >
                DA
              </button>
            </div>
          </div>
          <p className="text-[10px] text-white/25 mt-1">Wattzon v0.1</p>
        </div>
      </aside>

      {/* Main content */}
      <main className="flex-1 overflow-auto pt-14 md:pt-0">
        <Outlet />
      </main>
    </div>
  );
}
