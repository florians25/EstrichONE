import { PlusOutlined } from '@ant-design/icons';
import { Button, Divider, Select, Spin } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { get, post, problemMessage } from '../api/client';
import type { ContactResponse } from '../api/models';
import { uuidv7 } from './ids';

interface Props {
  id?: string;
  value?: string | null;
  onChange?: (value: string | null) => void;
}

/** Kunde suchen (Name, Kundennummer, Ort) oder mit dem eingegebenen Namen direkt anlegen. */
export function CustomerSelect({ id, value, onChange }: Props) {
  const [options, setOptions] = useState<ContactResponse[]>([]);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => {
    if (value && !options.some((o) => o.id === value)) {
      void get<ContactResponse>(`contacts/${value}`).then((c) => setOptions((all) => [c, ...all]), () => undefined);
    }
  }, [value, options]);

  const onSearch = (text: string) => {
    setSearch(text);
    clearTimeout(timer.current);
    timer.current = setTimeout(async () => {
      if (text.trim().length < 2) {
        return;
      }
      setLoading(true);
      try {
        setOptions(await get<ContactResponse[]>('contacts', { search: text.trim(), limit: 10 }));
      } finally {
        setLoading(false);
      }
    }, 200);
  };

  const create = async () => {
    try {
      const contact = await post<ContactResponse>('contacts', { id: uuidv7(), kind: 'person', name: search.trim() });
      setOptions((all) => [contact, ...all]);
      setSearch('');
      onChange?.(contact.id);
    } catch (e) {
      setError(problemMessage(e));
    }
  };

  return (
    <Select
      id={id}
      showSearch
      allowClear
      value={value ?? undefined}
      placeholder="Name, Kundennummer oder Ort"
      filterOption={false}
      onSearch={onSearch}
      onChange={(v) => onChange?.(v ?? null)}
      notFoundContent={loading ? <Spin size="small" /> : search.trim().length < 2 ? 'Mindestens zwei Zeichen eingeben' : 'Kein Kunde gefunden'}
      options={options.map((c) => ({ value: c.id, label: [c.name, c.customerNumber, c.city].filter(Boolean).join(' · ') }))}
      popupRender={(menu) => (
        <>
          {menu}
          {search.trim().length >= 2 && (
            <>
              <Divider style={{ margin: '4px 0' }} />
              <Button type="link" icon={<PlusOutlined />} onClick={create} block style={{ textAlign: 'left' }}>
                „{search.trim()}“ als neuen Kunden anlegen
              </Button>
            </>
          )}
          {error && <div style={{ color: '#b91c1c', padding: '4px 12px' }}>{error}</div>}
        </>
      )}
    />
  );
}
