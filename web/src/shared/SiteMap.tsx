import 'leaflet/dist/leaflet.css';
import type { LatLngBoundsExpression, LatLngExpression, LeafletMouseEvent } from 'leaflet';
import { useEffect } from 'react';
import { CircleMarker, MapContainer, Popup, TileLayer, Tooltip, useMap, useMapEvents } from 'react-leaflet';
import type { BauakteStatus } from '../api/models';

/** Farben der Marker je Status – gleiche Bedeutung wie die Status-Tags. */
export const statusMarkerColors: Record<BauakteStatus, string> = {
  inquiry: '#8C8C8C', planned: '#1677FF', inProgress: '#0B2D5B', drying: '#FA8C16', completed: '#52C41A', cancelled: '#BFBFBF',
};

export interface MapMarker {
  id: string;
  latitude: number;
  longitude: number;
  color?: string;
  label: string;
  popup?: React.ReactNode;
}

/** Mitte Deutschlands, wenn noch kein Standort bekannt ist. */
const fallbackCenter: LatLngExpression = [51.16, 10.45];

function FitToMarkers({ markers, zoom }: { markers: MapMarker[]; zoom: number }) {
  const map = useMap();
  const key = markers.map((m) => `${m.id}:${m.latitude}:${m.longitude}`).join('|');
  useEffect(() => {
    if (markers.length === 1) {
      map.setView([markers[0].latitude, markers[0].longitude], zoom);
    } else if (markers.length > 1) {
      const bounds: LatLngBoundsExpression = markers.map((m) => [m.latitude, m.longitude]);
      map.fitBounds(bounds, { padding: [32, 32], maxZoom: 15 });
    }
    // Nur bei geänderten Markern neu ausrichten, nicht bei jedem Rendern.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  return null;
}

function PickLocation({ onPick }: { onPick: (latitude: number, longitude: number) => void }) {
  useMapEvents({ click: (event: LeafletMouseEvent) => onPick(Number(event.latlng.lat.toFixed(6)), Number(event.latlng.lng.toFixed(6))) });
  return null;
}

/**
 * Karte mit OpenStreetMap-Kacheln. Marker als Vektorpunkte (keine Bilddateien nötig).
 * Mit <paramref name="onPick"/> wird ein Klick in die Karte als neuer Standort gemeldet.
 */
export function SiteMap({ markers, height = 320, zoom = 15, onPick, className }: {
  markers: MapMarker[];
  height?: number | string;
  zoom?: number;
  onPick?: (latitude: number, longitude: number) => void;
  className?: string;
}) {
  return (
    <div className={className} style={{ height, borderRadius: 6, overflow: 'hidden', cursor: onPick ? 'crosshair' : undefined }}
      role="region" aria-label="Karte">
      <MapContainer center={markers[0] ? [markers[0].latitude, markers[0].longitude] : fallbackCenter} zoom={markers.length ? zoom : 6}
        style={{ height: '100%', width: '100%' }} scrollWheelZoom>
        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap</a>-Mitwirkende'
          url="https://tile.openstreetmap.org/{z}/{x}/{y}.png"
          maxZoom={19}
        />
        {markers.map((m) => (
          <CircleMarker key={m.id} center={[m.latitude, m.longitude]} radius={9}
            pathOptions={{ color: '#fff', weight: 2, fillColor: m.color ?? '#0B2D5B', fillOpacity: 0.95 }}>
            <Tooltip direction="top" offset={[0, -8]}>{m.label}</Tooltip>
            {m.popup && <Popup>{m.popup}</Popup>}
          </CircleMarker>
        ))}
        <FitToMarkers markers={markers} zoom={zoom} />
        {onPick && <PickLocation onPick={onPick} />}
      </MapContainer>
    </div>
  );
}
