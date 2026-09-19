using System.Reflection;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public class RolloutKindsTests
{
    [Fact]
    public void EveryKindNamesItsRecordPayloadProperty()
    {
        var policy = SessionJsonOptions.Default.PropertyNamingPolicy!;
        var payloads = typeof(ThreadRolloutRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => policy.ConvertName(property.Name))
            .Where(name => name is not ("kind" or "timestamp"))
            .ToHashSet(StringComparer.Ordinal);

        var derived = RolloutKinds.All.Select(RolloutKinds.PayloadProperty).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(payloads.Order(), derived.Order());
    }
}
