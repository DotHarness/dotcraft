namespace DotCraft.Security;

public static class ApprovalContextScope
{
    private static readonly AsyncLocal<ApprovalContext?> CurrentContext = new();

    public static ApprovalContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }

    public static IDisposable Set(ApprovalContext context)
    {
        CurrentContext.Value = context;
        return new ContextScope();
    }

    private sealed class ContextScope : IDisposable
    {
        public void Dispose()
        {
            CurrentContext.Value = null;
        }
    }
}
