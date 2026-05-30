using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace HighPingKicker.Models;

public class PlayerInfo
{
    public Timer? Timer { get; set; }
    public bool IsInGracePeriod { get; set; } = true;
    public bool IsAdmin { get; set; } = false;
    public bool IsWhitelisted { get; set; } = false;
    public int WarningsGiven { get; set; } = 0;
    public bool IsImmune
    {
        get => IsAdmin || IsInGracePeriod || IsWhitelisted;
    }
}