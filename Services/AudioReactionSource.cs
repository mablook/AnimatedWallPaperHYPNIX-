namespace AnimatedWallPaper.Services;

// Which reactive input(s) HYPNIX analyzes while "Audio reactive" is on. This is an explicit
// user choice, independent from the master on/off switch. "Microphone" must never open the
// system loopback, so a microphone-only reaction cannot be nudged by system playback.
internal enum AudioReactionSource
{
    System,               // system output only (WASAPI loopback)
    Microphone,           // microphone only
    SystemAndMicrophone   // both, mixed by taking the stronger band
}

internal static class AudioReactionSourceExtensions
{
    public static bool UsesSystem(this AudioReactionSource source)
        => source is AudioReactionSource.System or AudioReactionSource.SystemAndMicrophone;

    public static bool UsesMicrophone(this AudioReactionSource source)
        => source is AudioReactionSource.Microphone or AudioReactionSource.SystemAndMicrophone;

    public static AudioReactionSource Normalize(this AudioReactionSource source)
        => Enum.IsDefined(source) ? source : AudioReactionSource.System;
}

// Live microphone feedback for the Sound panel meter. It is a transient level reading only:
// no audio is recorded, replayed or transmitted. "Off" means the meter is not capturing.
internal enum MicrophoneStatus
{
    Off,
    NoMicrophone,   // no capture endpoint is present
    Unavailable,    // the selected/effective device could not be opened (unplugged/invalidated)
    AccessBlocked,  // Windows microphone privacy denied access to the input
    Listening,      // capturing, below the sound threshold (silence)
    SoundDetected   // capturing, level above the sound threshold
}

// A Windows audio endpoint offered in the Sound panel. Id is the stable MMDevice identifier used
// to persist an explicit selection; a null Id represents the "System default" entry that follows
// whichever device Windows currently designates as default.
internal sealed record AudioDeviceInfo(string? Id, string Name, bool IsDefault)
{
    public bool IsSystemDefaultEntry => Id is null;
}
