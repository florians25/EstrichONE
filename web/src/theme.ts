import type { ThemeConfig } from 'antd';

/** Markenfarben (05-designsystem): Navy als Primärfarbe, ruhige Business-Optik mit dichten Tabellen. */
export const brand = {
  navy: '#0B2D5B',
  navyLight: '#12396F',
  blue: '#007BFF',
  action: '#0062CC',
  text: '#1F2937',
  line: '#D1D5DB',
};

export const theme: ThemeConfig = {
  token: {
    colorPrimary: brand.navy,
    colorInfo: brand.action,
    colorLink: brand.action,
    colorTextBase: brand.text,
    colorBorder: brand.line,
    borderRadius: 4,
    fontFamily: '"Segoe UI", system-ui, -apple-system, Roboto, "Helvetica Neue", Arial, sans-serif',
    fontSize: 14,
  },
  components: {
    Layout: { headerBg: brand.navy, siderBg: '#ffffff', bodyBg: '#F0F2F5' },
    Table: { headerBg: '#F3F5F8', rowHoverBg: '#EEF5FF', cellPaddingBlockSM: 6 },
    Menu: { itemSelectedBg: '#E6EEF8', itemSelectedColor: brand.navy, itemHeight: 38 },
    Card: { headerBg: '#FAFBFC' },
    Tabs: { inkBarColor: brand.navy, itemSelectedColor: brand.navy },
  },
};
