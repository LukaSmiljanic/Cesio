namespace Shared.Kernel.Tests;

public sealed class ResultTests
{
    private static readonly Error Conflict = new("conflict_state", "Invalid transition.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(Conflict);

        Assert.True(result.IsFailure);
        Assert.Same(Conflict, result.Error);
    }

    [Fact]
    public void Success_with_value_exposes_the_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_with_value_type_has_no_value()
    {
        var result = Result.Failure<int>(Conflict);

        Assert.True(result.IsFailure);
        Assert.Same(Conflict, result.Error);
        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("conflict_state", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Failures_require_an_error()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));
    }

    [Theory]
    [InlineData("validation_failed")]
    [InlineData("hold_active")]
    [InlineData("a")]
    [InlineData("error2")]
    public void Error_codes_are_snake_case(string code) =>
        Assert.Equal(code, new Error(code, "message").Code);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Conflict")]
    [InlineData("_conflict")]
    [InlineData("2conflict")]
    [InlineData("conflict-state")]
    [InlineData("conflictState")]
    [InlineData("conflict state")]
    [InlineData("čvor")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Error_codes_that_are_not_snake_case_are_rejected(string? code) =>
        Assert.Throws<ArgumentException>(() => new Error(code!, "message"));

    [Fact]
    public void Error_requires_a_message() =>
        Assert.Throws<ArgumentNullException>(() => new Error("conflict_state", null!));
}
