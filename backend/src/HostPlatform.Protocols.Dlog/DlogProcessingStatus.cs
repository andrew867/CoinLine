namespace HostPlatform.Protocols.Dlog;

public enum DlogProcessingStatus
{
    Ingested = 0,
    Decoded = 1,
    CorrelationLinked = 2,
    ReplayExported = 3
}
