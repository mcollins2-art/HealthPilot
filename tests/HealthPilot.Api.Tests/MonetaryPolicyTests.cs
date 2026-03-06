using HealthPilot.Api.Services;
using Xunit;

namespace HealthPilot.Api.Tests;

public class MonetaryPolicyTests
{
    [Theory]
    [InlineData(1.005, 1.01)]    // AwayFromZero: rounds up
    [InlineData(1.004, 1.00)]    // AwayFromZero: rounds down
    [InlineData(-1.005, -1.01)]  // AwayFromZero: rounds away from zero (more negative)
    [InlineData(0.000, 0.00)]
    [InlineData(100.999, 101.00)]
    [InlineData(0.335, 0.34)]
    [InlineData(0.334, 0.33)]
    public void Round_AppliesAwayFromZeroRounding(decimal input, decimal expected)
    {
        var result = MonetaryPolicy.Round(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void PolicyVersion_HasExpectedFormat()
    {
        Assert.False(string.IsNullOrWhiteSpace(MonetaryPolicy.PolicyVersion));
        Assert.Contains(".", MonetaryPolicy.PolicyVersion);
    }

    [Fact]
    public void Scale_IsTwoDecimalPlaces()
    {
        Assert.Equal(2, MonetaryPolicy.Scale);
    }

    [Fact]
    public void RoundingMode_IsAwayFromZero()
    {
        Assert.Equal(MidpointRounding.AwayFromZero, MonetaryPolicy.RoundingMode);
    }
}
