namespace DotCraft.Imaging;

internal sealed class BudgetMemoryStream(ImageBudget budget) : MemoryStream
{
    private bool _released;

    public override int Capacity
    {
        get => base.Capacity;
        set
        {
            var previous = base.Capacity;
            if (value == previous)
                return;
            budget.Reserve(value);
            try
            {
                base.Capacity = value;
            }
            catch
            {
                budget.Release(value);
                throw;
            }
            budget.Release(previous);
        }
    }

    public override byte[] ToArray()
    {
        budget.Reserve(Length);
        return base.ToArray();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_released)
        {
            budget.Release(base.Capacity);
            _released = true;
        }
        base.Dispose(disposing);
    }
}
