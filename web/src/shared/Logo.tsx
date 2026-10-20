/** Zeichen von EstrichONE (branding/logo-mark.svg): ein „E“ aus den Schichten eines Estrichaufbaus. Für dunkle Flächen. */
export function LogoMark({ size = 22 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="18 17 64 66" aria-hidden="true">
      <path d="M27 22h7v56h-7a4 4 0 0 1-4-4V26a4 4 0 0 1 4-4z" fill="#FFFFFF" />
      <rect x="23" y="64" width="54" height="14" rx="4" fill="#FFFFFF" />
      <path d="M23 26a4 4 0 0 1 4-4h46a4 4 0 0 1 4 4v1.5c0 3-1.6 5.5-4.5 6.5L66 36H23z" fill="#FFFFFF" />
      <rect x="23" y="43" width="38" height="14" rx="4" fill="#8EC5FF" />
      <circle cx="71" cy="50" r="6" fill="#3CCB7F" />
    </svg>
  );
}
