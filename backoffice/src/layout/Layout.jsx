import { NavLink, Outlet } from 'react-router-dom';

const navSections = [
  {
    label: 'Overview',
    items: [
      {
        to: '/',
        label: 'Dashboard',
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
    label: 'Onboarding',
    items: [
      {
        to: '/signups',
        label: 'Signups',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" />
          </svg>
        ),
      },
      {
        to: '/signups/new',
        label: 'New Signup',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
        ),
      },
    ],
  },
  {
    label: 'Portfolio',
    items: [
      {
        to: '/customers',
        label: 'Customers',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z" />
          </svg>
        ),
      },
      {
        to: '/products',
        label: 'Products',
        icon: (
          <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z" />
          </svg>
        ),
      },
    ],
  },
];

export default function Layout() {
  return (
    <div className="flex h-screen bg-gradient-to-br from-slate-50 via-white to-amber-50/30">
      {/* Sidebar — amber-to-orange gradient */}
      <aside className="w-[240px] bg-gradient-to-b from-amber-500 via-amber-600 to-orange-700 flex flex-col shrink-0 shadow-2xl shadow-amber-900/20 relative overflow-hidden">
        {/* Decorative orbs */}
        <div className="absolute top-0 right-0 w-32 h-32 bg-white/5 rounded-full -translate-y-1/2 translate-x-1/2 blur-2xl" />
        <div className="absolute bottom-20 left-0 w-24 h-24 bg-orange-400/10 rounded-full -translate-x-1/2 blur-xl" />

        {/* Brand */}
        <div className="px-5 pt-7 pb-6 relative z-10">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-white/15 backdrop-blur-sm flex items-center justify-center glow-pulse">
              <svg className="w-6 h-6" viewBox="0 0 32 32" fill="none">
                <path d="M18.5 3L7 18h7.5l-2 11L26 14h-7.5l2-11z" fill="white" />
                <path d="M18.5 3L7 18h7.5l-2 11L26 14h-7.5l2-11z" fill="url(#volt-shine)" fillOpacity="0.3" />
                <defs>
                  <linearGradient id="volt-shine" x1="10" y1="3" x2="22" y2="29">
                    <stop stopColor="white" stopOpacity="0.8" />
                    <stop offset="1" stopColor="white" stopOpacity="0" />
                  </linearGradient>
                </defs>
              </svg>
            </div>
            <div>
              <span className="text-[18px] font-bold text-white tracking-tight">Volt</span>
              <p className="text-[10px] text-amber-100/60 font-medium">Energy Platform</p>
            </div>
          </div>
        </div>

        {/* Navigation */}
        <nav className="flex-1 px-3 pb-4 space-y-5 overflow-y-auto relative z-10">
          {navSections.map((section) => (
            <div key={section.label}>
              <p className="px-3 mb-1.5 text-[10px] font-semibold uppercase tracking-[0.12em] text-amber-200/50">
                {section.label}
              </p>
              <div className="space-y-0.5">
                {section.items.map(({ to, label, icon, end }) => (
                  <NavLink
                    key={to}
                    to={to}
                    end={end || to === '/signups'}
                    className={({ isActive }) =>
                      `group relative flex items-center gap-3 px-3 py-2.5 rounded-xl text-[13px] font-medium transition-all duration-200 ${
                        isActive
                          ? 'bg-white/15 text-white shadow-lg shadow-black/10 backdrop-blur-sm'
                          : 'text-amber-100/70 hover:bg-white/10 hover:text-white'
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
                        {label}
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
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse shadow-sm shadow-emerald-400/50" />
            <span className="text-[11px] text-amber-100/60 font-medium">Development</span>
          </div>
          <p className="text-[10px] text-amber-200/30 mt-1">Volt v0.1</p>
        </div>
      </aside>

      {/* Main content */}
      <main className="flex-1 overflow-auto">
        <Outlet />
      </main>
    </div>
  );
}
