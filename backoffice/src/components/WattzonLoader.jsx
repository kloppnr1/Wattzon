import WattzonIcon from './WattzonIcon';

/**
 * Branded loading indicator used across all pages.
 * Replaces the generic teal spinner with the Wattzon grid icon animation.
 */
export default function WattzonLoader({ message }) {
  return (
    <div className="flex items-center justify-center h-full">
      <div className="flex flex-col items-center gap-4">
        <div className="w-12 h-12 rounded-xl bg-slate-100 flex items-center justify-center text-teal-700">
          <WattzonIcon size={28} animate />
        </div>
        {message && (
          <p className="text-sm text-slate-400 font-medium tracking-wide">{message}</p>
        )}
      </div>
    </div>
  );
}
