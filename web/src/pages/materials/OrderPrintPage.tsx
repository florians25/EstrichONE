import { useParams } from 'react-router-dom';
import { OrderPrintView } from './OrderDetail';

/** Eigenständige Druckseite für den Bestellschein (öffnet in neuem Tab). */
export default function OrderPrintPage() {
  const { id = '' } = useParams();
  return <OrderPrintView orderId={id} />;
}
