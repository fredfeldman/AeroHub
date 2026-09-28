using AeroHub.Contracts;
using AeroHub.Decoders.Acars;

namespace AeroHub.Decoders.Tests;

public class AcarsMessageParserTests
{
    private readonly AcarsMessageParser _parser = new();

    [Fact]
    public void Parse_known_oooi_payload_preserves_label_sublabel_and_raw_text()
    {
        var message = _parser.Parse(CreateInput("2L POS N123AB OUT 1412Z OFF 1427Z"));

        Assert.Equal(AviationMessageKind.Acars, message.Kind);
        Assert.Equal("2L", message.Acars?.Label);
        Assert.Equal("POS", message.Acars?.Sublabel);
        Assert.Equal("N123AB", message.AircraftIdentifier);
        Assert.Equal(ParserConfidence.Candidate, message.Confidence);
        Assert.Equal("2L POS N123AB OUT 1412Z OFF 1427Z", message.RawPayload);
        Assert.Empty(message.Warnings);
    }

    [Fact]
    public void Parse_malformed_payload_returns_message_with_warning()
    {
        var message = _parser.Parse(CreateInput("?"));

        Assert.Equal(AviationMessageKind.Acars, message.Kind);
        Assert.Null(message.Acars?.Label);
        Assert.Equal(ParserConfidence.Unknown, message.Confidence);
        Assert.Contains(message.Warnings, warning => warning.Code == "ACARS_MISSING_LABEL");
    }

    [Fact]
    public void Parse_unknown_label_preserves_payload_with_unknown_warning()
    {
        var message = _parser.Parse(CreateInput("ZZ TEST N789EF UNKNOWN PAYLOAD"));

        Assert.Equal("ZZ", message.Acars?.Label);
        Assert.Equal("TEST", message.Acars?.Sublabel);
        Assert.Equal("N789EF", message.AircraftIdentifier);
        Assert.Equal(ParserConfidence.Unknown, message.Confidence);
        Assert.Contains(message.Warnings, warning => warning.Code == "ACARS_UNKNOWN_LABEL");
        Assert.Equal("ZZ TEST N789EF UNKNOWN PAYLOAD", message.RawPayload);
    }

    [Fact]
    public void Parse_imported_record_uses_same_normalized_shape_with_import_provenance()
    {
        var imported = _parser.Parse(CreateInput(
            "H1 #DFB REQUEST WX N456CD",
            IngestionPath.ImportedDecodedData,
            "import-airframes-ndjson"));

        Assert.Equal("H1", imported.Acars?.Label);
        Assert.Equal("#DFB", imported.Acars?.Preamble);
        Assert.Equal("N456CD", imported.AircraftIdentifier);
        Assert.Equal(IngestionPath.ImportedDecodedData, imported.Provenance.Path);
        Assert.Equal("import-airframes-ndjson", imported.Provenance.SourceId);
    }

    private static AcarsParserInput CreateInput(
        string rawPayload,
        IngestionPath ingestionPath = IngestionPath.FixtureReplay,
        string sourceId = "fixture-local-acars")
    {
        var receivedAtUtc = DateTimeOffset.UnixEpoch;

        return new AcarsParserInput(
            rawPayload,
            1,
            receivedAtUtc,
            new IngestionProvenance(
                ingestionPath,
                sourceId,
                "ACARS test source",
                ingestionPath == IngestionPath.ImportedDecodedData ? "External ACARS App" : null,
                "fixture/acars-v1",
                "AcarsMessageParserTests",
                "0.1.0",
                receivedAtUtc,
                receivedAtUtc,
                "fixtures/acars/local-vhf/test.txt"),
            136.800,
            "VHF ACARS");
    }
}