import { NavLink, Outlet, useLocation } from 'react-router-dom';

const navSections = [
  {
    label: 'Onboarding',
    items: [
      {
        to: '/signups',
        label: 'Signups',
        icon: (
          <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" />
          </svg>
        ),
      },
      {
        to: '/signups/new',
        label: 'New Signup',
        icon: (
          <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
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
          <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z" />
          </svg>
        ),
      },
      {
        to: '/products',
        label: 'Products',
        icon: (
          <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z" />
          </svg>
        ),
      },
    ],
  },
];

const pageTitle = {
  '/signups': 'Signups',
  '/signups/new': 'New Signup',
  '/customers': 'Customers',
  '/products': 'Products',
};

export default function Layout() {
  const location = useLocation();

  // Derive title from path
  const title =
    pageTitle[location.pathname] ||
    (location.pathname.startsWith('/signups/') ? 'Signup Detail' : '') ||
    (location.pathname.startsWith('/customers/') ? 'Customer Detail' : '') ||
    'Back Office';

  return (
    <div className="flex h-screen bg-slate-100">
      {/* Sidebar */}
      <aside className="w-60 bg-slate-900 flex flex-col shrink-0">
        {/* Brand */}
        <div className="px-5 py-5">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-indigo-500 flex items-center justify-center">
              <svg className="w-5 h-5 text-white" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z" />
              </svg>
            </div>
            <div>
              <h1 className="text-sm font-semibold text-white tracking-wide">Settlement</h1>
              <p className="text-[11px] text-slate-400">Back Office</p>
            </div>
          </div>
        </div>

        {/* Navigation */}
        <nav className="flex-1 px-3 pb-4 space-y-6 overflow-y-auto">
          {navSections.map((section) => (
            <div key={section.label}>
              <p className="px-3 mb-2 text-[11px] font-semibold uppercase tracking-wider text-slate-500">
                {section.label}
              </p>
              <div className="space-y-0.5">
                {section.items.map(({ to, label, icon }) => (
                  <NavLink
                    key={to}
                    to={to}
                    end={to === '/signups'}
                    className={({ isActive }) =>
                      `flex items-center gap-3 px-3 py-2 rounded-lg text-sm transition-colors ${
                        isActive
                          ? 'bg-indigo-500/15 text-indigo-400 font-medium'
                          : 'text-slate-400 hover:bg-slate-800 hover:text-slate-200'
                      }`
                    }
                  >
                    {icon}
                    {label}
                  </NavLink>
                ))}
              </div>
            </div>
          ))}
        </nav>

        {/* Footer */}
        <div className="px-5 py-4 border-t border-slate-800">
          <p className="text-[11px] text-slate-500">DataHub Settlement v0.1</p>
        </div>
      </aside>

      {/* Main */}
      <div className="flex-1 flex flex-col overflow-hidden">
        {/* Top bar */}
        <header className="h-14 bg-white border-b border-slate-200 flex items-center px-8 shrink-0">
          <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
        </header>

        {/* Content */}
        <main className="flex-1 overflow-auto p-8">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
