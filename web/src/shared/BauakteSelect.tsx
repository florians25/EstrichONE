import { Select } from 'antd';
import { useRef, useState } from 'react';
import { get } from '../api/client';
import type { BauaktePage } from '../api/models';

/** Bauakte suchen (Nummer, Bauvorhaben, Ort). */
export function BauakteSelect({ id, value, onChange, initialLabel }: { id?: string; value?: string | null; onChange?: (v: string | null) => void; initialLabel: string | null }) {
  const [options, setOptions] = useState<{ value: string; label: string }[]>(value && initialLabel ? [{ value, label: initialLabel }] : []);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const search = (text: string) => {
    clearTimeout(timer.current);
    timer.current = setTimeout(async () => {
      if (text.trim().length < 2) {
        return;
      }
      const page = await get<BauaktePage>('bauakten', { search: text.trim(), limit: 8 });
      setOptions(page.items.map((b) => ({ value: b.id, label: `${b.number ?? 'neu'} · ${b.title}${b.city ? ` (${b.city})` : ''}` })));
    }, 200);
  };
  return (
    <Select id={id} aria-label="Bauakte" showSearch allowClear filterOption={false} placeholder="Nummer oder Bauvorhaben eingeben" value={value}
      onChange={(v) => onChange?.(v ?? null)} onSearch={search} options={options} notFoundContent={null} />
  );
}
