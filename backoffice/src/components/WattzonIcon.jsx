/**
 * Wattzon brand icon — four-quadrant grid with animated fill.
 * The filled quadrant rotates clockwise on hover (parent adds .wz-icon-hover)
 * or when `animate` prop is true.
 */
export default function WattzonIcon({ size = 22, animate = false, className = '' }) {
  return (
    <svg
      className={`wz-icon ${animate ? 'wz-icon-animate' : ''} ${className}`}
      width={size}
      height={size}
      viewBox="0 0 32 32"
      fill="none"
    >
      <rect x="4" y="4" width="11" height="11" rx="2" stroke="currentColor" strokeWidth="1.5" />
      <rect x="17" y="4" width="11" height="11" rx="2" stroke="currentColor" strokeWidth="1.5" />
      <rect x="4" y="17" width="11" height="11" rx="2" stroke="currentColor" strokeWidth="1.5" />
      <rect x="17" y="17" width="11" height="11" rx="2" stroke="currentColor" strokeWidth="1.5" />
      <rect
        className="wz-icon-fill"
        x="17" y="17" width="11" height="11" rx="2"
        fill="currentColor"
        fillOpacity="0.25"
        stroke="currentColor"
        strokeWidth="1.5"
      />
    </svg>
  );
}
