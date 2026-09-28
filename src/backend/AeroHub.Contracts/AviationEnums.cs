namespace AeroHub.Contracts;

public enum IngestionPath
{
    NativeDecoder,
    ExternalProcess,
    ImportedDecodedData,
    FixtureReplay
}

public enum SourceState
{
    Offline,
    Starting,
    Online,
    Degraded,
    Failed
}

public enum AviationMessageKind
{
    Unknown,
    Acars,
    Hfdl,
    Vdl2,
    Adsb,
    Cpdlc,
    AdsC,
    Satcom,
    Wefax
}

public enum ParserConfidence
{
    Unknown,
    Observed,
    Candidate,
    Confirmed
}

public enum NavaidType
{
    Vor,
    Dvor,
    Vortac,
    VorDme,
    IlsLocalizer,
    IlsGlideslope,
    MarkerBeacon,
    Ndb,
    Dme
}