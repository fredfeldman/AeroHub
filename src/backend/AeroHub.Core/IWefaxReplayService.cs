using AeroHub.Contracts;

namespace AeroHub.Core;

public interface IWefaxReplayService
{
    event EventHandler<WefaxLineBatch>? LinesPublished;

    event EventHandler<WefaxDecoderState>? StateChanged;

    WefaxDecoderState GetState();

    Task<WefaxReplayResult> ReplayAsync(int lineCount, bool partial, CancellationToken cancellationToken = default);

    WefaxDecoderState SetControls(bool isInverted, double slantCorrection);
}