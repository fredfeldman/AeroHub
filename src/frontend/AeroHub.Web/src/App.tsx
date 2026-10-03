import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import planeIconUrl from './assets/aircraft-icons/plane.png'
import selectedPlaneIconUrl from './assets/aircraft-icons/splane.png'
import helicopterIconUrl from './assets/aircraft-icons/helicopter.png'
import turbopropIconUrl from './assets/aircraft-icons/turboprop.png'
import unknownTypeIconUrl from './assets/aircraft-icons/unknown.png'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import './App.css'

// Center of Maidenhead grid locator EM13pc (near Allen, TX)
const ALLEN_TX_CENTER: [number, number] = [33.104167, -96.708333]

const MILES_TO_METERS = 1609.344
const RANGE_RING_MILES = [50, 100, 150, 200]
const MAP_VIEW_STORAGE_KEY = 'aerohub-map-view'
const ALL_AIRCRAFT_CLASSES: AircraftClass[] = ['Military', 'Government', 'Commercial', 'Civilian', 'Unknown']

type EmitterCategoryInfo = {
  iconUrl: string
  label: string
  rotatable: boolean
}

const EMITTER_CATEGORY_INFO: Record<string, EmitterCategoryInfo> = {
  A1: { iconUrl: turbopropIconUrl, label: 'Light', rotatable: true },
  A2: { iconUrl: turbopropIconUrl, label: 'Small', rotatable: true },
  A3: { iconUrl: planeIconUrl, label: 'Large', rotatable: true },
  A4: { iconUrl: planeIconUrl, label: 'High vortex large', rotatable: true },
  A5: { iconUrl: planeIconUrl, label: 'Heavy', rotatable: true },
  A6: { iconUrl: planeIconUrl, label: 'High performance', rotatable: true },
  A7: { iconUrl: helicopterIconUrl, label: 'Rotorcraft', rotatable: false },
  B1: { iconUrl: unknownTypeIconUrl, label: 'Glider/sailplane', rotatable: false },
  B2: { iconUrl: unknownTypeIconUrl, label: 'Lighter-than-air', rotatable: false },
  B4: { iconUrl: turbopropIconUrl, label: 'Ultralight', rotatable: true },
  B6: { iconUrl: unknownTypeIconUrl, label: 'UAV/drone', rotatable: false },
}

const DEFAULT_CATEGORY_INFO: EmitterCategoryInfo = { iconUrl: unknownTypeIconUrl, label: 'Unknown type', rotatable: false }

function describeEmitterCategory(category?: string): string {
  if (!category) {
    return 'Unknown type'
  }

  return EMITTER_CATEGORY_INFO[category]?.label ?? `Category ${category}`
}

type AircraftClass = 'Military' | 'Government' | 'Commercial' | 'Civilian' | 'Unknown'

const AIRCRAFT_CLASS_COLORS: Record<AircraftClass, string> = {
  Military: '#e06c75',
  Government: '#8a63d2',
  Commercial: '#4fc3d9',
  Civilian: '#9be7c0',
  Unknown: '#5f747c',
}

const MILITARY_OPERATOR_PATTERN = /AIR FORCE|NAVY|ARMY|MARINE CORPS|\bUSAF\b|\bUSN\b|\bUSMC\b|NATIONAL GUARD|\bDEFEN[CS]E\b|MILITARY/
const GOVERNMENT_OPERATOR_PATTERN = /UNITED STATES|\bU\.?S\.?\b|FEDERAL|CUSTOMS|BORDER PROTECTION|DEPARTMENT OF|\bFAA\b|\bNASA\b|SHERIFF|POLICE|STATE OF|COUNTY OF|CITY OF|GOVERNMENT|COAST GUARD/
const COMMERCIAL_CALLSIGN_PATTERN = /^[A-Z]{3}\d/

function classifyAircraft(track: AircraftTrack): AircraftClass {
  if (track.remoteIdOperationType?.toUpperCase() === 'COMMERCIAL') {
    return 'Commercial'
  }

  const operator = track.operatorName?.toUpperCase().trim() ?? ''

  if (track.isMilitary || MILITARY_OPERATOR_PATTERN.test(operator)) {
    return 'Military'
  }

  if (GOVERNMENT_OPERATOR_PATTERN.test(operator)) {
    return 'Government'
  }

  const callsign = track.callsign?.toUpperCase().trim()
  const registration = track.registration?.toUpperCase().trim()

  if (callsign && callsign !== registration && COMMERCIAL_CALLSIGN_PATTERN.test(callsign)) {
    return 'Commercial'
  }

  if (operator || registration) {
    return 'Civilian'
  }

  return 'Unknown'
}

function getAltitudeBandColor(altitudeFeet?: number): string {
  if (altitudeFeet === undefined) {
    return '#5f747c'
  }

  if (altitudeFeet < 1000) {
    return '#8fa2aa'
  }

  if (altitudeFeet < 10000) {
    return '#9be7c0'
  }

  if (altitudeFeet < 25000) {
    return '#e5c07b'
  }

  return '#e06c75'
}

function getAircraftIcon(track: AircraftTrack, isSelected: boolean) {
  const info = (track.emitterCategory && EMITTER_CATEGORY_INFO[track.emitterCategory]) || DEFAULT_CATEGORY_INFO
  const useSelectedArt = isSelected && info.iconUrl === planeIconUrl
  const iconUrl = useSelectedArt ? selectedPlaneIconUrl : info.iconUrl
  const rotation = info.rotatable ? track.trackDegrees ?? 0 : 0
  const size = isSelected ? 34 : 24
  const bandColor = getAltitudeBandColor(track.altitudeFeet)

  return L.divIcon({
    className: isSelected ? 'aircraft-marker-icon selected' : 'aircraft-marker-icon',
    html: `<span class="altitude-band" style="background:${bandColor};"></span><img src="${iconUrl}" alt="" style="width:${size}px;height:${size}px;transform:rotate(${rotation}deg);" />`,
    iconSize: [size, size],
    iconAnchor: [size / 2, size / 2],
    popupAnchor: [0, -size / 2],
  })
}

const MAX_TRACK_HISTORY_POINTS = 300
const MINIMUM_TRACK_HISTORY_POINT_METERS = 25

type TrackHistoryEntry = { point: L.LatLng; atUtc: number }

function recordTrackHistory(
  history: Map<string, TrackHistoryEntry[]>,
  aircraftIdentifier: string,
  latitude: number,
  longitude: number,
  atUtc: number,
) {
  const entries = history.get(aircraftIdentifier) ?? []
  const nextPoint = L.latLng(latitude, longitude)
  const lastEntry = entries[entries.length - 1]

  if (lastEntry && lastEntry.point.distanceTo(nextPoint) < MINIMUM_TRACK_HISTORY_POINT_METERS) {
    return
  }

  entries.push({ point: nextPoint, atUtc })

  if (entries.length > MAX_TRACK_HISTORY_POINTS) {
    entries.shift()
  }

  history.set(aircraftIdentifier, entries)
}

const EARTH_RADIUS_MILES = 3958.8

function distanceMilesFromCenter(latitude: number, longitude: number): number {
  const [centerLat, centerLon] = ALLEN_TX_CENTER
  const toRadians = (value: number) => (value * Math.PI) / 180
  const deltaLat = toRadians(latitude - centerLat)
  const deltaLon = toRadians(longitude - centerLon)
  const a =
    Math.sin(deltaLat / 2) ** 2 +
    Math.cos(toRadians(centerLat)) * Math.cos(toRadians(latitude)) * Math.sin(deltaLon / 2) ** 2
  return EARTH_RADIUS_MILES * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
}

function bearingDegreesFromCenter(latitude: number, longitude: number): number {
  const [centerLat, centerLon] = ALLEN_TX_CENTER
  const toRadians = (value: number) => (value * Math.PI) / 180
  const y = Math.sin(toRadians(longitude - centerLon)) * Math.cos(toRadians(latitude))
  const x =
    Math.cos(toRadians(centerLat)) * Math.sin(toRadians(latitude)) -
    Math.sin(toRadians(centerLat)) * Math.cos(toRadians(latitude)) * Math.cos(toRadians(longitude - centerLon))
  return (((Math.atan2(y, x) * 180) / Math.PI) + 360) % 360
}

function formatAge(iso: string): string {
  const ageSeconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000))

  if (ageSeconds < 60) {
    return `${ageSeconds}s ago`
  }

  return `${Math.round(ageSeconds / 60)}m ago`
}

function exportTracksAsCsv(tracks: AircraftTrack[]) {
  const header = ['hex', 'callsign', 'registration', 'type', 'class', 'latitude', 'longitude', 'altitudeFeet', 'groundSpeedKnots', 'trackDegrees', 'updatedAtUtc']
  const rows = tracks.map((track) => [
    track.aircraftIdentifier,
    track.callsign ?? '',
    track.registration ?? '',
    track.aircraftType ?? '',
    classifyAircraft(track),
    track.latitude ?? '',
    track.longitude ?? '',
    track.altitudeFeet ?? '',
    track.groundSpeedKnots ?? '',
    track.trackDegrees ?? '',
    track.updatedAtUtc,
  ])
  const csv = [header, ...rows].map((row) => row.map((value) => `"${String(value).replace(/"/g, '""')}"`).join(',')).join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `aerohub-aircraft-${new Date().toISOString().replace(/[:.]/g, '-')}.csv`
  link.click()
  URL.revokeObjectURL(url)
}

function exportTracksAsGeoJson(tracks: AircraftTrack[]) {
  const geojson = {
    type: 'FeatureCollection',
    features: tracks
      .filter((track) => track.latitude !== undefined && track.longitude !== undefined)
      .map((track) => ({
        type: 'Feature',
        geometry: {
          type: 'Point',
          coordinates: [track.longitude!, track.latitude!, track.altitudeFeet ? Math.round(track.altitudeFeet * 0.3048) : 0],
        },
        properties: {
          hex: track.aircraftIdentifier,
          callsign: track.callsign ?? null,
          registration: track.registration ?? null,
          aircraftType: track.aircraftType ?? null,
          aircraftClass: classifyAircraft(track),
          altitudeFeet: track.altitudeFeet ?? null,
          groundSpeedKnots: track.groundSpeedKnots ?? null,
          trackDegrees: track.trackDegrees ?? null,
          sourceId: track.sourceId,
          sourceType: track.sourceType,
          updatedAtUtc: track.updatedAtUtc,
        },
      })),
  }
  const blob = new Blob([JSON.stringify(geojson, null, 2)], { type: 'application/geo+json;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `aerohub-aircraft-${new Date().toISOString().replace(/[:.]/g, '-')}.geojson`
  link.click()
  URL.revokeObjectURL(url)
}

type AircraftPhotoInfo = {
  thumbnailUrl: string
  largeUrl?: string
  photographerName?: string
  sourceLink?: string
}

type HealthStatus = {
  serviceName: string
  version: string
  environment: string
  startedAtUtc: string
  checkedAtUtc: string
  capabilities: string[]
}

type HealthState =
  | { status: 'checking' }
  | { status: 'online'; data: HealthStatus }
  | { status: 'offline'; message: string }

type OperatorDiagnostics = {
  serviceName: string
  version: string
  environment: string
  startedAtUtc: string
  checkedAtUtc: string
  activeDecoders: number
  activeImports: number
  streamQueueDepth: number
  activeWarnings: number
  systemHealth: string
  storageBytesUsed: number
  recentWarnings: string[]
}

type SourceState = 'Offline' | 'Starting' | 'Online' | 'Degraded' | 'Failed'

type RuntimeSource = {
  id: string
  name: string
  state: SourceState
  adapterName: string
  frequencyMHz?: number
  capabilities: string[]
}

type DecoderState = {
  id: string
  name: string
  state: SourceState
  produces: string[]
  requiresExclusiveSource: boolean
}

type ImportState = {
  id: string
  name: string
  state: SourceState
  sourceFormat: string
  acceptedRecords: number
  rejectedRecords: number
  lastError?: string
}

type ImportDiagnostic = {
  id: string
  occurredAtUtc: string
  importId: string
  severity: string
  code: string
  message: string
  rawReference?: string
}

type ExternalDecoderProcess = {
  id: string
  name: string
  state: SourceState
  executableName: string
  detectedVersion?: string
  restartCount: number
  restartLimit: number
  lastStartedAtUtc?: string
  lastStoppedAtUtc?: string
  lastError?: string
}

type ExternalDecoderDiagnostic = {
  id: string
  occurredAtUtc: string
  processId: string
  severity: string
  code: string
  message: string
}

type NormalizedAviationMessage = {
  id: string
  sequence: number
  receivedAtUtc: string
  kind: string
  summary: string
  acars?: {
    label?: string
    sublabel?: string
    preamble?: string
    applicationCategory: string
    decodedText: string
    unconsumedText?: string
  }
  datalink?: {
    applicationType: string
    direction: string
    messageReference?: string
    acknowledgementState?: string
    category: string
    aircraftIdentifier?: string
    latitude?: number
    longitude?: number
    altitudeFeet?: number
    rawText: string
  }
  satcom?: {
    satellite?: string
    channel?: string
    bearer?: string
    groundEndpoint?: string
    reassemblyState: string
  }
  aircraftIdentifier?: string
  transport?: string
  frequencyMHz?: number
  confidence: string
  rawPayload: string
  warnings: Array<{
    code: string
    message: string
    severity: string
  }>
  provenance: {
    path: string
    sourceId: string
    sourceName: string
    sourceFormat?: string
    adapterName: string
    rawReference: string
  }
}

type RuntimeCatalog = {
  sources: RuntimeSource[]
  decoders: DecoderState[]
  imports: ImportState[]
}

type SpectrumFrame = {
  sourceId: string
  sequence: number
  generatedAtUtc: string
  centerFrequencyMHz: number
  spanKHz: number
  bins: number[]
}

type WaterfallRow = {
  sequence: number
  generatedAtUtc: string
  intensities: number[]
}

type WaterfallRows = {
  sourceId: string
  firstSequence: number
  generatedAtUtc: string
  width: number
  rows: WaterfallRow[]
}

type StreamMetrics = {
  sourceId: string
  state: SourceState
  speedMultiplier: number
  spectrumFramesProduced: number
  waterfallRowsProduced: number
  droppedVisualizationFrames: number
  queueDepth: number
  slowClientCount: number
  lastGeneratedAtUtc?: string
}

type AircraftTrack = {
  aircraftIdentifier: string
  updatedAtUtc: string
  latitude?: number
  longitude?: number
  altitudeFeet?: number
  sourceId: string
  confidence: string
  callsign?: string
  groundSpeedKnots?: number
  trackDegrees?: number
  sourceType: string
  correlationGroupId: string
  isStale: boolean
  emitterCategory?: string
  aircraftType?: string
  registration?: string
  operatorName?: string
  isMilitary?: boolean
  remoteIdSerialNumber?: string
  remoteIdOperatorId?: string
  remoteIdOperationType?: string
  remoteIdOperationTypeSource?: string
  remoteIdUasIdType?: string
  remoteIdUaType?: string
  remoteIdOperatorIdType?: number
  remoteIdMessageType?: string
  remoteIdAltitudeGeodeticMeters?: number
  remoteIdHeightAboveGroundMeters?: number
  remoteIdAltitudeBarometricMeters?: number
  remoteIdAltitudeReference?: string
  remoteIdVerticalSpeedMetersPerSecond?: number
  remoteIdHorizontalAccuracyMeters?: number
  remoteIdVerticalAccuracyMeters?: number
  remoteIdSpeedAccuracyMetersPerSecond?: number
  remoteIdDirectionAccuracyDegrees?: number
  remoteIdBroadcastAtUtc?: string
  remoteIdRadio?: string
  remoteIdSourceMac?: string
  remoteIdChannel?: number
  remoteIdRssi?: number
  remoteIdReceiverId?: string
  provenance: {
    path: string
    sourceId: string
    sourceName: string
    sourceApp?: string
    sourceFormat?: string
    rawReference: string
  }
}

type RemoteIdObservation = {
  id: string
  uasId?: string
  uasIdType?: string
  uaType?: string
  operatorId?: string
  operatorIdType?: number
  operationType?: string
  operationTypeSource?: string
  messageType: string
  broadcastAtUtc?: string
  receivedAtUtc: string
  latitude?: number
  longitude?: number
  altitudeGeodeticMeters?: number
  heightAboveGroundMeters?: number
  altitudeBarometricMeters?: number
  altitudeReference?: string
  groundSpeedMetersPerSecond?: number
  headingDegrees?: number
  verticalSpeedMetersPerSecond?: number
  horizontalAccuracyMeters?: number
  verticalAccuracyMeters?: number
  speedAccuracyMetersPerSecond?: number
  directionAccuracyDegrees?: number
  operatorLatitude?: number
  operatorLongitude?: number
  areaCount?: number
  areaRadiusMeters?: number
  areaCeilingMeters?: number
  areaFloorMeters?: number
  selfIdText?: string
  authenticationStatus?: string
  authenticationVerifiedBySource?: boolean
  radio?: string
  rssi?: number
  channel?: number
  receiverId?: string
  sourceMac?: string
  validationStatus?: string
  validationWarnings: string[]
}

type SondeTrack = {
  serial: string
  updatedAtUtc: string
  latitude?: number
  longitude?: number
  altitudeMeters?: number
  sourceId: string
  confidence: string
  sondeType?: string
  ascentRateMetersPerSecond?: number
  temperatureCelsius?: number
  humidityPercent?: number
  pressureHpa?: number
  sourceType: string
  correlationGroupId: string
  isStale: boolean
  frameSequence?: number
  crcValid?: boolean
  burstKill?: boolean
  batteryVoltage?: number
  frequencyMHz?: number
  rssi?: number
  provenance: {
    path: string
    sourceId: string
    sourceName: string
    sourceApp?: string
    sourceFormat?: string
    rawReference: string
  }
}

type FeederStatus = {
  id: string
  name: string
  targetService: string
  protocol: string
  mode: string
  state: SourceState
  host?: string
  port: number
  stationId?: string
  connectedClients: number
  framesSent: number
  bytesSent: number
  framesDropped: number
  lastSentAtUtc?: string
  lastError?: string
}

type FeederDiagnostic = {
  id: string
  occurredAtUtc: string
  feederId: string
  severity: string
  code: string
  message: string
}

type WefaxSettings = {
  isInverted: boolean
  slantCorrection: number
  ioc: number
  lineRateRpm: number
}

type Dump1090Settings = {
  host?: string
  port: number
  jsonPath: string
  pollIntervalSeconds: number
  autoConnect: boolean
}

type FeederSetting = {
  feederId: string
  host: string
  port: number
  stationId: string
  autoStart: boolean
}

type HardwareSourceSetting = {
  sourceId: string
  frequencyMHz?: number
  sampleRateHz?: number
  gainDb?: number
  agcEnabled: boolean
}

type LocalFrequencyProfileSetting = {
  id: string
  name: string
  frequencyMHz: number
  tags: string[]
}

type DecoderSettings = {
  wefax: WefaxSettings
  dump1090: Dump1090Settings
  feeders: FeederSetting[]
  hardwareSources: HardwareSourceSetting[]
  frequencyProfiles: LocalFrequencyProfileSetting[]
  staleTrackTimeoutMinutes: number
  minimumConfidenceFilter: string
}

type NavaidType =
  | 'Vor'
  | 'Dvor'
  | 'Vortac'
  | 'VorDme'
  | 'IlsLocalizer'
  | 'IlsGlideslope'
  | 'MarkerBeacon'
  | 'Ndb'
  | 'Dme'

type Navaid = {
  identifier: string
  name: string
  type: NavaidType
  latitude: number
  longitude: number
  elevationFeet?: number
  frequencyMHz: number
  associatedAirport?: string
  distanceMilesFromCenter: number
  bearingDegreesFromCenter: number
  usageNotes?: string
}

type WefaxSyncState = 'Searching' | 'Locked' | 'Corrected' | 'Coasting' | 'Lost'

type WefaxDecoderState = {
  sourceId: string
  state: SourceState
  syncState: WefaxSyncState
  ioc: number
  lineRateRpm: number
  isInverted: boolean
  slantCorrection: number
  imageWidth: number
  linesReceived: number
  lastLineAtUtc?: string
  warnings: Array<{
    code: string
    message: string
    severity: string
  }>
}

type WefaxImageLine = {
  sourceId: string
  sequence: number
  generatedAtUtc: string
  lineNumber: number
  width: number
  syncState: WefaxSyncState
  confidence: number
  pixels: number[]
  toneMetrics: {
    startToneLevel: number
    stopToneLevel: number
    measuredAtUtc: string
  }
}

type WefaxLineBatch = {
  sourceId: string
  firstSequence: number
  generatedAtUtc: string
  lines: WefaxImageLine[]
  state: WefaxDecoderState
}

type StorageSnapshot = {
  rootPath: string
  messageCount: number
  aircraftTrackCount: number
  importDiagnosticCount: number
  bytesUsed: number
  checkedAtUtc: string
  positionSampleCount?: number
}

type HardwareSource = {
  id: string
  name: string
  state: SourceState
  adapterName: string
  inputKind: string
  ownerDecoderId?: string
  capabilities: {
    minimumSampleRateHz: number
    maximumSampleRateHz: number
    maximumBandwidthHz: number
    minimumFrequencyMHz: number
    maximumFrequencyMHz: number
    gainControls: string[]
    requiresExclusiveOwnership: boolean
    streamFraming: string
    acknowledgesTuneCommands: boolean
  }
  metrics: {
    sourceId: string
    state: SourceState
    configuredSampleRateHz: number
    configuredBandwidthHz: number
    tunedFrequencyMHz?: number
    buffersReceived: number
    droppedBuffers: number
    reconnectCount: number
    signalLevelDbfs?: number
    snrDb?: number
    queueDepth: number
    isClipping: boolean
    lastBufferAtUtc?: string
  }
  lastError?: string
}

type HardwareSourceDiagnostic = {
  id: string
  occurredAtUtc: string
  sourceId: string
  severity: string
  code: string
  message: string
}

type Dump1090ConnectionSnapshot = {
  importId: string
  state: SourceState
  host?: string
  port?: number
  jsonPath?: string
  pollIntervalSeconds: number
  acceptedRecords: number
  rejectedRecords: number
  lastPolledAtUtc?: string
  lastError?: string
}

type Dump1090FormState = {
  host: string
  port: number
  jsonPath: string
  pollIntervalSeconds: number
}

function App() {
  const [health, setHealth] = useState<HealthState>({ status: 'checking' })
  const [operatorDiagnostics, setOperatorDiagnostics] = useState<OperatorDiagnostics | null>(null)
  const [catalog, setCatalog] = useState<RuntimeCatalog>({ sources: [], decoders: [], imports: [] })
  const [importDiagnostics, setImportDiagnostics] = useState<ImportDiagnostic[]>([])
  const [externalProcesses, setExternalProcesses] = useState<ExternalDecoderProcess[]>([])
  const [externalDiagnostics, setExternalDiagnostics] = useState<ExternalDecoderDiagnostic[]>([])
  const [messages, setMessages] = useState<NormalizedAviationMessage[]>([])
  const [spectrumFrame, setSpectrumFrame] = useState<SpectrumFrame | null>(null)
  const [waterfallRows, setWaterfallRows] = useState<WaterfallRow[]>([])
  const [streamMetrics, setStreamMetrics] = useState<StreamMetrics | null>(null)
  const [aircraftTracks, setAircraftTracks] = useState<AircraftTrack[]>([])
  const [remoteIdObservations, setRemoteIdObservations] = useState<RemoteIdObservation[]>([])
  const [remoteIdImportState, setRemoteIdImportState] = useState('Idle')
  const [sondeTracks, setSondeTracks] = useState<SondeTrack[]>([])
  const [navaids, setNavaids] = useState<Navaid[]>([])
  const [selectedAircraftId, setSelectedAircraftId] = useState<string | null>(null)
  const [showFlightTrack, setShowFlightTrack] = useState(true)
  const [showRangeRings, setShowRangeRings] = useState(true)
  const [showNavaids, setShowNavaids] = useState(true)
  const [showClustering, setShowClustering] = useState(true)
  const [showLastHourOnly, setShowLastHourOnly] = useState(true)
  const [followSelectedAircraft, setFollowSelectedAircraft] = useState(false)
  const [classFilter, setClassFilter] = useState<AircraftClass[]>(ALL_AIRCRAFT_CLASSES)
  const [positionOnlyFilter, setPositionOnlyFilter] = useState(false)
  const [minAltitudeFilter, setMinAltitudeFilter] = useState('')
  const [maxAltitudeFilter, setMaxAltitudeFilter] = useState('')
  const [nowTick, setNowTick] = useState(() => Date.now())
  const [wefaxState, setWefaxState] = useState<WefaxDecoderState | null>(null)
  const [wefaxLines, setWefaxLines] = useState<WefaxImageLine[]>([])
  const [wefaxReplayState, setWefaxReplayState] = useState('Idle')
  const [wefaxInverted, setWefaxInverted] = useState(false)
  const [wefaxSlant, setWefaxSlant] = useState(0)
  const [storageSnapshot, setStorageSnapshot] = useState<StorageSnapshot | null>(null)
  const [storageActionState, setStorageActionState] = useState('Idle')
  const [storageHistory, setStorageHistory] = useState<string[]>([])
  const [hardwareSources, setHardwareSources] = useState<HardwareSource[]>([])
  const [hardwareDiagnostics, setHardwareDiagnostics] = useState<HardwareSourceDiagnostic[]>([])
  const [hardwareActionState, setHardwareActionState] = useState('Idle')
  const [dump1090Status, setDump1090Status] = useState<Dump1090ConnectionSnapshot | null>(null)
  const [dump1090Diagnostics, setDump1090Diagnostics] = useState<ImportDiagnostic[]>([])
  const [dump1090Form, setDump1090Form] = useState<Dump1090FormState>({ host: '', port: 8080, jsonPath: '/data/aircraft.json', pollIntervalSeconds: 5 })
  const [dump1090ActionState, setDump1090ActionState] = useState('Idle')
  const [feeders, setFeeders] = useState<FeederStatus[]>([])
  const [feederDiagnostics, setFeederDiagnostics] = useState<FeederDiagnostic[]>([])
  const [feederActionState, setFeederActionState] = useState('Idle')
  const [decoderSettings, setDecoderSettings] = useState<DecoderSettings | null>(null)
  const [settingsActionState, setSettingsActionState] = useState('Idle')
  const [isInitialLoad, setIsInitialLoad] = useState(true)
  const [activityLog, setActivityLog] = useState<string[]>([])
  const [selectedMessageId, setSelectedMessageId] = useState<string | null>(null)
  const [messageFilter, setMessageFilter] = useState('all')
  const [hubState, setHubState] = useState<HubConnectionState>(HubConnectionState.Disconnected)
  const [replayState, setReplayState] = useState('Idle')
  const [importState, setImportState] = useState('Idle')
  const [adsbImportState, setAdsbImportState] = useState('Idle')
  const [satcomImportState, setSatcomImportState] = useState('Idle')
  const [radiosondeImportState, setRadiosondeImportState] = useState('Idle')
  const [externalImportState, setExternalImportState] = useState('Idle')
  const [externalProcessState, setExternalProcessState] = useState('Idle')
  const [streamState, setStreamState] = useState('Idle')
  const filteredMessages = messages.filter((message) => {
    if (messageFilter === 'all') {
      return true
    }

    if (messageFilter === 'warnings') {
      return message.warnings.length > 0
    }

    return message.confidence === messageFilter
  })
  const selectedMessage = messages.find((message) => message.id === selectedMessageId) ?? filteredMessages[0]

  const mapTracks = useMemo(() => {
    const cutoff = nowTick - 60 * 60 * 1000
    const minAltitude = minAltitudeFilter === '' ? undefined : Number(minAltitudeFilter)
    const maxAltitude = maxAltitudeFilter === '' ? undefined : Number(maxAltitudeFilter)

    return aircraftTracks.filter((track) => {
      if (showLastHourOnly && new Date(track.updatedAtUtc).getTime() < cutoff) {
        return false
      }

      if (!classFilter.includes(classifyAircraft(track))) {
        return false
      }

      if (positionOnlyFilter && (track.latitude === undefined || track.longitude === undefined)) {
        return false
      }

      if (minAltitude !== undefined && (track.altitudeFeet === undefined || track.altitudeFeet < minAltitude)) {
        return false
      }

      if (maxAltitude !== undefined && (track.altitudeFeet === undefined || track.altitudeFeet > maxAltitude)) {
        return false
      }

      return true
    })
  }, [aircraftTracks, showLastHourOnly, nowTick, classFilter, positionOnlyFilter, minAltitudeFilter, maxAltitudeFilter])

  useEffect(() => {
    const interval = window.setInterval(() => setNowTick(Date.now()), 30000)
    return () => window.clearInterval(interval)
  }, [])

  function logActivity(message: string) {
    const timestamp = new Date().toLocaleTimeString()
    setActivityLog((current) => [`${timestamp} — ${message}`, ...current].slice(0, 8))
  }

  function toggleClassFilter(aircraftClass: AircraftClass) {
    setClassFilter((current) =>
      current.includes(aircraftClass) ? current.filter((item) => item !== aircraftClass) : [...current, aircraftClass],
    )
  }

  useEffect(() => {
    const controller = new AbortController()

    async function loadStartupState() {
      try {
        const [healthResponse, operatorResponse, sourcesResponse, decodersResponse, importsResponse, diagnosticsResponse, messagesResponse, streamMetricsResponse, aircraftResponse, remoteIdObservationsResponse, externalResponse, externalDiagnosticsResponse, wefaxResponse, storageResponse, hardwareSourcesResponse, hardwareDiagnosticsResponse, dump1090StatusResponse, dump1090DiagnosticsResponse, sondesResponse, navaidsResponse, feedersResponse, feederDiagnosticsResponse, settingsResponse] = await Promise.all([
          fetch('/api/health', { signal: controller.signal }),
          fetch('/api/diagnostics/operator', { signal: controller.signal }),
          fetch('/api/sources', { signal: controller.signal }),
          fetch('/api/decoders', { signal: controller.signal }),
          fetch('/api/imports', { signal: controller.signal }),
          fetch('/api/imports/diagnostics', { signal: controller.signal }),
          fetch('/api/messages/recent', { signal: controller.signal }),
          fetch('/api/streams/synthetic/metrics', { signal: controller.signal }),
          fetch('/api/aircraft', { signal: controller.signal }),
          fetch('/api/remote-id/observations?limit=100', { signal: controller.signal }),
          fetch('/api/external-decoders', { signal: controller.signal }),
          fetch('/api/external-decoders/diagnostics', { signal: controller.signal }),
          fetch('/api/wefax/state', { signal: controller.signal }),
          fetch('/api/storage', { signal: controller.signal }),
          fetch('/api/hardware-sources', { signal: controller.signal }),
          fetch('/api/hardware-sources/diagnostics', { signal: controller.signal }),
          fetch('/api/imports/dump1090/status', { signal: controller.signal }),
          fetch('/api/imports/dump1090/diagnostics', { signal: controller.signal }),
          fetch('/api/sondes', { signal: controller.signal }),
          fetch('/api/navaids?maxDistanceMiles=200', { signal: controller.signal }),
          fetch('/api/feeders', { signal: controller.signal }),
          fetch('/api/feeders/diagnostics', { signal: controller.signal }),
          fetch('/api/settings', { signal: controller.signal }),
        ])

        if (!healthResponse.ok) {
          throw new Error(`Health check failed with ${healthResponse.status}`)
        }

        setHealth({ status: 'online', data: (await healthResponse.json()) as HealthStatus })
        setOperatorDiagnostics(operatorResponse.ok ? ((await operatorResponse.json()) as OperatorDiagnostics) : null)
        setCatalog({
          sources: sourcesResponse.ok ? ((await sourcesResponse.json()) as RuntimeSource[]) : [],
          decoders: decodersResponse.ok ? ((await decodersResponse.json()) as DecoderState[]) : [],
          imports: importsResponse.ok ? ((await importsResponse.json()) as ImportState[]) : [],
        })
        setImportDiagnostics(diagnosticsResponse.ok ? ((await diagnosticsResponse.json()) as ImportDiagnostic[]) : [])
        setMessages(messagesResponse.ok ? sortMessages((await messagesResponse.json()) as NormalizedAviationMessage[]) : [])
        setStreamMetrics(streamMetricsResponse.ok ? ((await streamMetricsResponse.json()) as StreamMetrics) : null)
        setAircraftTracks(aircraftResponse.ok ? sortAircraftTracks((await aircraftResponse.json()) as AircraftTrack[]) : [])
        setRemoteIdObservations(remoteIdObservationsResponse.ok ? sortRemoteIdObservations((await remoteIdObservationsResponse.json()) as RemoteIdObservation[]) : [])
        setExternalProcesses(externalResponse.ok ? sortExternalProcesses((await externalResponse.json()) as ExternalDecoderProcess[]) : [])
        setExternalDiagnostics(externalDiagnosticsResponse.ok ? sortExternalDiagnostics((await externalDiagnosticsResponse.json()) as ExternalDecoderDiagnostic[]) : [])
        setWefaxState(wefaxResponse.ok ? ((await wefaxResponse.json()) as WefaxDecoderState) : null)
        setStorageSnapshot(storageResponse.ok ? ((await storageResponse.json()) as StorageSnapshot) : null)
        setHardwareSources(hardwareSourcesResponse.ok ? sortHardwareSources((await hardwareSourcesResponse.json()) as HardwareSource[]) : [])
        setHardwareDiagnostics(hardwareDiagnosticsResponse.ok ? sortHardwareDiagnostics((await hardwareDiagnosticsResponse.json()) as HardwareSourceDiagnostic[]) : [])
        setDump1090Status(dump1090StatusResponse.ok ? ((await dump1090StatusResponse.json()) as Dump1090ConnectionSnapshot) : null)
        setDump1090Diagnostics(dump1090DiagnosticsResponse.ok ? sortDiagnostics((await dump1090DiagnosticsResponse.json()) as ImportDiagnostic[]) : [])
        setSondeTracks(sondesResponse.ok ? sortSondeTracks((await sondesResponse.json()) as SondeTrack[]) : [])
        setNavaids(navaidsResponse.ok ? ((await navaidsResponse.json()) as Navaid[]) : [])
        setFeeders(feedersResponse.ok ? ((await feedersResponse.json()) as FeederStatus[]) : [])
        setFeederDiagnostics(feederDiagnosticsResponse.ok ? sortFeederDiagnostics((await feederDiagnosticsResponse.json()) as FeederDiagnostic[]) : [])
        setDecoderSettings(settingsResponse.ok ? ((await settingsResponse.json()) as DecoderSettings) : null)
      } catch (error) {
        if (!controller.signal.aborted) {
          setHealth({
            status: 'offline',
            message: error instanceof Error ? error.message : 'Backend is offline',
          })
        }
      } finally {
        if (!controller.signal.aborted) {
          setIsInitialLoad(false)
        }
      }
    }

    void loadStartupState()

    return () => controller.abort()
  }, [])

  useEffect(() => {
    let disposed = false
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/aerohub')
      .withAutomaticReconnect()
      .build()

    connection.on('message.snapshot', (snapshot: NormalizedAviationMessage[]) => {
      setMessages(sortMessages(snapshot))
    })

    connection.on('message.received', (message: NormalizedAviationMessage) => {
      setMessages((current) => sortMessages([message, ...current.filter((item) => item.id !== message.id)]).slice(0, 25))
    })

    connection.on('import.diagnostic.snapshot', (snapshot: ImportDiagnostic[]) => {
      setImportDiagnostics(sortDiagnostics(snapshot))
    })

    connection.on('import.diagnostic', (diagnostic: ImportDiagnostic) => {
      setImportDiagnostics((current) => sortDiagnostics([diagnostic, ...current.filter((item) => item.id !== diagnostic.id)]).slice(0, 25))
    })

    connection.on('import.updated', (updated: ImportState) => {
      setCatalog((current) => ({
        ...current,
        imports: [...current.imports.filter((item) => item.id !== updated.id), updated].sort((left, right) => left.id.localeCompare(right.id)),
      }))
    })

    connection.on('spectrum.frame', (frame: SpectrumFrame) => {
      setSpectrumFrame(frame)
    })

    connection.on('waterfall.rows', (rows: WaterfallRows) => {
      setWaterfallRows((current) => [...current, ...rows.rows].slice(-80))
    })

    connection.on('stream.metrics', (metrics: StreamMetrics) => {
      setStreamMetrics(metrics)
    })

    connection.on('aircraft.snapshot', (snapshot: AircraftTrack[]) => {
      setAircraftTracks(sortAircraftTracks(snapshot))
    })

    connection.on('aircraft.updated', (track: AircraftTrack) => {
      setAircraftTracks((current) => sortAircraftTracks([track, ...current.filter((item) => item.aircraftIdentifier !== track.aircraftIdentifier)]))
    })

    connection.on('remote-id.observation.snapshot', (snapshot: RemoteIdObservation[]) => {
      setRemoteIdObservations(sortRemoteIdObservations(snapshot))
    })

    connection.on('remote-id.observation', (observation: RemoteIdObservation) => {
      setRemoteIdObservations((current) => sortRemoteIdObservations([observation, ...current.filter((item) => item.id !== observation.id)]).slice(0, 100))
    })

    connection.on('sonde.snapshot', (snapshot: SondeTrack[]) => {
      setSondeTracks(sortSondeTracks(snapshot))
    })

    connection.on('sonde.updated', (track: SondeTrack) => {
      setSondeTracks((current) => sortSondeTracks([track, ...current.filter((item) => item.serial !== track.serial)]))
    })

    connection.on('external.snapshot', (snapshot: ExternalDecoderProcess[]) => {
      setExternalProcesses(sortExternalProcesses(snapshot))
    })

    connection.on('external.diagnostic.snapshot', (snapshot: ExternalDecoderDiagnostic[]) => {
      setExternalDiagnostics(sortExternalDiagnostics(snapshot))
    })

    connection.on('external.updated', (updated: ExternalDecoderProcess) => {
      setExternalProcesses((current) => sortExternalProcesses([...current.filter((item) => item.id !== updated.id), updated]))
    })

    connection.on('external.diagnostic', (diagnostic: ExternalDecoderDiagnostic) => {
      setExternalDiagnostics((current) => sortExternalDiagnostics([diagnostic, ...current.filter((item) => item.id !== diagnostic.id)]).slice(0, 25))
    })

    connection.on('wefax.state', (state: WefaxDecoderState) => {
      setWefaxState(state)
      setWefaxInverted(state.isInverted)
      setWefaxSlant(state.slantCorrection)
    })

    connection.on('wefax.lines', (batch: WefaxLineBatch) => {
      setWefaxState(batch.state)
      setWefaxLines((current) => [...current, ...batch.lines].slice(-140))
    })

    connection.on('hardware.snapshot', (snapshot: HardwareSource[]) => {
      setHardwareSources(sortHardwareSources(snapshot))
    })

    connection.on('hardware.updated', (updated: HardwareSource) => {
      setHardwareSources((current) => sortHardwareSources([...current.filter((item) => item.id !== updated.id), updated]))
    })

    connection.on('hardware.diagnostic.snapshot', (snapshot: HardwareSourceDiagnostic[]) => {
      setHardwareDiagnostics(sortHardwareDiagnostics(snapshot))
    })

    connection.on('hardware.diagnostic', (diagnostic: HardwareSourceDiagnostic) => {
      setHardwareDiagnostics((current) => sortHardwareDiagnostics([diagnostic, ...current.filter((item) => item.id !== diagnostic.id)]).slice(0, 25))
    })

    connection.on('dump1090.snapshot', (snapshot: Dump1090ConnectionSnapshot) => {
      setDump1090Status(snapshot)
    })

    connection.on('dump1090.updated', (snapshot: Dump1090ConnectionSnapshot) => {
      setDump1090Status(snapshot)
    })

    connection.on('dump1090.diagnostic.snapshot', (snapshot: ImportDiagnostic[]) => {
      setDump1090Diagnostics(sortDiagnostics(snapshot))
    })

    connection.on('dump1090.diagnostic', (diagnostic: ImportDiagnostic) => {
      setDump1090Diagnostics((current) => sortDiagnostics([diagnostic, ...current.filter((item) => item.id !== diagnostic.id)]).slice(0, 25))
    })

    connection.on('feeder.snapshot', (snapshot: FeederStatus[]) => {
      setFeeders(snapshot)
    })

    connection.on('feeder.updated', (updated: FeederStatus) => {
      setFeeders((current) => [...current.filter((item) => item.id !== updated.id), updated].sort((a, b) => a.id.localeCompare(b.id)))
    })

    connection.on('feeder.diagnostic.snapshot', (snapshot: FeederDiagnostic[]) => {
      setFeederDiagnostics(sortFeederDiagnostics(snapshot))
    })

    connection.on('feeder.diagnostic', (diagnostic: FeederDiagnostic) => {
      setFeederDiagnostics((current) => sortFeederDiagnostics([diagnostic, ...current.filter((item) => item.id !== diagnostic.id)]).slice(0, 25))
    })

    connection.on('settings.snapshot', (snapshot: DecoderSettings) => {
      setDecoderSettings(snapshot)
    })

    connection.on('settings.updated', (updated: DecoderSettings) => {
      setDecoderSettings(updated)
    })

    connection.onreconnecting(() => {
      if (!disposed) {
        setHubState(HubConnectionState.Reconnecting)
        logActivity('Live updates disconnected, reconnecting\u2026')
      }
    })
    connection.onreconnected(() => {
      if (!disposed) {
        setHubState(HubConnectionState.Connected)
        logActivity('Live updates reconnected')
      }
      void requestSnapshots()
    })
    connection.onclose(() => {
      if (!disposed) {
        setHubState(HubConnectionState.Disconnected)
      }
    })

    async function requestSnapshots() {
      await connection.invoke('GetRecentMessages', 50)
      await connection.invoke('GetRecentImportDiagnostics', 50)
      await connection.invoke('GetStreamMetrics')
      await connection.invoke('GetAircraftTracks')
      await connection.invoke('GetRemoteIdObservations', 100)
      await connection.invoke('GetSondeTracks')
      await connection.invoke('GetExternalDecoderProcesses')
      await connection.invoke('GetExternalDecoderDiagnostics', 50)
      await connection.invoke('GetWefaxState')
      await connection.invoke('GetHardwareSources')
      await connection.invoke('GetHardwareSourceDiagnostics', 50)
      await connection.invoke('GetDump1090Status')
      await connection.invoke('GetDump1090Diagnostics', 50)
      await connection.invoke('GetFeeders')
      await connection.invoke('GetFeederDiagnostics', 50)
      await connection.invoke('GetSettings')
    }

    async function start() {
      try {
        await connection.start()
        if (disposed) {
          await connection.stop()
          return
        }

        setHubState(connection.state)
        await requestSnapshots()
      } catch {
        if (!disposed) {
          setHubState(HubConnectionState.Disconnected)
        }
      }
    }

    const startHandle = setTimeout(() => {
      void start()
    }, 0)

    return () => {
      disposed = true
      clearTimeout(startHandle)
      void connection.stop()
    }
  }, [])

  async function replayAcarsFixture() {
    setReplayState('Publishing fixture')

    try {
      const response = await fetch('/api/fixtures/replay/acars', { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Replay failed with ${response.status}`)
      }

      const result = (await response.json()) as { fixtureName: string; publishedMessages: number }
      setReplayState(`${result.fixtureName}: ${result.publishedMessages} messages`)
      await refreshRecentMessages()
      await refreshStorageSnapshot()
    } catch (error) {
      setReplayState(error instanceof Error ? error.message : 'Replay failed')
    }
  }

  async function startNdjsonImport() {
    setImportState('Importing NDJSON')

    try {
      const response = await fetch('/api/imports/local-acars-ndjson/start', { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Import failed with ${response.status}`)
      }

      const result = (await response.json()) as { acceptedRecords: number; rejectedRecords: number }
      setImportState(`${result.acceptedRecords} accepted / ${result.rejectedRecords} quarantined`)
      await refreshRecentMessages()
      await refreshImports()
      await refreshImportDiagnostics()
      await refreshStorageSnapshot()
    } catch (error) {
      setImportState(error instanceof Error ? error.message : 'Import failed')
    }
  }

  async function startAdsbImport() {
    setAdsbImportState('Importing ADS-B')

    try {
      const response = await fetch('/api/imports/local-adsb-readsb-json/start', { method: 'POST' })

      if (!response.ok) {
        throw new Error(`ADS-B import failed with ${response.status}`)
      }

      const result = (await response.json()) as { acceptedRecords: number; rejectedRecords: number }
      setAdsbImportState(`${result.acceptedRecords} tracks / ${result.rejectedRecords} quarantined`)
      await refreshAircraftTracks()
      await refreshImports()
      await refreshImportDiagnostics()
      await refreshStorageSnapshot()
    } catch (error) {
      setAdsbImportState(error instanceof Error ? error.message : 'ADS-B import failed')
    }
  }

  async function startRemoteIdImport() {
    setRemoteIdImportState('Importing Remote ID reports')

    try {
      const response = await fetch('/api/imports/local-remote-id-json/start', { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Remote ID import failed with ${response.status}`)
      }

      const result = (await response.json()) as { acceptedRecords: number; rejectedRecords: number }
      setRemoteIdImportState(`${result.acceptedRecords} drone tracks / ${result.rejectedRecords} quarantined`)
      await refreshAircraftTracks()
      await refreshRemoteIdObservations()
      await refreshImports()
      await refreshImportDiagnostics()
      await refreshStorageSnapshot()
    } catch (error) {
      setRemoteIdImportState(error instanceof Error ? error.message : 'Remote ID import failed')
    }
  }

  async function startSatcomImport() {
    setSatcomImportState('Importing SATCOM')

    try {
      const response = await fetch('/api/imports/sample-satcom-json/start', { method: 'POST' })

      if (!response.ok) {
        throw new Error(`SATCOM import failed with ${response.status}`)
      }

      const result = (await response.json()) as { acceptedRecords: number; rejectedRecords: number }
      setSatcomImportState(`${result.acceptedRecords} accepted / ${result.rejectedRecords} quarantined`)
      await refreshRecentMessages()
      await refreshAircraftTracks()
      await refreshImports()
      await refreshImportDiagnostics()
      await refreshStorageSnapshot()
    } catch (error) {
      setSatcomImportState(error instanceof Error ? error.message : 'SATCOM import failed')
    }
  }

  async function startRadiosondeImport() {
    setRadiosondeImportState('Importing RadioSonde telemetry')

    try {
      const response = await fetch('/api/imports/local-radiosonde-json/start', { method: 'POST' })

      if (!response.ok) {
        throw new Error(`RadioSonde import failed with ${response.status}`)
      }

      const result = (await response.json()) as { acceptedRecords: number; rejectedRecords: number }
      setRadiosondeImportState(`${result.acceptedRecords} sondes / ${result.rejectedRecords} quarantined`)
      await refreshSondeTracks()
      await refreshImports()
      await refreshImportDiagnostics()
      await refreshStorageSnapshot()
    } catch (error) {
      setRadiosondeImportState(error instanceof Error ? error.message : 'RadioSonde import failed')
    }
  }

  async function startExternalImport(importId: string, label: string) {
    setExternalImportState(`Importing ${label}`)

    try {
      const response = await fetch(`/api/imports/${importId}/start`, { method: 'POST' })

      if (!response.ok) {
        throw new Error(`${label} import failed with ${response.status}`)
      }

      const result = (await response.json()) as { acceptedRecords: number; rejectedRecords: number }
      setExternalImportState(`${label}: ${result.acceptedRecords} accepted / ${result.rejectedRecords} quarantined`)
      await refreshRecentMessages()
      await refreshImports()
      await refreshImportDiagnostics()
      await refreshStorageSnapshot()
    } catch (error) {
      setExternalImportState(error instanceof Error ? error.message : `${label} import failed`)
    }
  }

  async function simulateExternalProcess(processId: string, scenario: string) {
    if ((scenario === 'crash' || scenario === 'restart-limit') && !window.confirm(`Simulate "${scenario}" for ${processId}? This will affect its restart count and health state.`)) {
      return
    }

    setExternalProcessState(`${processId}: ${scenario}`)

    try {
      const response = await fetch(`/api/external-decoders/${processId}/simulate/${scenario}`, { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Simulation failed with ${response.status}`)
      }

      const result = (await response.json()) as { processId: string; scenario: string; state: SourceState; restartCount: number }
      const message = `${result.processId}: ${result.state} after ${result.scenario}`
      setExternalProcessState(message)
      logActivity(`external process ${message}`)
      await refreshExternalProcesses()
      await refreshExternalDiagnostics()
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Simulation failed'
      setExternalProcessState(message)
      logActivity(`external process simulation failed: ${message}`)
    }
  }

  async function replaySyntheticStream(speedMultiplier: number) {
    setStreamState(`Replaying ${speedMultiplier}x`)

    try {
      const response = await fetch(`/api/streams/synthetic/replay?speedMultiplier=${speedMultiplier}`, { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Synthetic replay failed with ${response.status}`)
      }

      const result = (await response.json()) as { spectrumFramesProduced: number; waterfallRowsProduced: number }
      setStreamState(`${speedMultiplier}x replay: ${result.spectrumFramesProduced} spectrum / ${result.waterfallRowsProduced} waterfall`)
      await refreshStreamMetrics()
    } catch (error) {
      setStreamState(error instanceof Error ? error.message : 'Synthetic replay failed')
    }
  }

  async function replayWefax(partial: boolean) {
    setWefaxReplayState(partial ? 'Replaying partial WEFAX' : 'Replaying WEFAX')
    setWefaxLines([])

    try {
      const response = await fetch(`/api/wefax/replay?lineCount=${partial ? 48 : 96}&partial=${partial}`, { method: 'POST' })

      if (!response.ok) {
        throw new Error(`WEFAX replay failed with ${response.status}`)
      }

      const result = (await response.json()) as { linesPublished: number; finalSyncState: WefaxSyncState; isPartial: boolean }
      setWefaxReplayState(`${result.linesPublished} lines / ${result.finalSyncState}${result.isPartial ? ' / partial' : ''}`)
      await refreshWefaxState()
    } catch (error) {
      setWefaxReplayState(error instanceof Error ? error.message : 'WEFAX replay failed')
    }
  }

  async function updateWefaxControls(isInverted: boolean, slantCorrection: number) {
    setWefaxInverted(isInverted)
    setWefaxSlant(slantCorrection)

    const response = await fetch(`/api/wefax/controls?isInverted=${isInverted}&slantCorrection=${slantCorrection}`, { method: 'POST' })

    if (response.ok) {
      setWefaxState((await response.json()) as WefaxDecoderState)
    }
  }

  async function refreshFeeders() {
    const response = await fetch('/api/feeders')

    if (response.ok) {
      setFeeders((await response.json()) as FeederStatus[])
    }
  }

  async function refreshFeederDiagnostics() {
    const response = await fetch('/api/feeders/diagnostics')

    if (response.ok) {
      setFeederDiagnostics(sortFeederDiagnostics((await response.json()) as FeederDiagnostic[]))
    }
  }

  async function updateDecoderSettings(newSettings: DecoderSettings) {
    setSettingsActionState('Saving settings\u2026')

    try {
      const response = await fetch('/api/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(newSettings),
      })

      if (!response.ok) {
        throw new Error(`Failed to save settings: ${response.status}`)
      }

      const saved = (await response.json()) as DecoderSettings
      setDecoderSettings(saved)
      setSettingsActionState('Settings saved')
      logActivity('Decoder settings updated & persisted')
      await refreshSettings()
    } catch (error) {
      setSettingsActionState(error instanceof Error ? error.message : 'Save failed')
    }
  }

  async function refreshSettings() {
    const response = await fetch('/api/settings')

    if (response.ok) {
      setDecoderSettings((await response.json()) as DecoderSettings)
    }
  }

  async function startFeeder(feederId: string) {
    setFeederActionState(`Starting ${feederId}`)

    try {
      const response = await fetch('/api/feeders/start', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ feederId }),
      })

      if (!response.ok) {
        throw new Error(`Feeder start failed with ${response.status}`)
      }

      setFeederActionState('Feeder started')
      await refreshFeeders()
      await refreshFeederDiagnostics()
    } catch (error) {
      setFeederActionState(error instanceof Error ? error.message : 'Feeder start failed')
    }
  }

  async function stopFeeder(feederId: string) {
    setFeederActionState(`Stopping ${feederId}`)

    try {
      const response = await fetch(`/api/feeders/${feederId}/stop`, { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Feeder stop failed with ${response.status}`)
      }

      setFeederActionState('Feeder stopped')
      await refreshFeeders()
      await refreshFeederDiagnostics()
    } catch (error) {
      setFeederActionState(error instanceof Error ? error.message : 'Feeder stop failed')
    }
  }

  async function sendFeederTest(feederId: string) {
    setFeederActionState(`Sending test frame to ${feederId}`)

    try {
      const response = await fetch(`/api/feeders/${feederId}/test`, { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Feeder test failed with ${response.status}`)
      }

      setFeederActionState('Test frame sent')
      await refreshFeeders()
      await refreshFeederDiagnostics()
    } catch (error) {
      setFeederActionState(error instanceof Error ? error.message : 'Feeder test failed')
    }
  }

  async function refreshRecentMessages() {
    const response = await fetch('/api/messages/recent')

    if (response.ok) {
      setMessages(sortMessages((await response.json()) as NormalizedAviationMessage[]))
    }
  }

  async function connectDump1090() {
    if (!dump1090Form.host.trim()) {
      setDump1090ActionState('A host is required.')
      return
    }

    setDump1090ActionState(`Connecting to ${dump1090Form.host}:${dump1090Form.port}\u2026`)

    try {
      const response = await fetch('/api/imports/dump1090/connect', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(dump1090Form),
      })

      if (!response.ok) {
        throw new Error(`Connect failed with ${response.status}`)
      }

      const result = (await response.json()) as Dump1090ConnectionSnapshot
      setDump1090Status(result)
      const message = `Connected to ${result.host}:${result.port}${result.jsonPath ?? ''}`
      setDump1090ActionState(message)
      logActivity(`dump1090: ${message}`)
      await refreshDump1090Diagnostics()
    } catch (error) {
      const message = error instanceof Error ? error.message : 'dump1090 connect failed'
      setDump1090ActionState(message)
      logActivity(`dump1090 connect failed: ${message}`)
    }
  }

  async function disconnectDump1090() {
    setDump1090ActionState('Disconnecting\u2026')

    try {
      const response = await fetch('/api/imports/dump1090/disconnect', { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Disconnect failed with ${response.status}`)
      }

      const result = (await response.json()) as Dump1090ConnectionSnapshot
      setDump1090Status(result)
      setDump1090ActionState('Disconnected')
      logActivity('dump1090: disconnected')
    } catch (error) {
      const message = error instanceof Error ? error.message : 'dump1090 disconnect failed'
      setDump1090ActionState(message)
      logActivity(`dump1090 disconnect failed: ${message}`)
    }
  }

  async function refreshDump1090Diagnostics() {
    const response = await fetch('/api/imports/dump1090/diagnostics')

    if (response.ok) {
      setDump1090Diagnostics(sortDiagnostics((await response.json()) as ImportDiagnostic[]))
    }
  }

  async function refreshImports() {
    const response = await fetch('/api/imports')

    if (response.ok) {
      const imports = (await response.json()) as ImportState[]
      setCatalog((current) => ({ ...current, imports }))
    }
  }

  async function refreshImportDiagnostics() {
    const response = await fetch('/api/imports/diagnostics')

    if (response.ok) {
      setImportDiagnostics(sortDiagnostics((await response.json()) as ImportDiagnostic[]))
    }
  }

  async function refreshStreamMetrics() {
    const response = await fetch('/api/streams/synthetic/metrics')

    if (response.ok) {
      setStreamMetrics((await response.json()) as StreamMetrics)
    }
  }

  async function refreshAircraftTracks() {
    const response = await fetch('/api/aircraft')

    if (response.ok) {
      setAircraftTracks(sortAircraftTracks((await response.json()) as AircraftTrack[]))
    }
  }

  async function refreshRemoteIdObservations() {
    const response = await fetch('/api/remote-id/observations?limit=100')

    if (response.ok) {
      setRemoteIdObservations(sortRemoteIdObservations((await response.json()) as RemoteIdObservation[]))
    }
  }

  async function refreshSondeTracks() {
    const response = await fetch('/api/sondes')

    if (response.ok) {
      setSondeTracks(sortSondeTracks((await response.json()) as SondeTrack[]))
    }
  }

  async function refreshExternalProcesses() {
    const response = await fetch('/api/external-decoders')

    if (response.ok) {
      setExternalProcesses(sortExternalProcesses((await response.json()) as ExternalDecoderProcess[]))
    }
  }

  async function refreshExternalDiagnostics() {
    const response = await fetch('/api/external-decoders/diagnostics')

    if (response.ok) {
      setExternalDiagnostics(sortExternalDiagnostics((await response.json()) as ExternalDecoderDiagnostic[]))
    }
  }

  async function refreshWefaxState() {
    const response = await fetch('/api/wefax/state')

    if (response.ok) {
      setWefaxState((await response.json()) as WefaxDecoderState)
    }
  }

  async function refreshStorageSnapshot() {
    const response = await fetch('/api/storage')

    if (response.ok) {
      setStorageSnapshot((await response.json()) as StorageSnapshot)
    }

    await refreshOperatorDiagnostics()
  }

  async function refreshOperatorDiagnostics() {
    const response = await fetch('/api/diagnostics/operator')

    if (response.ok) {
      setOperatorDiagnostics((await response.json()) as OperatorDiagnostics)
    }
  }

  async function refreshHardwareSources() {
    const response = await fetch('/api/hardware-sources')

    if (response.ok) {
      setHardwareSources(sortHardwareSources((await response.json()) as HardwareSource[]))
    }
  }

  async function refreshHardwareDiagnostics() {
    const response = await fetch('/api/hardware-sources/diagnostics')

    if (response.ok) {
      setHardwareDiagnostics(sortHardwareDiagnostics((await response.json()) as HardwareSourceDiagnostic[]))
    }
  }

  async function startHardwareSource(source: HardwareSource) {
    setHardwareActionState(`Starting ${source.name}`)

    const sampleRateHz = source.id === 'rtl-tcp-local' ? 1024000 : 48000
    const bandwidthHz = source.id === 'rtl-tcp-local' ? 200000 : 24000
    const frequencyMHz = source.id === 'rtl-tcp-local' ? 136.8 : 136.8

    try {
      const response = await fetch('/api/hardware-sources/start', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          sourceId: source.id,
          decoderId: source.id === 'rtl-tcp-local' ? 'wideband-rf-monitor' : 'file-audio-acars',
          sampleRateHz,
          bandwidthHz,
          frequencyMHz,
          requiresExclusiveOwnership: true,
        }),
      })

      if (!response.ok) {
        throw new Error(`Source start failed with ${response.status}`)
      }

      const updated = (await response.json()) as HardwareSource
      setHardwareActionState(`${updated.name}: ${updated.state}`)
      await refreshHardwareSources()
      await refreshHardwareDiagnostics()
    } catch (error) {
      setHardwareActionState(error instanceof Error ? error.message : 'Source start failed')
    }
  }

  async function stopHardwareSource(sourceId: string) {
    setHardwareActionState(`Stopping ${sourceId}`)

    try {
      const response = await fetch(`/api/hardware-sources/${sourceId}/stop`, { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Source stop failed with ${response.status}`)
      }

      const updated = (await response.json()) as HardwareSource
      setHardwareActionState(`${updated.name}: ${updated.state}`)
      await refreshHardwareSources()
      await refreshHardwareDiagnostics()
    } catch (error) {
      setHardwareActionState(error instanceof Error ? error.message : 'Source stop failed')
    }
  }

  async function simulateHardwareSource(sourceId: string, scenario: string) {
    if (scenario === 'failure' && !window.confirm(`Simulate a failure for ${sourceId}? This will mark the source degraded/offline.`)) {
      return
    }

    setHardwareActionState(`${sourceId}: ${scenario}`)

    try {
      const response = await fetch(`/api/hardware-sources/${sourceId}/simulate/${scenario}`, { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Source simulation failed with ${response.status}`)
      }

      const result = (await response.json()) as { sourceId: string; scenario: string; state: SourceState }
      const message = `${result.sourceId}: ${result.state} after ${result.scenario}`
      setHardwareActionState(message)
      logActivity(`hardware source ${message}`)
      await refreshHardwareSources()
      await refreshHardwareDiagnostics()
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Source simulation failed'
      setHardwareActionState(message)
      logActivity(`hardware source simulation failed: ${message}`)
    }
  }

  async function exportStorageSnapshot() {
    setStorageActionState('Exporting snapshot')

    try {
      const response = await fetch('/api/storage/export?exportName=manual-snapshot', { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Storage export failed with ${response.status}`)
      }

      const result = (await response.json()) as { messagesExported: number; tracksExported: number; diagnosticsExported: number }
      recordStorageAction(`${result.messagesExported} messages / ${result.tracksExported} tracks / ${result.diagnosticsExported} diagnostics exported`)
      await refreshStorageSnapshot()
    } catch (error) {
      setStorageActionState(error instanceof Error ? error.message : 'Storage export failed')
    }
  }

  async function applyStorageRetention() {
    if (!window.confirm('Apply retention now? This permanently deletes messages, tracks, and diagnostics older than the configured window.')) {
      return
    }

    setStorageActionState('Applying retention')

    try {
      const response = await fetch('/api/storage/retention/apply?messageDays=30&trackDays=30&diagnosticDays=14', { method: 'POST' })

      if (!response.ok) {
        throw new Error(`Retention failed with ${response.status}`)
      }

      const result = (await response.json()) as { messagesRemoved: number; tracksRemoved: number; diagnosticsRemoved: number }
      recordStorageAction(`${result.messagesRemoved} messages / ${result.tracksRemoved} tracks / ${result.diagnosticsRemoved} diagnostics removed`)
      await refreshStorageSnapshot()
    } catch (error) {
      setStorageActionState(error instanceof Error ? error.message : 'Retention failed')
    }
  }

  function recordStorageAction(message: string) {
    setStorageActionState(message)
    setStorageHistory((current) => [message, ...current].slice(0, 4))
    logActivity(`storage: ${message}`)
  }

  return (
    <main className="shell">
      <aside className="sidebar" aria-label="AeroHub sections">
        <div className="brand">
          <span className="brand-mark">AH</span>
          <div>
            <strong>AeroHub</strong>
            <span>Aviation SDR console</span>
          </div>
        </div>

        <nav>
          <a href="#live-rf">Live RF</a>
          <a href="#messages">Messages</a>
          <a href="#map">Map</a>
          <a href="#aircraft">Aircraft</a>
          <a href="#decoders">Decoders</a>
          <a href="#imports">Imports</a>
          <a href="#wefax">WEFAX</a>
          <a href="#settings">Settings</a>
        </nav>

        <ActivityLog entries={activityLog} />
      </aside>

      <section className="workspace">
        <header className="topbar">
          <div>
            <p className="eyebrow">Sprint 11 hardware sources</p>
            <h1>Live input adapters, ownership, and source health</h1>
          </div>
          <div className="topbar-status">
            <BackendStatus health={health} />
            <HubStatusBadge state={hubState} />
          </div>
        </header>

        <article className="panel map-panel" id="map" aria-label="Live aircraft map">
          <div className="panel-heading">
            <h2>Map</h2>
            <span>
              {mapTracks.filter((track) => track.latitude !== undefined && track.longitude !== undefined).length} positioned / {mapTracks.length} tracks
            </span>
          </div>
          <div className="map-toggles">
            <label className="flight-track-toggle">
              <input type="checkbox" checked={showFlightTrack} onChange={(event) => setShowFlightTrack(event.target.checked)} />
              Show flight track for selected aircraft
            </label>
            <label className="flight-track-toggle">
              <input type="checkbox" checked={showRangeRings} onChange={(event) => setShowRangeRings(event.target.checked)} />
              Show range rings (50 mi intervals)
            </label>
            <label className="flight-track-toggle">
              <input type="checkbox" checked={showNavaids} onChange={(event) => setShowNavaids(event.target.checked)} />
              Show radio navaids ({navaids.length})
            </label>
            <label className="flight-track-toggle">
              <input type="checkbox" checked={showClustering} onChange={(event) => setShowClustering(event.target.checked)} />
              Cluster markers when zoomed out
            </label>
            <label className="flight-track-toggle">
              <input type="checkbox" checked={showLastHourOnly} onChange={(event) => setShowLastHourOnly(event.target.checked)} />
              Only show last hour of traffic
            </label>
            <label className="flight-track-toggle">
              <input type="checkbox" checked={followSelectedAircraft} onChange={(event) => setFollowSelectedAircraft(event.target.checked)} />
              Follow selected aircraft
            </label>
            <label className="flight-track-toggle">
              <input type="checkbox" checked={positionOnlyFilter} onChange={(event) => setPositionOnlyFilter(event.target.checked)} />
              Position only
            </label>
          </div>
          <div className="map-filters">
            <div className="map-class-filters" aria-label="Filter by aircraft class">
              {ALL_AIRCRAFT_CLASSES.map((aircraftClass) => (
                <label key={aircraftClass} className="map-class-filter-toggle">
                  <input
                    type="checkbox"
                    checked={classFilter.includes(aircraftClass)}
                    onChange={() => toggleClassFilter(aircraftClass)}
                  />
                  <span className="aircraft-class-dot" style={{ backgroundColor: AIRCRAFT_CLASS_COLORS[aircraftClass] }} />
                  {aircraftClass}
                </label>
              ))}
            </div>
            <label className="map-altitude-filter">
              Alt min (ft)
              <input type="number" value={minAltitudeFilter} onChange={(event) => setMinAltitudeFilter(event.target.value)} placeholder="0" />
            </label>
            <label className="map-altitude-filter">
              Alt max (ft)
              <input type="number" value={maxAltitudeFilter} onChange={(event) => setMaxAltitudeFilter(event.target.value)} placeholder="45000" />
            </label>
            <button className="action compact-action" type="button" onClick={() => exportTracksAsCsv(mapTracks)}>
              Export (CSV)
            </button>
            <button className="action compact-action" type="button" onClick={() => exportTracksAsGeoJson(mapTracks)}>
              Export (GeoJSON)
            </button>
          </div>
          <div className="map-panel-body">
            <AircraftMap
              tracks={mapTracks}
              sondes={sondeTracks}
              navaids={navaids}
              selectedAircraftId={selectedAircraftId}
              onSelectAircraft={setSelectedAircraftId}
              showFlightTrack={showFlightTrack}
              showRangeRings={showRangeRings}
              showNavaids={showNavaids}
              showClustering={showClustering}
              followSelectedAircraft={followSelectedAircraft}
            />
            <AircraftListPanel tracks={mapTracks} selectedAircraftId={selectedAircraftId} onSelectAircraft={setSelectedAircraftId} />
          </div>
        </article>

        <section className="grid" aria-label="AeroHub overview">
          <article className="panel spectrum" id="live-rf">
            <div className="panel-heading">
              <h2>Live RF</h2>
              <span>{streamMetrics?.state ?? 'Offline'}</span>
            </div>
            <div className="stream-actions" aria-label="Synthetic RF replay controls">
              {[1, 5, 20].map((speedMultiplier) => (
                <button key={speedMultiplier} className="action compact-action" type="button" onClick={() => replaySyntheticStream(speedMultiplier)}>
                  Replay {speedMultiplier}x
                </button>
              ))}
            </div>
            <p className="panel-copy status-line">{streamState}</p>
            <SpectrumCanvas frame={spectrumFrame} />
            <WaterfallCanvas rows={waterfallRows} />
            <HardwareSourcePanel
              sources={hardwareSources}
              diagnostics={hardwareDiagnostics}
              actionState={hardwareActionState}
              onStart={startHardwareSource}
              onStop={stopHardwareSource}
              onSimulate={simulateHardwareSource}
            />
            <FeederPanel
              feeders={feeders}
              diagnostics={feederDiagnostics}
              actionState={feederActionState}
              onStart={startFeeder}
              onStop={stopFeeder}
              onTest={sendFeederTest}
            />
          </article>

          <article className="panel" id="messages">
            <div className="panel-heading">
              <h2>Messages</h2>
              <span>{filteredMessages.length} shown / {messages.length} recent</span>
            </div>
            <button className="action" type="button" onClick={replayAcarsFixture}>
              Replay ACARS Fixture
            </button>
            <p className="panel-copy status-line">{replayState}</p>
            <label className="filter">
              <span>Filter</span>
              <select value={messageFilter} onChange={(event) => setMessageFilter(event.target.value)}>
                <option value="all">All Messages</option>
                <option value="Confirmed">Confirmed</option>
                <option value="Candidate">Candidate</option>
                <option value="Observed">Observed</option>
                <option value="Unknown">Unknown</option>
                <option value="warnings">Warnings</option>
              </select>
            </label>
            <MessageList messages={filteredMessages} selectedMessageId={selectedMessage?.id} onSelect={setSelectedMessageId} isLoading={isInitialLoad} />
          </article>

          <article className="panel" id="aircraft">
            <div className="panel-heading">
              <h2>Aircraft</h2>
              <span>{aircraftTracks.length} tracks</span>
            </div>
            <dl className="metrics">
              <div>
                <dt>Sources</dt>
                <dd>{catalog.sources.length}</dd>
              </div>
              <div>
                <dt>Decoders</dt>
                <dd>{operatorDiagnostics?.activeDecoders ?? catalog.decoders.length}</dd>
              </div>
              <div>
                <dt>Imports</dt>
                <dd>{operatorDiagnostics?.activeImports ?? catalog.imports.length}</dd>
              </div>
              <div>
                <dt>Spectrum frames</dt>
                <dd>{streamMetrics?.spectrumFramesProduced ?? 0}</dd>
              </div>
              <div>
                <dt>Waterfall rows</dt>
                <dd>{streamMetrics?.waterfallRowsProduced ?? 0}</dd>
              </div>
              <div>
                <dt>Queue depth</dt>
                <dd>{operatorDiagnostics?.streamQueueDepth ?? streamMetrics?.queueDepth ?? 0}</dd>
              </div>
              <div>
                <dt>Warnings</dt>
                <dd>{operatorDiagnostics?.activeWarnings ?? importDiagnostics.length}</dd>
              </div>
              <div>
                <dt>Aircraft tracks</dt>
                <dd>{aircraftTracks.length}</dd>
              </div>
              <div>
                <dt>External processes</dt>
                <dd>{externalProcesses.length}</dd>
              </div>
              <div>
                <dt>Stored messages</dt>
                <dd>{storageSnapshot?.messageCount ?? 0}</dd>
              </div>
              <div>
                <dt>Stored tracks</dt>
                <dd>{storageSnapshot?.aircraftTrackCount ?? 0}</dd>
              </div>
              <div>
                <dt>Cached positions</dt>
                <dd>{storageSnapshot?.positionSampleCount ?? 0}</dd>
              </div>
              <div>
                <dt>Storage bytes</dt>
                <dd>{formatBytes(operatorDiagnostics?.storageBytesUsed ?? storageSnapshot?.bytesUsed ?? 0)}</dd>
              </div>
            </dl>
            <div className="storage-actions" aria-label="Storage actions">
              <button className="action compact-action" type="button" onClick={refreshStorageSnapshot}>
                Refresh Storage
              </button>
              <button className="action compact-action" type="button" onClick={exportStorageSnapshot}>
                Export Snapshot
              </button>
              <button className="action compact-action" type="button" onClick={applyStorageRetention}>
                Apply Retention
              </button>
            </div>
            <p className="panel-copy status-line">{storageActionState}</p>
            {operatorDiagnostics ? (
              <div className="panel-copy" aria-label="Operator health summary">
                <strong>{operatorDiagnostics.systemHealth}</strong>
                <span> / {operatorDiagnostics.environment} / {operatorDiagnostics.version}</span>
                {operatorDiagnostics.recentWarnings.length > 0 ? (
                  <ul className="status-list">
                    {operatorDiagnostics.recentWarnings.map((warning) => <li key={warning}>{warning}</li>)}
                  </ul>
                ) : null}
              </div>
            ) : null}
            <StorageHistory entries={storageHistory} />
            <Dump1090Panel
              status={dump1090Status}
              diagnostics={dump1090Diagnostics}
              form={dump1090Form}
              actionState={dump1090ActionState}
              onFormChange={setDump1090Form}
              onConnect={connectDump1090}
              onDisconnect={disconnectDump1090}
            />
            <p className="panel-copy status-line">
              See the full-screen <a href="#map">Map</a> section for live aircraft positions.
            </p>
            <AircraftTrackTable tracks={aircraftTracks} isLoading={isInitialLoad} />
          </article>

          <article className="panel" id="decoders">
            <div className="panel-heading">
              <h2>Message Detail</h2>
              <span>{selectedMessage?.acars?.label ?? selectedMessage?.kind ?? 'No message selected'}</span>
            </div>
            <MessageDetail message={selectedMessage} />
          </article>

          <article className="panel" id="imports">
            <div className="panel-heading">
              <h2>Imports</h2>
              <span>{importDiagnostics.length} diagnostics</span>
            </div>
            <p className="panel-copy">
              Native decoder output and imported app output will share the same normalized message and aircraft-track pipeline.
            </p>
            <button className="action secondary" type="button" onClick={startNdjsonImport}>
              Start NDJSON Import
            </button>
            <p className="panel-copy status-line">{importState}</p>
            <button className="action secondary" type="button" onClick={startAdsbImport}>
              Start ADS-B Import
            </button>
            <p className="panel-copy status-line">{adsbImportState}</p>
            <button className="action secondary" type="button" onClick={startSatcomImport}>
              Start SATCOM Import
            </button>
            <p className="panel-copy status-line">{satcomImportState}</p>
            <button className="action secondary" type="button" onClick={startRadiosondeImport}>
              Start RadioSonde Import
            </button>
            <p className="panel-copy status-line">{radiosondeImportState}</p>
            <div className="split-actions">
              <button className="action secondary" type="button" onClick={() => startExternalImport('sample-dumphfdl-json', 'dumphfdl')}>
                Import dumphfdl
              </button>
              <button className="action secondary" type="button" onClick={() => startExternalImport('sample-dumpvdl2-json', 'dumpvdl2')}>
                Import dumpvdl2
              </button>
            </div>
            <p className="panel-copy status-line">{externalImportState}</p>
            <ul className="status-list">
              {catalog.imports.map((importSource) => (
                <li key={importSource.id}>
                  <strong>{importSource.name}</strong>
                  <span>{importSource.sourceFormat} / {importSource.state}</span>
                  <span>{importSource.acceptedRecords} accepted / {importSource.rejectedRecords} quarantined</span>
                  {importSource.lastError ? <span>{importSource.lastError}</span> : null}
                </li>
              ))}
            </ul>
            <ImportDiagnostics diagnostics={importDiagnostics} />
            <ExternalDecoderPanel processes={externalProcesses} diagnostics={externalDiagnostics} stateText={externalProcessState} onSimulate={simulateExternalProcess} />
          </article>

          <RadioSondePanel sondes={sondeTracks} actionState={radiosondeImportState} onStartImport={startRadiosondeImport} />

          <CommercialDronePanel tracks={aircraftTracks} observations={remoteIdObservations} actionState={remoteIdImportState} onStartImport={startRemoteIdImport} />

          <article className="panel wefax-panel" id="wefax">
            <div className="panel-heading">
              <h2>WEFAX</h2>
              <span>{wefaxState?.syncState ?? 'Searching'}</span>
            </div>
            <div className="stream-actions" aria-label="WEFAX replay controls">
              <button className="action compact-action" type="button" onClick={() => replayWefax(false)}>
                Replay WEFAX
              </button>
              <button className="action compact-action" type="button" onClick={() => replayWefax(true)}>
                Replay Partial
              </button>
            </div>
            <p className="panel-copy status-line">{wefaxReplayState}</p>
            <div className="wefax-controls">
              <label>
                <input type="checkbox" checked={wefaxInverted} onChange={(event) => updateWefaxControls(event.target.checked, wefaxSlant)} />
                Invert
              </label>
              <label>
                Slant
                <input type="range" min="-3" max="3" step="0.5" value={wefaxSlant} onChange={(event) => updateWefaxControls(wefaxInverted, Number(event.target.value))} />
              </label>
              <button className="action compact-action" type="button" onClick={() => updateWefaxControls(false, 0)}>
                Resync
              </button>
            </div>
            <WefaxCanvas lines={wefaxLines} />
            <WefaxStatus state={wefaxState} />
          </article>

          <DecoderSettingsPanel settings={decoderSettings} actionState={settingsActionState} onSave={updateDecoderSettings} />
        </section>
      </section>
    </main>
  )
}

function StorageHistory({ entries }: { entries: string[] }) {
  if (entries.length === 0) {
    return null
  }

  return (
    <ol className="storage-history" aria-label="Storage action history">
      {entries.map((entry) => (
        <li key={entry}>{entry}</li>
      ))}
    </ol>
  )
}

function MessageList({
  messages,
  selectedMessageId,
  onSelect,
  isLoading,
}: {
  messages: NormalizedAviationMessage[]
  selectedMessageId?: string
  onSelect: (messageId: string) => void
  isLoading: boolean
}) {
  const [copiedMessageId, setCopiedMessageId] = useState<string | null>(null)

  if (messages.length === 0) {
    return <p className="empty-state">{isLoading ? 'Loading messages\u2026' : 'No matching messages. Replay the ACARS fixture or change the filter.'}</p>
  }

  async function handleCopyRaw(event: React.MouseEvent, message: NormalizedAviationMessage) {
    event.stopPropagation()
    const copied = await copyToClipboard(message.rawPayload)
    setCopiedMessageId(copied ? message.id : null)
    window.setTimeout(() => setCopiedMessageId((current) => (current === message.id ? null : current)), 1500)
  }

  return (
    <ol className="message-list" aria-label="Recent aviation messages">
      {messages.map((message) => (
        <li key={message.id}>
          <div className={message.id === selectedMessageId ? 'message-card selected' : 'message-card'}>
            <button className="message-card-select" type="button" onClick={() => onSelect(message.id)}>
              <div>
                <strong>{message.acars?.label ?? message.kind}</strong>
                <span>#{message.sequence} / {message.confidence}</span>
              </div>
              <p>{message.summary}</p>
              <small>
                {message.aircraftIdentifier ?? 'Unknown aircraft'} / {message.transport ?? 'Unknown transport'} / {message.frequencyMHz?.toFixed(3) ?? 'n/a'} MHz / {message.provenance.path} / {formatTime(message.receivedAtUtc)}
              </small>
            </button>
            <button
              type="button"
              className="copy-raw-button"
              onClick={(event) => handleCopyRaw(event, message)}
              aria-label={`Copy raw payload for message ${message.sequence}`}
            >
              {copiedMessageId === message.id ? 'Copied' : 'Copy raw'}
            </button>
          </div>
        </li>
      ))}
    </ol>
  )
}

function MessageDetail({ message }: { message?: NormalizedAviationMessage }) {
  const [copyState, setCopyState] = useState('Copy raw payload')

  if (!message) {
    return <p className="empty-state">Select an ACARS message to inspect parsed fields and raw payload.</p>
  }

  const rawPayload = message.rawPayload

  async function handleCopyRawPayload() {
    const copied = await copyToClipboard(rawPayload)
    setCopyState(copied ? 'Copied to clipboard' : 'Copy failed')
    window.setTimeout(() => setCopyState('Copy raw payload'), 1500)
  }

  return (
    <div className="detail">
      <dl className="metrics compact">
        <div>
          <dt>Label</dt>
          <dd>{message.acars?.label ?? 'n/a'}</dd>
        </div>
        <div>
          <dt>Sublabel</dt>
          <dd>{message.acars?.sublabel ?? message.acars?.preamble ?? 'n/a'}</dd>
        </div>
        <div>
          <dt>Category</dt>
          <dd>{message.acars?.applicationCategory ?? 'Unknown'}</dd>
        </div>
        <div>
          <dt>Aircraft</dt>
          <dd>{message.aircraftIdentifier ?? message.datalink?.aircraftIdentifier ?? 'Unknown'}</dd>
        </div>
      </dl>
      {message.datalink ? <DatalinkDetail message={message} /> : null}
      <div className="raw-payload-heading">
        <span>Raw payload</span>
        <button type="button" className="copy-raw-button" onClick={handleCopyRawPayload}>
          {copyState}
        </button>
      </div>
      <pre>{rawPayload}</pre>
      {message.warnings.length > 0 ? (
        <ul className="warnings">
          {message.warnings.map((warning) => (
            <li key={warning.code}>
              <strong>{warning.code}</strong>
              <span>{warning.message}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-state">No parser warnings.</p>
      )}
    </div>
  )
}

function DatalinkDetail({ message }: { message: NormalizedAviationMessage }) {
  const datalink = message.datalink

  if (!datalink) {
    return null
  }

  return (
    <section className="detail-section" aria-label="Datalink details">
      <h3>{datalink.applicationType} Detail</h3>
      <dl className="metrics compact">
        <div>
          <dt>Direction</dt>
          <dd>{datalink.direction}</dd>
        </div>
        <div>
          <dt>Reference</dt>
          <dd>{datalink.messageReference ?? 'n/a'}</dd>
        </div>
        <div>
          <dt>Ack</dt>
          <dd>{datalink.acknowledgementState ?? 'n/a'}</dd>
        </div>
        <div>
          <dt>Category</dt>
          <dd>{datalink.category}</dd>
        </div>
      </dl>
      {message.satcom ? (
        <dl className="metrics compact satcom-detail">
          <div>
            <dt>Satellite</dt>
            <dd>{message.satcom.satellite ?? 'n/a'}</dd>
          </div>
          <div>
            <dt>Channel</dt>
            <dd>{message.satcom.channel ?? 'n/a'}</dd>
          </div>
          <div>
            <dt>Bearer</dt>
            <dd>{message.satcom.bearer ?? 'n/a'}</dd>
          </div>
          <div>
            <dt>Reassembly</dt>
            <dd>{message.satcom.reassemblyState}</dd>
          </div>
        </dl>
      ) : null}
    </section>
  )
}

function SpectrumCanvas({ frame }: { frame: SpectrumFrame | null }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    const context = canvas?.getContext('2d')

    if (!canvas || !context) {
      return
    }

    context.clearRect(0, 0, canvas.width, canvas.height)
    context.fillStyle = '#0b1216'
    context.fillRect(0, 0, canvas.width, canvas.height)
    context.strokeStyle = '#24343c'
    context.lineWidth = 1

    for (let y = 40; y < canvas.height; y += 40) {
      context.beginPath()
      context.moveTo(0, y)
      context.lineTo(canvas.width, y)
      context.stroke()
    }

    if (!frame) {
      context.fillStyle = '#8fa2aa'
      context.fillText('Waiting for synthetic spectrum replay', 18, 34)
      return
    }

    context.strokeStyle = '#9be7c0'
    context.lineWidth = 2
    context.beginPath()

    frame.bins.forEach((bin, index) => {
      const x = (index / Math.max(frame.bins.length - 1, 1)) * canvas.width
      const normalized = Math.max(0, Math.min(1, (bin + 110) / 85))
      const y = canvas.height - normalized * (canvas.height - 20) - 10

      if (index === 0) {
        context.moveTo(x, y)
      } else {
        context.lineTo(x, y)
      }
    })

    context.stroke()
    context.fillStyle = '#8fa2aa'
    context.fillText(`${frame.centerFrequencyMHz.toFixed(3)} MHz / ${frame.spanKHz.toFixed(0)} kHz / #${frame.sequence}`, 18, 24)
  }, [frame])

  return <canvas className="spectrum-canvas" ref={canvasRef} width="760" height="220" aria-label="Synthetic spectrum display" />
}

function WaterfallCanvas({ rows }: { rows: WaterfallRow[] }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    const context = canvas?.getContext('2d')

    if (!canvas || !context) {
      return
    }

    context.clearRect(0, 0, canvas.width, canvas.height)
    context.fillStyle = '#0b1216'
    context.fillRect(0, 0, canvas.width, canvas.height)

    if (rows.length === 0) {
      context.fillStyle = '#8fa2aa'
      context.fillText('Waiting for synthetic waterfall rows', 18, 28)
      return
    }

    const rowHeight = canvas.height / 80
    const visibleRows = rows.slice(-80)

    visibleRows.forEach((row, rowIndex) => {
      const y = canvas.height - (visibleRows.length - rowIndex) * rowHeight
      const binWidth = canvas.width / Math.max(row.intensities.length, 1)

      row.intensities.forEach((intensity, index) => {
        const green = Math.min(255, 38 + intensity)
        const blue = Math.min(255, 62 + intensity / 2)
        context.fillStyle = `rgb(12, ${green}, ${blue})`
        context.fillRect(index * binWidth, y, Math.ceil(binWidth), Math.ceil(rowHeight))
      })
    })
  }, [rows])

  return <canvas className="waterfall-canvas" ref={canvasRef} width="760" height="220" aria-label="Synthetic waterfall display" />
}

function WefaxCanvas({ lines }: { lines: WefaxImageLine[] }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    const context = canvas?.getContext('2d')

    if (!canvas || !context) {
      return
    }

    context.clearRect(0, 0, canvas.width, canvas.height)
    context.fillStyle = '#0b1216'
    context.fillRect(0, 0, canvas.width, canvas.height)

    if (lines.length === 0) {
      context.fillStyle = '#8fa2aa'
      context.fillText('Waiting for synthetic WEFAX lines', 18, 28)
      return
    }

    const visibleLines = lines.slice(-140)
    const rowHeight = canvas.height / 140
    const imageData = context.createImageData(canvas.width, Math.max(1, Math.ceil(rowHeight)))

    visibleLines.forEach((line, rowIndex) => {
      const y = Math.floor(canvas.height - (visibleLines.length - rowIndex) * rowHeight)

      for (let x = 0; x < canvas.width; x++) {
        const sourceIndex = Math.floor((x / canvas.width) * line.pixels.length)
        const value = line.pixels[sourceIndex] ?? 0
        const offset = x * 4
        imageData.data[offset] = value
        imageData.data[offset + 1] = value
        imageData.data[offset + 2] = value
        imageData.data[offset + 3] = 255
      }

      context.putImageData(imageData, 0, y)
    })
  }, [lines])

  return <canvas className="wefax-canvas" ref={canvasRef} width="760" height="360" aria-label="WEFAX image display" />
}

function HardwareSourcePanel({
  sources,
  diagnostics,
  actionState,
  onStart,
  onStop,
  onSimulate,
}: {
  sources: HardwareSource[]
  diagnostics: HardwareSourceDiagnostic[]
  actionState: string
  onStart: (source: HardwareSource) => void
  onStop: (sourceId: string) => void
  onSimulate: (sourceId: string, scenario: string) => void
}) {
  return (
    <section className="hardware-panel" aria-label="Hardware source adapters">
      <div className="panel-heading compact-heading">
        <h2>Source Adapters</h2>
        <span>{diagnostics.length} diagnostics</span>
      </div>
      <p className="panel-copy status-line">{actionState}</p>
      <div className="hardware-source-list">
        {sources.map((source) => (
          <article key={source.id}>
            <div className="hardware-source-header">
              <div>
                <strong>{source.name}</strong>
                <span>{source.adapterName} / {source.inputKind} / {source.state}</span>
              </div>
              <span>{source.ownerDecoderId ?? 'unowned'}</span>
            </div>
            <dl className="metrics compact hardware-metrics">
              <div>
                <dt>Sample rate</dt>
                <dd>{formatHz(source.metrics.configuredSampleRateHz)}</dd>
              </div>
              <div>
                <dt>Bandwidth</dt>
                <dd>{formatHz(source.metrics.configuredBandwidthHz)}</dd>
              </div>
              <div>
                <dt>Queue</dt>
                <dd>{source.metrics.queueDepth}</dd>
              </div>
              <div>
                <dt>Drops</dt>
                <dd>{source.metrics.droppedBuffers}</dd>
              </div>
              <div>
                <dt>Reconnects</dt>
                <dd>{source.metrics.reconnectCount}</dd>
              </div>
              <div>
                <dt>SNR</dt>
                <dd>{source.metrics.snrDb?.toFixed(1) ?? 'n/a'} dB</dd>
              </div>
            </dl>
            <p className="source-capabilities">
              {formatHz(source.capabilities.minimumSampleRateHz)}-{formatHz(source.capabilities.maximumSampleRateHz)} / {source.capabilities.streamFraming} / {source.capabilities.acknowledgesTuneCommands ? 'acknowledged control' : 'fire-and-forget control'}
            </p>
            {source.lastError ? <p className="panel-copy status-line">{source.lastError}</p> : null}
            <div className="scenario-actions hardware-actions">
              <button type="button" onClick={() => onStart(source)} aria-label={`Start ${source.name}`}>start</button>
              <button type="button" onClick={() => onStop(source.id)} aria-label={`Stop ${source.name}`}>stop</button>
              {['buffer-drop', 'reconnect', 'clipping', 'failure'].map((scenario) => (
                <button key={scenario} type="button" onClick={() => onSimulate(source.id, scenario)} aria-label={`Simulate ${scenario} for ${source.name}`}>
                  {scenario}
                </button>
              ))}
            </div>
          </article>
        ))}
      </div>
      <HardwareDiagnostics diagnostics={diagnostics} />
    </section>
  )
}

function HardwareDiagnostics({ diagnostics }: { diagnostics: HardwareSourceDiagnostic[] }) {
  if (diagnostics.length === 0) {
    return <p className="empty-state">No hardware source diagnostics yet.</p>
  }

  return (
    <ol className="diagnostics" aria-label="Hardware source diagnostics">
      {diagnostics.slice(0, 4).map((diagnostic) => (
        <li key={diagnostic.id}>
          <strong>{diagnostic.code}</strong>
          <span>{diagnostic.message}</span>
          <small>{diagnostic.sourceId} / {diagnostic.severity} / {formatTime(diagnostic.occurredAtUtc)}</small>
        </li>
      ))}
    </ol>
  )
}

function WefaxStatus({ state }: { state: WefaxDecoderState | null }) {
  if (!state) {
    return <p className="empty-state">No WEFAX state yet.</p>
  }

  return (
    <div>
      <dl className="metrics compact">
        <div>
          <dt>IOC</dt>
          <dd>{state.ioc}</dd>
        </div>
        <div>
          <dt>Line rate</dt>
          <dd>{state.lineRateRpm} rpm</dd>
        </div>
        <div>
          <dt>Lines</dt>
          <dd>{state.linesReceived}</dd>
        </div>
        <div>
          <dt>Slant</dt>
          <dd>{state.slantCorrection.toFixed(1)}</dd>
        </div>
      </dl>
      {state.warnings.length > 0 ? (
        <ul className="warnings">
          {state.warnings.map((warning) => (
            <li key={warning.code}>
              <strong>{warning.code}</strong>
              <span>{warning.message}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-state">No WEFAX warnings.</p>
      )}
    </div>
  )
}

function sortMessages(messages: NormalizedAviationMessage[]) {
  return [...messages].sort((left, right) => right.sequence - left.sequence)
}

async function copyToClipboard(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // fall through to the legacy fallback below
  }

  const textarea = document.createElement('textarea')
  textarea.value = text
  textarea.style.position = 'fixed'
  textarea.style.opacity = '0'
  document.body.appendChild(textarea)
  textarea.focus()
  textarea.select()

  try {
    return document.execCommand('copy')
  } catch {
    return false
  } finally {
    document.body.removeChild(textarea)
  }
}

function getSondeIcon(sonde: SondeTrack, isSelected: boolean) {
  const size = isSelected ? 32 : 24
  const color = sonde.burstKill ? '#e06c75' : '#e5c07b'
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="${size}" height="${size}" fill="${color}"><path d="M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z"/></svg>`

  return L.divIcon({
    className: isSelected ? 'sonde-marker-icon selected' : 'sonde-marker-icon',
    html: `<span class="sonde-symbol" title="RadioSonde ${sonde.serial}">${svg}</span>`,
    iconSize: [size, size],
    iconAnchor: [size / 2, size / 2],
    popupAnchor: [0, -size / 2],
  })
}

function getNavaidIcon(navaid: Navaid, isSelected: boolean) {
  const size = isSelected ? 24 : 18
  let svg = ''
  let color = '#4fc3d9'

  if (navaid.type === 'Vortac' || navaid.type === 'VorDme') {
    color = '#4fc3d9'
    svg = `<svg viewBox="0 0 24 24" width="${size}" height="${size}"><polygon points="12,2 20.66,7 20.66,17 12,22 3.34,17 3.34,7" fill="#111e24" stroke="${color}" stroke-width="2.5"/><rect x="8" y="8" width="8" height="8" fill="${color}"/></svg>`
  } else if (navaid.type === 'Vor' || navaid.type === 'Dvor') {
    color = '#9be7c0'
    svg = `<svg viewBox="0 0 24 24" width="${size}" height="${size}"><polygon points="12,2 20.66,7 20.66,17 12,22 3.34,17 3.34,7" fill="#111e24" stroke="${color}" stroke-width="2.5"/><circle cx="12" cy="12" r="3" fill="${color}"/></svg>`
  } else if (navaid.type === 'IlsLocalizer' || navaid.type === 'IlsGlideslope') {
    color = '#e5c07b'
    svg = `<svg viewBox="0 0 24 24" width="${size}" height="${size}"><polygon points="12,2 22,22 2,22" fill="#111e24" stroke="${color}" stroke-width="2.5"/><line x1="12" y1="2" x2="12" y2="22" stroke="${color}" stroke-width="2"/></svg>`
  } else if (navaid.type === 'Ndb') {
    color = '#e06c75'
    svg = `<svg viewBox="0 0 24 24" width="${size}" height="${size}"><circle cx="12" cy="12" r="9" fill="none" stroke="${color}" stroke-width="2" stroke-dasharray="3 2"/><circle cx="12" cy="12" r="4" fill="${color}"/></svg>`
  } else {
    color = '#8a63d2'
    svg = `<svg viewBox="0 0 24 24" width="${size}" height="${size}"><rect x="4" y="4" width="16" height="16" fill="#111e24" stroke="${color}" stroke-width="2.5"/><circle cx="12" cy="12" r="3" fill="${color}"/></svg>`
  }

  const width = size + 20
  const height = size + 14

  return L.divIcon({
    className: isSelected ? 'navaid-marker-icon selected' : 'navaid-marker-icon',
    html: `<div class="navaid-symbol-wrapper">${svg}<span class="navaid-label" style="border-color:${color};">${navaid.identifier}</span></div>`,
    iconSize: [width, height],
    iconAnchor: [width / 2, size / 2],
    popupAnchor: [0, -size / 2],
  })
}

function getClusterIcon(count: number, containsSelected: boolean) {
  const size = count > 20 ? 38 : count > 5 ? 32 : 26
  return L.divIcon({
    className: containsSelected ? 'cluster-marker-icon selected' : 'cluster-marker-icon',
    html: `<span>${count}</span>`,
    iconSize: [size, size],
    iconAnchor: [size / 2, size / 2],
  })
}

function AircraftMap({
  tracks,
  sondes = [],
  navaids = [],
  selectedAircraftId,
  onSelectAircraft,
  showFlightTrack,
  showRangeRings,
  showNavaids,
  showClustering = true,
  followSelectedAircraft,
}: {
  tracks: AircraftTrack[]
  sondes?: SondeTrack[]
  navaids?: Navaid[]
  selectedAircraftId: string | null
  onSelectAircraft: (identifier: string) => void
  showFlightTrack: boolean
  showRangeRings: boolean
  showNavaids: boolean
  showClustering?: boolean
  followSelectedAircraft: boolean
}) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const mapRef = useRef<L.Map | null>(null)
  const markersRef = useRef<Map<string, L.Marker>>(new Map())
  const historyRef = useRef<Map<string, TrackHistoryEntry[]>>(new Map())
  const trackLineRef = useRef<L.Polyline | null>(null)
  const rangeRingsRef = useRef<L.LayerGroup | null>(null)
  const playbackMarkerRef = useRef<L.CircleMarker | null>(null)
  const [playbackMinutesAgo, setPlaybackMinutesAgo] = useState(0)

  useEffect(() => {
    setPlaybackMinutesAgo(0)
  }, [selectedAircraftId])

  useEffect(() => {
    if (!containerRef.current || mapRef.current) {
      return
    }

    let initialCenter: [number, number] = ALLEN_TX_CENTER
    let initialZoom = 9

    try {
      const savedView = localStorage.getItem(MAP_VIEW_STORAGE_KEY)

      if (savedView) {
        const parsed = JSON.parse(savedView) as { lat: number; lng: number; zoom: number }
        initialCenter = [parsed.lat, parsed.lng]
        initialZoom = parsed.zoom
      }
    } catch {
      // Ignore a corrupt/unavailable saved view and fall back to the default center.
    }

    const map = L.map(containerRef.current, {
      center: initialCenter,
      zoom: initialZoom,
    })

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19,
    }).addTo(map)

    map.on('moveend zoomend', () => {
      const center = map.getCenter()

      try {
        localStorage.setItem(MAP_VIEW_STORAGE_KEY, JSON.stringify({ lat: center.lat, lng: center.lng, zoom: map.getZoom() }))
      } catch {
        // Best-effort persistence; ignore storage failures (e.g. private browsing quotas).
      }
    })

    mapRef.current = map

    const resizeObserver = new ResizeObserver(() => map.invalidateSize())
    resizeObserver.observe(containerRef.current)

    return () => {
      resizeObserver.disconnect()
      map.remove()
      mapRef.current = null
    }
  }, [])

  useEffect(() => {
    const copyHex = (hex: string) => {
      void copyToClipboard(hex)
    }

    ;(window as unknown as { aeroHubCopyHex?: (hex: string) => void }).aeroHubCopyHex = copyHex

    return () => {
      delete (window as unknown as { aeroHubCopyHex?: (hex: string) => void }).aeroHubCopyHex
    }
  }, [])

  useEffect(() => {
    const map = mapRef.current

    if (!map) {
      return
    }

    if (rangeRingsRef.current) {
      rangeRingsRef.current.remove()
      rangeRingsRef.current = null
    }

    if (!showRangeRings) {
      return
    }

    const group = L.layerGroup()

    for (const miles of RANGE_RING_MILES) {
      L.circle(ALLEN_TX_CENTER, {
        radius: miles * MILES_TO_METERS,
        color: '#4b6d75',
        weight: 1,
        fill: false,
        dashArray: '4 6',
        interactive: false,
      }).addTo(group)

      const labelPoint = L.latLng(ALLEN_TX_CENTER[0] + miles / 69, ALLEN_TX_CENTER[1])
      L.marker(labelPoint, {
        icon: L.divIcon({
          className: 'range-ring-label',
          html: `${miles} mi`,
          iconSize: [0, 0],
        }),
        interactive: false,
      }).addTo(group)
    }

    group.addTo(map)
    rangeRingsRef.current = group
  }, [showRangeRings])

  const redrawTrail = useCallback(() => {
    const map = mapRef.current

    if (!map) {
      return
    }

    if (trackLineRef.current) {
      trackLineRef.current.remove()
      trackLineRef.current = null
    }

    if (!showFlightTrack || !selectedAircraftId) {
      return
    }

    const history = historyRef.current.get(selectedAircraftId)

    if (!history || history.length < 2) {
      return
    }

    trackLineRef.current = L.polyline(
      history.map((entry) => entry.point),
      {
        color: '#9be7c0',
        weight: 2,
        opacity: 0.85,
        dashArray: '5 6',
      },
    ).addTo(map)
  }, [showFlightTrack, selectedAircraftId])

  const redrawPlaybackMarker = useCallback(() => {
    const map = mapRef.current

    if (playbackMarkerRef.current) {
      playbackMarkerRef.current.remove()
      playbackMarkerRef.current = null
    }

    if (!map || !selectedAircraftId || playbackMinutesAgo <= 0) {
      return
    }

    const history = historyRef.current.get(selectedAircraftId)

    if (!history || history.length === 0) {
      return
    }

    const targetTime = Date.now() - playbackMinutesAgo * 60000
    const entry = [...history].reverse().find((item) => item.atUtc <= targetTime) ?? history[0]

    playbackMarkerRef.current = L.circleMarker(entry.point, {
      radius: 7,
      color: '#e5c07b',
      weight: 2,
      fillColor: '#e5c07b',
      fillOpacity: 0.6,
    })
      .bindTooltip(`${playbackMinutesAgo} min ago (${new Date(entry.atUtc).toLocaleTimeString()})`, { permanent: true, direction: 'top' })
      .addTo(map)
  }, [selectedAircraftId, playbackMinutesAgo])

  useEffect(() => {
    const map = mapRef.current

    if (!map) {
      return
    }

    const seenIds = new Set<string>()
    const zoom = map.getZoom()
    const validTracks = tracks.filter((track) => track.latitude !== undefined && track.longitude !== undefined)
    const enableClustering = showClustering && zoom < 9 && validTracks.length > 3

    const renderAircraftMarker = (track: AircraftTrack) => {
      seenIds.add(track.aircraftIdentifier)
      const isSelected = track.aircraftIdentifier === selectedAircraftId
      const label = track.callsign ?? track.aircraftIdentifier
      const distance = distanceMilesFromCenter(track.latitude!, track.longitude!)
      const bearing = bearingDegreesFromCenter(track.latitude!, track.longitude!)
      const sourceLabel = track.provenance?.sourceName ?? track.sourceType ?? track.sourceId
      const popupHtml = `<strong>${label}</strong> <span class="source-badge">${track.sourceId}</span><br />${track.registration ?? 'Unregistered'} / ${track.aircraftType ?? describeEmitterCategory(track.emitterCategory)}<br />${track.aircraftIdentifier} / ${track.confidence} / ${classifyAircraft(track)}<br />Source: <em>${sourceLabel}</em><br />${track.altitudeFeet?.toLocaleString() ?? 'n/a'} ft / ${track.groundSpeedKnots?.toFixed(0) ?? 'n/a'} kt / ${track.trackDegrees?.toFixed(0) ?? 'n/a'}\u00b0<br />${distance.toFixed(0)} mi @ ${bearing.toFixed(0)}\u00b0 from center<br />${formatAge(track.updatedAtUtc)} (updated ${formatTime(track.updatedAtUtc)})<br /><button type="button" onclick="window.aeroHubCopyHex && window.aeroHubCopyHex('${track.aircraftIdentifier}')">Copy hex</button>`
      const icon = getAircraftIcon(track, isSelected)
      const existing = markersRef.current.get(track.aircraftIdentifier)

      recordTrackHistory(historyRef.current, track.aircraftIdentifier, track.latitude!, track.longitude!, new Date(track.updatedAtUtc).getTime())

      if (existing) {
        existing.setLatLng([track.latitude!, track.longitude!])
        existing.setPopupContent(popupHtml)
        existing.setOpacity(track.isStale ? 0.45 : 1)
        existing.setIcon(icon)
        existing.setZIndexOffset(isSelected ? 1000 : 0)
      } else {
        const marker = L.marker([track.latitude!, track.longitude!], { icon, opacity: track.isStale ? 0.45 : 1, zIndexOffset: isSelected ? 1000 : 0 })
          .bindPopup(popupHtml)
          .on('click', () => onSelectAircraft(track.aircraftIdentifier))
          .addTo(map)
        markersRef.current.set(track.aircraftIdentifier, marker)
      }
    }

    if (enableClustering) {
      type TrackCluster = {
        tracks: AircraftTrack[]
        centerLat: number
        centerLon: number
        containerPoint: L.Point
      }

      const clusters: TrackCluster[] = []
      const CLUSTER_RADIUS_PX = 50

      for (const track of validTracks) {
        const point = map.latLngToContainerPoint([track.latitude!, track.longitude!])
        let assigned = false

        for (const cluster of clusters) {
          if (point.distanceTo(cluster.containerPoint) < CLUSTER_RADIUS_PX) {
            cluster.tracks.push(track)
            cluster.centerLat = cluster.tracks.reduce((sum, item) => sum + item.latitude!, 0) / cluster.tracks.length
            cluster.centerLon = cluster.tracks.reduce((sum, item) => sum + item.longitude!, 0) / cluster.tracks.length
            cluster.containerPoint = map.latLngToContainerPoint([cluster.centerLat, cluster.centerLon])
            assigned = true
            break
          }
        }

        if (!assigned) {
          clusters.push({
            tracks: [track],
            centerLat: track.latitude!,
            centerLon: track.longitude!,
            containerPoint: point,
          })
        }
      }

      for (const cluster of clusters) {
        if (cluster.tracks.length === 1) {
          renderAircraftMarker(cluster.tracks[0])
        } else {
          const clusterId = `cluster-${cluster.tracks.map((item) => item.aircraftIdentifier).sort().join('-')}`
          seenIds.add(clusterId)
          const containsSelected = Boolean(selectedAircraftId && cluster.tracks.some((item) => item.aircraftIdentifier === selectedAircraftId))
          const icon = getClusterIcon(cluster.tracks.length, containsSelected)
          const popupHtml = `<strong>Group of ${cluster.tracks.length} aircraft</strong><br />${cluster.tracks.map((item) => item.callsign ?? item.aircraftIdentifier).slice(0, 5).join(', ')}${cluster.tracks.length > 5 ? '\u2026' : ''}`
          const existing = markersRef.current.get(clusterId)

          for (const track of cluster.tracks) {
            recordTrackHistory(historyRef.current, track.aircraftIdentifier, track.latitude!, track.longitude!, new Date(track.updatedAtUtc).getTime())
          }

          if (existing) {
            existing.setLatLng([cluster.centerLat, cluster.centerLon])
            existing.setPopupContent(popupHtml)
            existing.setIcon(icon)
            existing.setZIndexOffset(containsSelected ? 1000 : 100)
          } else {
            const marker = L.marker([cluster.centerLat, cluster.centerLon], { icon, zIndexOffset: containsSelected ? 1000 : 100 })
              .bindPopup(popupHtml)
              .on('click', () => {
                map.setView([cluster.centerLat, cluster.centerLon], zoom + 2)
              })
              .addTo(map)
            markersRef.current.set(clusterId, marker)
          }
        }
      }
    } else {
      for (const track of validTracks) {
        renderAircraftMarker(track)
      }
    }

    for (const sonde of sondes) {
      if (sonde.latitude === undefined || sonde.longitude === undefined) {
        continue
      }

      const id = `sonde-${sonde.serial}`
      seenIds.add(id)
      const isSelected = id === selectedAircraftId
      const distance = distanceMilesFromCenter(sonde.latitude, sonde.longitude)
      const bearing = bearingDegreesFromCenter(sonde.latitude, sonde.longitude)
      const altFt = sonde.altitudeMeters ? Math.round(sonde.altitudeMeters * 3.28084) : undefined
      const popupHtml = `<strong>🎈 RadioSonde ${sonde.serial}</strong><br />Type: ${sonde.sondeType ?? 'Unknown'}<br />Alt: ${sonde.altitudeMeters?.toLocaleString() ?? 'n/a'} m (${altFt?.toLocaleString() ?? 'n/a'} ft)<br />Ascent: ${sonde.ascentRateMetersPerSecond !== undefined ? (sonde.ascentRateMetersPerSecond > 0 ? '+' : '') + sonde.ascentRateMetersPerSecond.toFixed(1) + ' m/s' : 'n/a'}<br />PTU: ${sonde.temperatureCelsius?.toFixed(1) ?? 'n/a'} \u00b0C / ${sonde.humidityPercent?.toFixed(1) ?? 'n/a'} % RH / ${sonde.pressureHpa?.toFixed(1) ?? 'n/a'} hPa<br />Freq: ${sonde.frequencyMHz?.toFixed(3) ?? 'n/a'} MHz | Frame ${sonde.frameSequence ?? 'n/a'}<br />${distance.toFixed(0)} mi @ ${bearing.toFixed(0)}\u00b0 from center<br />${formatAge(sonde.updatedAtUtc)} (updated ${formatTime(sonde.updatedAtUtc)})`
      const icon = getSondeIcon(sonde, isSelected)
      const existing = markersRef.current.get(id)

      recordTrackHistory(historyRef.current, id, sonde.latitude, sonde.longitude, new Date(sonde.updatedAtUtc).getTime())

      if (existing) {
        existing.setLatLng([sonde.latitude, sonde.longitude])
        existing.setPopupContent(popupHtml)
        existing.setOpacity(sonde.isStale ? 0.45 : 1)
        existing.setIcon(icon)
        existing.setZIndexOffset(isSelected ? 1000 : 0)
      } else {
        const marker = L.marker([sonde.latitude, sonde.longitude], { icon, opacity: sonde.isStale ? 0.45 : 1, zIndexOffset: isSelected ? 1000 : 0 })
          .bindPopup(popupHtml)
          .on('click', () => onSelectAircraft(id))
          .addTo(map)
        markersRef.current.set(id, marker)
      }
    }

    if (showNavaids) {
      for (const navaid of navaids) {
        const id = `navaid-${navaid.identifier}`
        seenIds.add(id)
        const isSelected = id === selectedAircraftId
        const popupHtml = `<strong>📡 ${navaid.identifier} &mdash; ${navaid.name}</strong><br />Type: <strong>${navaid.type}</strong><br />Freq: ${navaid.frequencyMHz.toFixed(2)} MHz<br />${navaid.associatedAirport ? `Airport: ${navaid.associatedAirport}<br />` : ''}${navaid.elevationFeet ? `Elevation: ${navaid.elevationFeet.toLocaleString()} ft<br />` : ''}${navaid.distanceMilesFromCenter.toFixed(1)} mi @ ${navaid.bearingDegreesFromCenter.toFixed(0)}\u00b0 from center<br />${navaid.usageNotes ? `<em>${navaid.usageNotes}</em>` : ''}`
        const icon = getNavaidIcon(navaid, isSelected)
        const existing = markersRef.current.get(id)

        if (existing) {
          existing.setLatLng([navaid.latitude, navaid.longitude])
          existing.setPopupContent(popupHtml)
          existing.setIcon(icon)
          existing.setZIndexOffset(isSelected ? 500 : 100)
        } else {
          const marker = L.marker([navaid.latitude, navaid.longitude], { icon, zIndexOffset: isSelected ? 500 : 100 })
            .bindPopup(popupHtml)
            .on('click', () => onSelectAircraft(id))
            .addTo(map)
          markersRef.current.set(id, marker)
        }
      }
    }

    for (const [aircraftIdentifier, marker] of markersRef.current) {
      if (!seenIds.has(aircraftIdentifier)) {
        marker.remove()
        markersRef.current.delete(aircraftIdentifier)
      }
    }

    redrawTrail()
    redrawPlaybackMarker()

    if (followSelectedAircraft && selectedAircraftId) {
      const selectedMarker = markersRef.current.get(selectedAircraftId)

      if (selectedMarker) {
        map.panTo(selectedMarker.getLatLng())
      }
    }
  }, [tracks, sondes, navaids, selectedAircraftId, onSelectAircraft, redrawTrail, redrawPlaybackMarker, followSelectedAircraft, showNavaids, showClustering])

  useEffect(() => {
    const map = mapRef.current
    const marker = selectedAircraftId ? markersRef.current.get(selectedAircraftId) : undefined

    if (!map || !marker) {
      return
    }

    marker.openPopup()
    map.panTo(marker.getLatLng())
  }, [selectedAircraftId])

  useEffect(() => {
    redrawPlaybackMarker()
  }, [redrawPlaybackMarker])

  useEffect(() => {
    if (!selectedAircraftId || historyRef.current.has(selectedAircraftId)) {
      return
    }

    let cancelled = false

    async function loadHistory() {
      try {
        const response = await fetch(`/api/aircraft/${selectedAircraftId}/history?limit=300`)

        if (!response.ok || cancelled) {
          return
        }

        const samples = (await response.json()) as Array<{ latitude: number; longitude: number; recordedAtUtc: string }>

        if (cancelled || samples.length < 2) {
          return
        }

        historyRef.current.set(
          selectedAircraftId!,
          samples.map((sample) => ({ point: L.latLng(sample.latitude, sample.longitude), atUtc: new Date(sample.recordedAtUtc).getTime() })),
        )
        redrawTrail()
        redrawPlaybackMarker()
      } catch {
        // Cached history is a convenience seed; the trail will keep accumulating client-side regardless.
      }
    }

    void loadHistory()

    return () => {
      cancelled = true
    }
  }, [selectedAircraftId, redrawTrail, redrawPlaybackMarker])

  const selectedHistory = selectedAircraftId ? historyRef.current.get(selectedAircraftId) : undefined
  const canPlayback = Boolean(selectedAircraftId && selectedHistory && selectedHistory.length >= 2)

  return (
    <div className="aircraft-map-wrapper">
      <div className="aircraft-map" ref={containerRef} role="img" aria-label="Live aircraft map centered on Allen, Texas" />
      {canPlayback ? (
        <div className="playback-control" aria-label="Flight history playback">
          <label>
            Playback: {playbackMinutesAgo === 0 ? 'Live' : `${playbackMinutesAgo} min ago`}
            <input
              type="range"
              min={0}
              max={60}
              value={playbackMinutesAgo}
              onChange={(event) => setPlaybackMinutesAgo(Number(event.target.value))}
            />
          </label>
        </div>
      ) : null}
    </div>
  )
}

function AircraftListPanel({
  tracks,
  selectedAircraftId,
  onSelectAircraft,
}: {
  tracks: AircraftTrack[]
  selectedAircraftId: string | null
  onSelectAircraft: (aircraftIdentifier: string) => void
}) {
  const [photo, setPhoto] = useState<AircraftPhotoInfo | null>(null)
  const [photoState, setPhotoState] = useState<'idle' | 'loading' | 'not-found'>('idle')
  const sortedTracks = sortAircraftTracks(tracks)
  const selectedTrack = sortedTracks.find((track) => track.aircraftIdentifier === selectedAircraftId)

  useEffect(() => {
    if (!selectedAircraftId) {
      setPhoto(null)
      setPhotoState('idle')
      return
    }

    let cancelled = false
    setPhoto(null)
    setPhotoState('loading')

    async function loadPhoto() {
      try {
        const response = await fetch(`/api/aircraft/${selectedAircraftId}/photo`)

        if (cancelled) {
          return
        }

        if (!response.ok) {
          setPhotoState('not-found')
          return
        }

        setPhoto((await response.json()) as AircraftPhotoInfo)
        setPhotoState('idle')
      } catch {
        if (!cancelled) {
          setPhotoState('not-found')
        }
      }
    }

    void loadPhoto()

    return () => {
      cancelled = true
    }
  }, [selectedAircraftId])

  if (sortedTracks.length === 0) {
    return (
      <div className="aircraft-list-panel-wrapper">
        <p className="empty-state">No aircraft tracks yet.</p>
      </div>
    )
  }

  return (
    <div className="aircraft-list-panel-wrapper">
      {selectedTrack ? <SelectedAircraftCard track={selectedTrack} photo={photo} photoState={photoState} /> : null}
      <ol className="aircraft-list-panel" aria-label="Aircraft list">
        {sortedTracks.map((track) => {
          const aircraftClass = classifyAircraft(track)

          return (
            <li key={track.aircraftIdentifier}>
              <button
                type="button"
                className={track.aircraftIdentifier === selectedAircraftId ? 'aircraft-list-item selected' : 'aircraft-list-item'}
                onClick={() => onSelectAircraft(track.aircraftIdentifier)}
              >
                <div className="aircraft-list-item-heading">
                  <span
                    className="aircraft-class-dot"
                    style={{ backgroundColor: AIRCRAFT_CLASS_COLORS[aircraftClass] }}
                    title={aircraftClass}
                    aria-label={`${aircraftClass} aircraft`}
                  />
                  <strong>{track.callsign ?? track.aircraftIdentifier}</strong>
                  {track.isStale ? <span className="stale-badge">STALE</span> : null}
                </div>
                <span>{track.registration ?? 'Unregistered'} / {track.aircraftType ?? describeEmitterCategory(track.emitterCategory)}</span>
                <span>
                  {track.aircraftIdentifier} / {track.confidence} /{' '}
                  <span className="aircraft-class-label" style={{ color: AIRCRAFT_CLASS_COLORS[aircraftClass] }}>
                    {aircraftClass}
                  </span>{' '}
                  / <span className="source-badge">{track.sourceId}</span>
                </span>
                <span>
                  {track.altitudeFeet?.toLocaleString() ?? 'n/a'} ft / {track.groundSpeedKnots?.toFixed(0) ?? 'n/a'} kt / {track.trackDegrees?.toFixed(0) ?? 'n/a'} deg
                </span>
                <small>updated {formatTime(track.updatedAtUtc)}</small>
              </button>
            </li>
          )
        })}
      </ol>
    </div>
  )
}

function SelectedAircraftCard({
  track,
  photo,
  photoState,
}: {
  track: AircraftTrack
  photo: AircraftPhotoInfo | null
  photoState: 'idle' | 'loading' | 'not-found'
}) {
  const aircraftClass = classifyAircraft(track)

  return (
    <div className="selected-aircraft-card" aria-label="Selected aircraft detail">
      <div className="selected-aircraft-photo">
        {photoState === 'loading' ? (
          <span className="empty-state">{'Loading photo\u2026'}</span>
        ) : photo ? (
          <a href={photo.sourceLink ?? undefined} target="_blank" rel="noreferrer">
            <img src={photo.thumbnailUrl} alt={track.registration ?? track.aircraftIdentifier} />
          </a>
        ) : (
          <span className="empty-state">No photo available</span>
        )}
        {photo?.photographerName ? <small className="photo-credit">Photo: {photo.photographerName} / planespotters.net</small> : null}
      </div>
      <div className="selected-aircraft-details">
        <strong>{track.callsign ?? track.aircraftIdentifier}</strong>
        <span className="aircraft-class-badge" style={{ backgroundColor: AIRCRAFT_CLASS_COLORS[aircraftClass] }}>
          {aircraftClass}
        </span>
        <span>{track.registration ?? 'Unregistered'} / {track.aircraftType ?? describeEmitterCategory(track.emitterCategory)}</span>
        <span>{track.operatorName ?? 'Unknown operator'}</span>
        <span>Source: <strong>{track.sourceId}</strong> ({track.sourceType})</span>
      </div>
    </div>
  )
}

function AircraftTrackTable({ tracks, isLoading }: { tracks: AircraftTrack[]; isLoading: boolean }) {
  if (tracks.length === 0) {
    return <p className="empty-state">{isLoading ? 'Loading aircraft tracks\u2026' : 'Start the ADS-B import to create aircraft tracks.'}</p>
  }

  return (
    <div className="aircraft-table" aria-label="Aircraft tracks">
      {tracks.map((track) => (
        <div key={track.aircraftIdentifier} className={track.isStale ? 'aircraft-row stale' : 'aircraft-row'}>
          <strong>
            {track.callsign ?? track.aircraftIdentifier}
            {track.isStale ? <span className="stale-badge">STALE</span> : null}
          </strong>
          <span>{track.aircraftIdentifier} / {track.confidence} / {track.sourceType}</span>
          <span>{track.registration ?? 'Unregistered'} / {track.aircraftType ?? describeEmitterCategory(track.emitterCategory)}</span>
          <span>{formatCoordinate(track.latitude, track.longitude)} / {track.altitudeFeet?.toLocaleString() ?? 'n/a'} ft</span>
          <span>{track.groundSpeedKnots?.toFixed(0) ?? 'n/a'} kt / {track.trackDegrees?.toFixed(0) ?? 'n/a'} deg</span>
          <small>{track.provenance.path} / {track.provenance.sourceApp ?? 'unknown app'} / {track.correlationGroupId} / updated {formatTime(track.updatedAtUtc)}</small>
        </div>
      ))}
    </div>
  )
}

function sortAircraftTracks(tracks: AircraftTrack[]) {
  return [...tracks].sort((left, right) => (left.callsign ?? left.aircraftIdentifier).localeCompare(right.callsign ?? right.aircraftIdentifier))
}

function sortRemoteIdObservations(observations: RemoteIdObservation[]) {
  return [...observations].sort((left, right) => right.receivedAtUtc.localeCompare(left.receivedAtUtc))
}

function ExternalDecoderPanel({
  processes,
  diagnostics,
  stateText,
  onSimulate,
}: {
  processes: ExternalDecoderProcess[]
  diagnostics: ExternalDecoderDiagnostic[]
  stateText: string
  onSimulate: (processId: string, scenario: string) => void
}) {
  return (
    <section className="external-panel" aria-label="External decoder process state">
      <div className="panel-heading compact-heading">
        <h2>External Processes</h2>
        <span>{diagnostics.length} events</span>
      </div>
      <p className="panel-copy status-line">{stateText}</p>
      <div className="process-list">
        {processes.map((process) => (
          <article key={process.id}>
            <div>
              <strong>{process.name}</strong>
              <span>{process.executableName} / {process.state}</span>
              <span>restarts {process.restartCount}/{process.restartLimit}</span>
              {process.lastError ? <small>{process.lastError}</small> : null}
            </div>
            <div className="scenario-actions">
              {['start', 'crash', 'timeout', 'malformed-output', 'restart-limit'].map((scenario) => (
                <button key={scenario} type="button" onClick={() => onSimulate(process.id, scenario)} aria-label={`Simulate ${scenario} for ${process.name}`}>
                  {scenario}
                </button>
              ))}
            </div>
          </article>
        ))}
      </div>
      <ExternalDiagnostics diagnostics={diagnostics} />
    </section>
  )
}

function ExternalDiagnostics({ diagnostics }: { diagnostics: ExternalDecoderDiagnostic[] }) {
  if (diagnostics.length === 0) {
    return <p className="empty-state">No external decoder diagnostics yet.</p>
  }

  return (
    <ol className="diagnostics" aria-label="External decoder diagnostics">
      {diagnostics.map((diagnostic) => (
        <li key={diagnostic.id}>
          <strong>{diagnostic.code}</strong>
          <span>{diagnostic.message}</span>
          <small>{diagnostic.processId} / {diagnostic.severity} / {formatTime(diagnostic.occurredAtUtc)}</small>
        </li>
      ))}
    </ol>
  )
}

function sortExternalProcesses(processes: ExternalDecoderProcess[]) {
  return [...processes].sort((left, right) => left.id.localeCompare(right.id))
}

function sortExternalDiagnostics(diagnostics: ExternalDecoderDiagnostic[]) {
  return [...diagnostics].sort((left, right) => right.occurredAtUtc.localeCompare(left.occurredAtUtc))
}

function sortHardwareSources(sources: HardwareSource[]) {
  return [...sources].sort((left, right) => left.id.localeCompare(right.id))
}

function sortHardwareDiagnostics(diagnostics: HardwareSourceDiagnostic[]) {
  return [...diagnostics].sort((left, right) => right.occurredAtUtc.localeCompare(left.occurredAtUtc))
}

function formatHz(value: number) {
  if (value >= 1000000) {
    return `${(value / 1000000).toFixed(2)} MHz`
  }

  if (value >= 1000) {
    return `${(value / 1000).toFixed(0)} kHz`
  }

  return `${value.toFixed(0)} Hz`
}

function formatCoordinate(latitude?: number, longitude?: number) {
  if (latitude === undefined || longitude === undefined) {
    return 'n/a'
  }

  return `${latitude.toFixed(3)}, ${longitude.toFixed(3)}`
}

function RadioSondePanel({
  sondes,
  actionState,
  onStartImport,
}: {
  sondes: SondeTrack[]
  actionState: string
  onStartImport: () => void
}) {
  return (
    <article className="panel sonde-panel" id="sondes">
      <div className="panel-heading">
        <h2>RadioSonde Telemetry</h2>
        <span>{sondes.length} active sonde(s)</span>
      </div>
      <p className="panel-copy">
        Weather balloon radiosondes (RS41/RS92/DFM/M10) transmit 400-406 MHz narrowband FM telemetry.
      </p>
      <button className="action secondary" type="button" onClick={onStartImport}>
        Start RadioSonde Import
      </button>
      <p className="panel-copy status-line">{actionState}</p>
      {sondes.length === 0 ? (
        <p className="empty-state">No radiosonde tracks active. Run an import to stream telemetry.</p>
      ) : (
        <div className="status-list sonde-list">
          {sondes.map((sonde) => {
            const altFt = sonde.altitudeMeters ? Math.round(sonde.altitudeMeters * 3.28084) : undefined
            return (
              <div key={sonde.serial} className="sonde-card">
                <div className="sonde-card-header">
                  <strong>🎈 {sonde.serial} ({sonde.sondeType ?? 'Radiosonde'})</strong>
                  <span className="sonde-badge">{sonde.burstKill ? 'BURST DETECTED' : 'IN FLIGHT'}</span>
                </div>
                <span>
                  Alt: {sonde.altitudeMeters?.toLocaleString() ?? 'n/a'} m ({altFt?.toLocaleString() ?? 'n/a'} ft) / Ascent: {sonde.ascentRateMetersPerSecond !== undefined ? (sonde.ascentRateMetersPerSecond > 0 ? '+' : '') + sonde.ascentRateMetersPerSecond.toFixed(1) + ' m/s' : 'n/a'}
                </span>
                <span>
                  PTU: {sonde.temperatureCelsius?.toFixed(1) ?? 'n/a'} &deg;C / {sonde.humidityPercent?.toFixed(1) ?? 'n/a'} % RH / {sonde.pressureHpa?.toFixed(1) ?? 'n/a'} hPa
                </span>
                <span>
                  Freq: {sonde.frequencyMHz?.toFixed(3) ?? 'n/a'} MHz | Frame #{sonde.frameSequence ?? 'n/a'} | RSSI: {sonde.rssi?.toFixed(1) ?? 'n/a'} dBm
                </span>
                <small>Updated {formatTime(sonde.updatedAtUtc)} ({formatAge(sonde.updatedAtUtc)})</small>
              </div>
            )
          })}
        </div>
      )}
    </article>
  )
}

function CommercialDronePanel({
  tracks,
  observations,
  actionState,
  onStartImport,
}: {
  tracks: AircraftTrack[]
  observations: RemoteIdObservation[]
  actionState: string
  onStartImport: () => void
}) {
  const drones = sortAircraftTracks(tracks.filter((track) => track.remoteIdSerialNumber))

  return (
    <article className="panel" id="commercial-drones">
      <div className="panel-heading">
        <h2>Commercial Drone Tracking</h2>
        <span>{drones.length} active drone(s)</span>
      </div>
      <p className="panel-copy">
        Remote ID position reports appear on the aircraft map and in this list. Live tracking requires a compatible receiver or feed.
      </p>
      <button className="action secondary" type="button" onClick={onStartImport}>
        Import Remote ID Sample
      </button>
      <p className="panel-copy status-line">{actionState}</p>
      {drones.length === 0 ? (
        <p className="empty-state">No Remote ID drone reports available.</p>
      ) : (
        <div className="aircraft-table" aria-label="Commercial drone tracks">
          {drones.map((drone) => (
            <div key={drone.aircraftIdentifier} className={drone.isStale ? 'aircraft-row stale' : 'aircraft-row'}>
              <strong>{drone.remoteIdSerialNumber}</strong>
              <span>{drone.remoteIdOperationType ?? 'Operation type unknown'} / {drone.confidence}</span>
              <span>Operator ID: {drone.remoteIdOperatorId ?? 'Not provided'}</span>
              <span>{formatCoordinate(drone.latitude, drone.longitude)} / Geodetic: {drone.remoteIdAltitudeGeodeticMeters?.toFixed(1) ?? 'n/a'} m / AGL: {drone.remoteIdHeightAboveGroundMeters?.toFixed(1) ?? 'n/a'} m</span>
              <span>{drone.groundSpeedKnots?.toFixed(0) ?? 'n/a'} kt / {drone.trackDegrees?.toFixed(0) ?? 'n/a'} deg</span>
              <small>{formatAge(drone.updatedAtUtc)} / {drone.provenance.sourceApp ?? 'unknown source'}</small>
            </div>
          ))}
        </div>
      )}
      <div className="panel-heading compact-heading">
        <h3>Recent Remote ID Messages</h3>
        <span>{observations.length}</span>
      </div>
      {observations.length === 0 ? (
        <p className="empty-state">No Remote ID observations received.</p>
      ) : (
        <div className="aircraft-table" aria-label="Recent Remote ID observations">
          {observations.slice(0, 12).map((observation) => (
            <div key={observation.id} className="aircraft-row">
              <strong>{observation.messageType} / {observation.uasId ?? 'UAS ID unavailable'}</strong>
              <span>{observation.uasIdType ?? 'ID type unknown'} / {observation.uaType ?? 'UA type unknown'} / Operator: {observation.operatorId ?? 'n/a'}</span>
              <span>{formatCoordinate(observation.latitude, observation.longitude)} / Geodetic: {observation.altitudeGeodeticMeters?.toFixed(1) ?? 'n/a'} m / AGL: {observation.heightAboveGroundMeters?.toFixed(1) ?? 'n/a'} m</span>
              <span>{observation.groundSpeedMetersPerSecond?.toFixed(1) ?? 'n/a'} m/s / {observation.headingDegrees?.toFixed(0) ?? 'n/a'} deg / vertical {observation.verticalSpeedMetersPerSecond?.toFixed(1) ?? 'n/a'} m/s</span>
              <span>{observation.radio ?? 'radio unknown'} / ch {observation.channel ?? 'n/a'} / {observation.rssi?.toFixed(0) ?? 'n/a'} dBm / {observation.receiverId ?? 'receiver unknown'}</span>
              <span>System area: {observation.areaCount ?? 'n/a'} / radius {observation.areaRadiusMeters?.toFixed(0) ?? 'n/a'} m / floor {observation.areaFloorMeters?.toFixed(0) ?? 'n/a'} m / ceiling {observation.areaCeilingMeters?.toFixed(0) ?? 'n/a'} m</span>
              <small>Accuracy: horizontal {observation.horizontalAccuracyMeters?.toFixed(1) ?? 'n/a'} m / vertical {observation.verticalAccuracyMeters?.toFixed(1) ?? 'n/a'} m / speed {observation.speedAccuracyMetersPerSecond?.toFixed(1) ?? 'n/a'} m/s / direction {observation.directionAccuracyDegrees?.toFixed(0) ?? 'n/a'} deg</small>
              {observation.selfIdText ? <small>Self-ID: {observation.selfIdText}</small> : null}
              <small>{formatTime(observation.receivedAtUtc)} / {observation.validationStatus ?? 'validation unknown'} / authentication: {observation.authenticationStatus ?? 'not reported'} / source reports verified: {observation.authenticationVerifiedBySource === undefined ? 'unknown' : observation.authenticationVerifiedBySource ? 'yes' : 'no'}</small>
              {observation.validationWarnings.length > 0 ? <small>Warnings: {observation.validationWarnings.join('; ')}</small> : null}
            </div>
          ))}
        </div>
      )}
    </article>
  )
}

function FeederPanel({
  feeders,
  diagnostics,
  actionState,
  onStart,
  onStop,
  onTest,
}: {
  feeders: FeederStatus[]
  diagnostics: FeederDiagnostic[]
  actionState: string
  onStart: (id: string) => void
  onStop: (id: string) => void
  onTest: (id: string) => void
}) {
  return (
    <section className="external-panel" aria-label="Flight tracking online data feeders">
      <div className="panel-heading compact-heading">
        <h2>Data Feeders (FlightAware)</h2>
        <span>{feeders.filter((f) => f.state === 'Online').length} active</span>
      </div>
      <p className="panel-copy status-line">{actionState}</p>
      <div className="process-list">
        {feeders.map((feeder) => (
          <article key={feeder.id}>
            <div>
              <strong>{feeder.name}</strong>
              <span>Target: <strong>{feeder.targetService}</strong> ({feeder.host}:{feeder.port})</span>
              <span>Protocol: {feeder.protocol} / Mode: {feeder.mode} / State: <strong>{feeder.state}</strong></span>
              <span>Sent: {feeder.framesSent} frames ({formatBytes(feeder.bytesSent)}) | Clients: {feeder.connectedClients}</span>
              {feeder.lastError ? <small>{feeder.lastError}</small> : null}
            </div>
            <div className="scenario-actions">
              <button type="button" onClick={() => onStart(feeder.id)} aria-label={`Start feeder ${feeder.name}`}>start</button>
              <button type="button" onClick={() => onStop(feeder.id)} aria-label={`Stop feeder ${feeder.name}`}>stop</button>
              <button type="button" onClick={() => onTest(feeder.id)} aria-label={`Send test frame for ${feeder.name}`}>test-frame</button>
            </div>
          </article>
        ))}
      </div>
      <FeederDiagnostics diagnostics={diagnostics} />
    </section>
  )
}

function FeederDiagnostics({ diagnostics }: { diagnostics: FeederDiagnostic[] }) {
  if (diagnostics.length === 0) {
    return <p className="empty-state">No feeder diagnostics yet.</p>
  }

  return (
    <ol className="diagnostics" aria-label="Data feeder diagnostics">
      {diagnostics.slice(0, 4).map((diagnostic) => (
        <li key={diagnostic.id}>
          <strong>{diagnostic.code}</strong>
          <span>{diagnostic.message}</span>
          <small>{diagnostic.feederId} / {diagnostic.severity} / {formatTime(diagnostic.occurredAtUtc)}</small>
        </li>
      ))}
    </ol>
  )
}

function sortFeederDiagnostics(diagnostics: FeederDiagnostic[]) {
  return [...diagnostics].sort((left, right) => right.occurredAtUtc.localeCompare(left.occurredAtUtc))
}

function sortSondeTracks(sondes: SondeTrack[]) {
  return [...sondes].sort((left, right) => new Date(right.updatedAtUtc).getTime() - new Date(left.updatedAtUtc).getTime())
}

function DecoderSettingsPanel({
  settings,
  actionState,
  onSave,
}: {
  settings: DecoderSettings | null
  actionState: string
  onSave: (settings: DecoderSettings) => void
}) {
  const [draft, setDraft] = useState<DecoderSettings | null>(settings)

  useEffect(() => {
    setDraft(settings)
  }, [settings])

  if (!draft) {
    return (
      <article className="panel settings-panel" id="settings">
        <div className="panel-heading">
          <h2>Decoder Settings</h2>
          <span>Loading preferences&hellip;</span>
        </div>
      </article>
    )
  }

  return (
    <article className="panel settings-panel" id="settings">
      <div className="panel-heading">
        <h2>Decoder Settings</h2>
        <span>Persisted preferences</span>
      </div>
      <p className="panel-copy">
        Decoder thresholds, WEFAX parameters, dump1090 auto-connect, and data feeder auto-start preferences are persisted to disk in <code>data/store/settings.json</code> with automatic backup and crash recovery.
      </p>
      <div className="settings-grid">
        <div className="settings-section">
          <h3>General & Track Settings</h3>
          <label className="settings-field">
            Stale Track Expiry (minutes)
            <input
              type="number"
              min="1"
              max="120"
              value={draft.staleTrackTimeoutMinutes}
              onChange={(e) => setDraft({ ...draft, staleTrackTimeoutMinutes: Number(e.target.value) })}
            />
          </label>
        </div>

        <div className="settings-section">
          <h3>WEFAX Decoder Defaults</h3>
          <label className="settings-field">
            Default IOC
            <input
              type="number"
              value={draft.wefax.ioc}
              onChange={(e) => setDraft({ ...draft, wefax: { ...draft.wefax, ioc: Number(e.target.value) } })}
            />
          </label>
          <label className="settings-field">
            Line Rate (RPM)
            <input
              type="number"
              value={draft.wefax.lineRateRpm}
              onChange={(e) => setDraft({ ...draft, wefax: { ...draft.wefax, lineRateRpm: Number(e.target.value) } })}
            />
          </label>
        </div>

        <div className="settings-section">
          <h3>dump1090 Network Options</h3>
          <label className="settings-field">
            Default Host
            <input
              type="text"
              value={draft.dump1090.host ?? ''}
              onChange={(e) => setDraft({ ...draft, dump1090: { ...draft.dump1090, host: e.target.value } })}
              placeholder="e.g. 192.168.50.23"
            />
          </label>
          <label className="settings-field checkbox-field">
            <input
              type="checkbox"
              checked={draft.dump1090.autoConnect}
              onChange={(e) => setDraft({ ...draft, dump1090: { ...draft.dump1090, autoConnect: e.target.checked } })}
            />
            Auto-connect on backend startup
          </label>
        </div>
      </div>

      <div className="settings-actions">
        <button className="action" type="button" onClick={() => onSave(draft)}>
          Save Settings
        </button>
        <span className="status-line">{actionState}</span>
      </div>
    </article>
  )
}

function formatBytes(value: number) {
  if (value >= 1024 * 1024 * 1024) {
    return `${(value / (1024 * 1024 * 1024)).toFixed(2)} GB`
  }

  if (value >= 1024 * 1024) {
    return `${(value / (1024 * 1024)).toFixed(2)} MB`
  }

  if (value >= 1024) {
    return `${(value / 1024).toFixed(1)} KB`
  }

  return `${value.toFixed(0)} B`
}

function formatTime(iso?: string) {
  if (!iso) {
    return 'n/a'
  }

  const parsed = new Date(iso)

  if (Number.isNaN(parsed.getTime())) {
    return 'n/a'
  }

  return parsed.toLocaleTimeString()
}

function ImportDiagnostics({ diagnostics }: { diagnostics: ImportDiagnostic[] }) {
  if (diagnostics.length === 0) {
    return <p className="empty-state">No import diagnostics yet.</p>
  }

  return (
    <ol className="diagnostics" aria-label="Import diagnostics">
      {diagnostics.map((diagnostic) => (
        <li key={diagnostic.id}>
          <strong>{diagnostic.code}</strong>
          <span>{diagnostic.message}</span>
          <small>{diagnostic.importId} / {diagnostic.rawReference ?? 'no raw reference'} / {formatTime(diagnostic.occurredAtUtc)}</small>
        </li>
      ))}
    </ol>
  )
}

function sortDiagnostics(diagnostics: ImportDiagnostic[]) {
  return [...diagnostics].sort((left, right) => right.occurredAtUtc.localeCompare(left.occurredAtUtc))
}

function BackendStatus({ health }: { health: HealthState }) {
  if (health.status === 'online') {
    return (
      <div className="status online" aria-label="Backend online">
        <span />
        <div>
          <strong>{health.data.serviceName}</strong>
          <small>{health.data.environment} / v{health.data.version}</small>
        </div>
      </div>
    )
  }

  if (health.status === 'offline') {
    return (
      <div className="status offline" aria-label="Backend offline">
        <span />
        <div>
          <strong>Backend offline</strong>
          <small>{health.message}</small>
        </div>
      </div>
    )
  }

  return (
    <div className="status checking" aria-label="Checking backend">
      <span />
      <div>
        <strong>Checking backend</strong>
        <small>/api/health</small>
      </div>
    </div>
  )
}

function HubStatusBadge({ state }: { state: HubConnectionState }) {
  const isConnected = state === HubConnectionState.Connected
  const isReconnecting = state === HubConnectionState.Reconnecting || state === HubConnectionState.Connecting
  const statusClass = isConnected ? 'online' : isReconnecting ? 'checking' : 'offline'
  const label = isConnected ? 'Live updates connected' : isReconnecting ? 'Reconnecting\u2026' : 'Live updates disconnected'

  return (
    <div className={`status ${statusClass}`} aria-live="polite" aria-label="Live update connection status">
      <span />
      <div>
        <strong>{label}</strong>
        <small>SignalR / {state}</small>
      </div>
    </div>
  )
}

function ActivityLog({ entries }: { entries: string[] }) {
  return (
    <div className="activity-log" aria-label="Recent activity">
      <h3>Recent Activity</h3>
      {entries.length === 0 ? (
        <p className="empty-state">No actions yet.</p>
      ) : (
        <ol>
          {entries.map((entry) => (
            <li key={entry}>{entry}</li>
          ))}
        </ol>
      )}
    </div>
  )
}

function Dump1090Panel({
  status,
  diagnostics,
  form,
  actionState,
  onFormChange,
  onConnect,
  onDisconnect,
}: {
  status: Dump1090ConnectionSnapshot | null
  diagnostics: ImportDiagnostic[]
  form: Dump1090FormState
  actionState: string
  onFormChange: (form: Dump1090FormState) => void
  onConnect: () => void
  onDisconnect: () => void
}) {
  const isActive = status?.state === 'Online' || status?.state === 'Starting' || status?.state === 'Degraded'

  return (
    <section className="dump1090-panel" aria-label="Network ADS-B (dump1090) source">
      <div className="panel-heading compact-heading">
        <h2>Network ADS-B (dump1090)</h2>
        <span>{status?.state ?? 'Offline'}</span>
      </div>
      <div className="dump1090-form">
        <label>
          Host
          <input
            type="text"
            value={form.host}
            placeholder="192.168.1.50"
            onChange={(event) => onFormChange({ ...form, host: event.target.value })}
          />
        </label>
        <label>
          Port
          <input
            type="number"
            min={1}
            max={65535}
            value={form.port}
            onChange={(event) => onFormChange({ ...form, port: Number(event.target.value) })}
          />
        </label>
        <label>
          JSON path
          <input
            type="text"
            value={form.jsonPath}
            onChange={(event) => onFormChange({ ...form, jsonPath: event.target.value })}
          />
        </label>
        <label>
          Poll seconds
          <input
            type="number"
            min={1}
            max={3600}
            value={form.pollIntervalSeconds}
            onChange={(event) => onFormChange({ ...form, pollIntervalSeconds: Number(event.target.value) })}
          />
        </label>
      </div>
      <div className="scenario-actions">
        <button className="action compact-action" type="button" onClick={onConnect} disabled={!form.host.trim()}>
          Connect
        </button>
        <button className="action compact-action secondary" type="button" onClick={onDisconnect} disabled={!isActive}>
          Disconnect
        </button>
      </div>
      <p className="panel-copy status-line">{actionState}</p>
      {status ? (
        <dl className="metrics compact">
          <div>
            <dt>Endpoint</dt>
            <dd>{status.host ? `${status.host}:${status.port}${status.jsonPath ?? ''}` : 'n/a'}</dd>
          </div>
          <div>
            <dt>Accepted</dt>
            <dd>{status.acceptedRecords}</dd>
          </div>
          <div>
            <dt>Quarantined</dt>
            <dd>{status.rejectedRecords}</dd>
          </div>
          <div>
            <dt>Last poll</dt>
            <dd>{formatTime(status.lastPolledAtUtc)}</dd>
          </div>
        </dl>
      ) : null}
      {status?.lastError ? <p className="panel-copy status-line">{status.lastError}</p> : null}
      <ImportDiagnostics diagnostics={diagnostics} />
    </section>
  )
}

export default App
