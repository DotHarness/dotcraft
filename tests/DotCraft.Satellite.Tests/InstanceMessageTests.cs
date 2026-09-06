using DotCraft.Satellite.Services;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class InstanceMessageTests
{
    [Fact]
    public void Join_EncodesOneLine_TheCliCanWrite()
    {
        var encoded = InstanceMessage.Join("http://ann-pc:47600/i/inv_abcdefgh").Encode();

        Assert.Equal(@"{""kind"":""join"",""url"":""http://ann-pc:47600/i/inv_abcdefgh""}", encoded);
        Assert.DoesNotContain('\n', encoded);
    }

    [Fact]
    public void Show_CarriesNoUrl()
    {
        Assert.Equal(@"{""kind"":""show""}", InstanceMessage.Show().Encode());
    }

    [Fact]
    public void Decode_RoundTripsBothKinds()
    {
        var join = InstanceMessage.Join("http://ann-pc:47600/i/inv_abcdefgh");

        Assert.Equal(join, InstanceMessage.Decode(join.Encode()));
        Assert.Equal(InstanceMessage.Show(), InstanceMessage.Decode(InstanceMessage.Show().Encode()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData(@"{""url"":""http://ann-pc:47600/i/inv_abcdefgh""}")]
    public void Decode_RefusesAnythingWithoutAKind(string? line)
    {
        Assert.Null(InstanceMessage.Decode(line));
    }

    [Fact]
    public void PipeName_IsScopedToTheSignedInUser()
    {
        Assert.StartsWith("DotCraft.Satellite.", SingleInstanceGate.PipeName, StringComparison.Ordinal);
        Assert.True(SingleInstanceGate.PipeName.Length > "DotCraft.Satellite.".Length);
    }
}
