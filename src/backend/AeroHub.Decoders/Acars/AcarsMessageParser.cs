using System.Text.RegularExpressions;
using AeroHub.Contracts;

namespace AeroHub.Decoders.Acars;

public sealed partial class AcarsMessageParser : IAcarsMessageParser
{
    public NormalizedAviationMessage Parse(AcarsParserInput input)
    {
        var rawPayload = input.RawPayload ?? string.Empty;
        var trimmedPayload = rawPayload.Trim();
        var warnings = new List<AviationWarning>();

        if (trimmedPayload.Length == 0)
        {
            warnings.Add(new AviationWarning("ACARS_EMPTY_PAYLOAD", "ACARS payload is empty.", "Error"));
            return CreateMessage(input, null, null, null, "Malformed", null, ParserConfidence.Unknown, "Malformed ACARS payload", rawPayload, null, warnings);
        }

        if (ContainsUnsupportedCharacters(rawPayload))
        {
            warnings.Add(new AviationWarning("ACARS_UNSUPPORTED_CHARACTER", "Payload contains non-printable characters; raw text was preserved.", "Warning"));
        }

        var tokens = TokenExpression().Matches(trimmedPayload).Select(match => match.Value).ToArray();
        var label = tokens.Length > 0 && IsLabel(tokens[0]) ? tokens[0].ToUpperInvariant() : null;

        if (label is null)
        {
            warnings.Add(new AviationWarning("ACARS_MISSING_LABEL", "Payload does not start with a valid two-character ACARS label.", "Warning"));
            return CreateMessage(input, null, null, null, "Unknown", ExtractAircraftIdentifier(tokens), ParserConfidence.Unknown, "ACARS message with missing label", rawPayload, trimmedPayload, warnings);
        }

        var secondToken = tokens.Skip(1).FirstOrDefault();
        var sublabel = IsSublabel(secondToken) ? secondToken : null;
        var preamble = IsPreamble(secondToken) ? secondToken : null;
        var aircraftIdentifier = ExtractAircraftIdentifier(tokens);
        var profile = Classify(label, sublabel, preamble);

        if (profile.Warning is not null)
        {
            warnings.Add(profile.Warning);
        }

        var summary = BuildSummary(label, sublabel, preamble, profile.Category, aircraftIdentifier, profile.Confidence);
        var unconsumedText = tokens.Length > 1 ? trimmedPayload[trimmedPayload.IndexOf(tokens[1], StringComparison.Ordinal)..] : null;

        return CreateMessage(input, label, sublabel, preamble, profile.Category, aircraftIdentifier, profile.Confidence, summary, rawPayload, unconsumedText, warnings);
    }

    private static NormalizedAviationMessage CreateMessage(
        AcarsParserInput input,
        string? label,
        string? sublabel,
        string? preamble,
        string category,
        string? aircraftIdentifier,
        ParserConfidence confidence,
        string summary,
        string rawPayload,
        string? unconsumedText,
        IReadOnlyList<AviationWarning> warnings)
    {
        return new NormalizedAviationMessage(
            $"{input.Provenance.SourceId}-acars-{input.Sequence}",
            input.Sequence,
            input.ReceivedAtUtc,
            AviationMessageKind.Acars,
            summary,
            new AcarsMessageDetails(label, sublabel, preamble, category, rawPayload, unconsumedText),
            aircraftIdentifier,
            input.Transport,
            input.FrequencyMHz,
            confidence,
            input.Provenance,
            rawPayload,
            warnings);
    }

    private static (string Category, ParserConfidence Confidence, AviationWarning? Warning) Classify(string label, string? sublabel, string? preamble)
    {
        return label switch
        {
            "2L" => ("OOOI/position candidate", ParserConfidence.Candidate, null),
            "44" => ("OOOI/position candidate", ParserConfidence.Candidate, null),
            "4J" => ("Position/weather candidate", ParserConfidence.Candidate, null),
            "40" => ("Operational/free text candidate", ParserConfidence.Candidate, null),
            "H1" when string.Equals(sublabel, "POS", StringComparison.OrdinalIgnoreCase) => ("Position candidate", ParserConfidence.Candidate, null),
            "H1" when string.Equals(preamble, "#DFB", StringComparison.OrdinalIgnoreCase) => ("Weather/operational observed", ParserConfidence.Observed, null),
            "H1" => ("H1 application observed", ParserConfidence.Observed, null),
            "SQ" => ("Squitter/status observed", ParserConfidence.Observed, null),
            "MA" => ("Media/MIAM observed", ParserConfidence.Observed, null),
            _ => ("Unknown", ParserConfidence.Unknown, new AviationWarning("ACARS_UNKNOWN_LABEL", $"ACARS label '{label}' is not recognized by the starter parser.", "Info"))
        };
    }

    private static string BuildSummary(string label, string? sublabel, string? preamble, string category, string? aircraftIdentifier, ParserConfidence confidence)
    {
        var discriminator = sublabel ?? preamble;
        var target = aircraftIdentifier is null ? "unknown aircraft" : aircraftIdentifier;
        var labelText = discriminator is null ? label : $"{label}/{discriminator}";
        return $"ACARS {labelText} {category} for {target} ({confidence})";
    }

    private static bool IsLabel(string token)
    {
        return token.Length == 2 && token.All(char.IsLetterOrDigit);
    }

    private static bool IsSublabel(string? token)
    {
        return token is { Length: >= 2 and <= 4 } && token.All(char.IsLetterOrDigit);
    }

    private static bool IsPreamble(string? token)
    {
        return token is { Length: >= 2 } && token[0] == '#';
    }

    private static string? ExtractAircraftIdentifier(IEnumerable<string> tokens)
    {
        return tokens.FirstOrDefault(token => RegistrationExpression().IsMatch(token) || IcaoExpression().IsMatch(token));
    }

    private static bool ContainsUnsupportedCharacters(string text)
    {
        return text.Any(character => character is < ' ' and not '\r' and not '\n' and not '\t' || character > '~');
    }

    [GeneratedRegex("\\S+")]
    private static partial Regex TokenExpression();

    [GeneratedRegex("^N[A-Z0-9]{2,5}$", RegexOptions.IgnoreCase)]
    private static partial Regex RegistrationExpression();

    [GeneratedRegex("^[A-F0-9]{6}$", RegexOptions.IgnoreCase)]
    private static partial Regex IcaoExpression();
}