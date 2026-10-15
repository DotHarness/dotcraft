namespace DotCraft.Tools.BackgroundTerminals;

/// <summary>
/// What the model has read from one terminal and whether its completion still owes a notice.
/// Each transition returns true when the caller must raise the notice, so it is raised once in any order.
/// </summary>
internal sealed class TerminalModelReadState
{
    private readonly object _sync = new();
    private long _cursor;
    private int _waiters;
    private bool _handedOff;
    private bool _completed;
    private bool _settled;

    public long Cursor
    {
        get
        {
            lock (_sync)
                return _cursor;
        }
    }

    public void Settle()
    {
        lock (_sync)
            _settled = true;
    }

    public void Read(long end, bool final)
    {
        lock (_sync)
        {
            _cursor = Math.Max(_cursor, end);
            if (final)
                _settled = true;
        }
    }

    public bool HandOff(long end, bool running, bool armed)
    {
        lock (_sync)
        {
            _cursor = Math.Max(_cursor, end);
            if (!running)
            {
                _settled = true;
                return false;
            }
            if (!armed)
                return false;
            _handedOff = true;
            return TryClaimNotice();
        }
    }

    public bool Complete(bool suppressed)
    {
        lock (_sync)
        {
            _completed = true;
            return !suppressed && _waiters == 0 && TryClaimNotice();
        }
    }

    public void EnterWait()
    {
        lock (_sync)
            _waiters++;
    }

    public bool LeaveWait(bool suppressed)
    {
        lock (_sync)
        {
            _waiters--;
            return !suppressed && _waiters == 0 && TryClaimNotice();
        }
    }

    public bool OwesStallNotice(bool suppressed)
    {
        lock (_sync)
            return !suppressed && _handedOff && !_settled && _waiters == 0;
    }

    private bool TryClaimNotice()
    {
        if (!_handedOff || !_completed || _settled)
            return false;
        _settled = true;
        return true;
    }
}
