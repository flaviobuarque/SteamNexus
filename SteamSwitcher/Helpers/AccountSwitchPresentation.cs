using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Helpers;

// Keeps a user-selected account visible while Steam restarts and publishes its session.
public sealed class AccountSwitchPresentation(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private SteamAccount? _target;
    private DateTimeOffset? _confirmationDeadline;
    public long Revision { get; private set; }

    public void Begin(SteamAccount target)
    {
        Revision++;
        _target = target;
        _confirmationDeadline = null;
    }

    public void Complete() => _confirmationDeadline = _clock.GetUtcNow().AddMinutes(2);

    public void Cancel()
    {
        Revision++;
        _target = null;
        _confirmationDeadline = null;
    }

    public SteamAccount? Resolve(SteamAccount? observed)
    {
        if (_target is null) return observed;
        if (_confirmationDeadline is null) return _target;
        if (observed?.UniqueKey == _target.UniqueKey || _clock.GetUtcNow() >= _confirmationDeadline)
        {
            Cancel();
            return observed;
        }
        return _target;
    }
}
