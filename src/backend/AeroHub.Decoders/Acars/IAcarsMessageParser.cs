using AeroHub.Contracts;

namespace AeroHub.Decoders.Acars;

public interface IAcarsMessageParser
{
    NormalizedAviationMessage Parse(AcarsParserInput input);
}