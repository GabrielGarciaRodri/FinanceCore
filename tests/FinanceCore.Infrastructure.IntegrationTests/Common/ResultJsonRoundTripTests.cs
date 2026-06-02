using System.Text.Json;
using FinanceCore.Application.Common.Models;
using FluentAssertions;
using Xunit;

namespace FinanceCore.Infrastructure.IntegrationTests.Common;

/// <summary>
/// Regression tests para asegurar que Result&lt;T&gt; sigue siendo JSON-round-trippable.
/// El CachingBehavior depende de esto: serializa a Redis y deserializa en cache HIT.
/// Si esto se rompe, el dashboard refresh (y cualquier query cacheable) tira 500.
/// </summary>
public class ResultJsonRoundTripTests
{
    private record SampleDto(string Name, int Count, decimal Amount);

    private static readonly JsonSerializerOptions JsonOptions = new();

    [Fact]
    public void Result_Success_With_Reference_Type_Roundtrips()
    {
        var original = Result<SampleDto>.Success(new SampleDto("hello", 42, 1234.56m));

        var json = JsonSerializer.SerializeToUtf8Bytes(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Result<SampleDto>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.IsSuccess.Should().BeTrue();
        deserialized.IsFailure.Should().BeFalse();
        deserialized.Value.Should().NotBeNull();
        deserialized.Value!.Name.Should().Be("hello");
        deserialized.Value.Count.Should().Be(42);
        deserialized.Value.Amount.Should().Be(1234.56m);
        deserialized.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Result_Failure_Roundtrips()
    {
        var original = Result<SampleDto>.Failure("something broke");

        var json = JsonSerializer.SerializeToUtf8Bytes(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Result<SampleDto>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.IsSuccess.Should().BeFalse();
        deserialized.IsFailure.Should().BeTrue();
        deserialized.Value.Should().BeNull();
        deserialized.Errors.Should().HaveCount(1);
        deserialized.Error.Should().Be("something broke");
    }

    [Fact]
    public void Result_Failure_With_Multiple_Errors_Roundtrips()
    {
        var original = Result<SampleDto>.Failure(new[] { "error1", "error2", "error3" });

        var json = JsonSerializer.SerializeToUtf8Bytes(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Result<SampleDto>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.IsFailure.Should().BeTrue();
        deserialized.Errors.Should().BeEquivalentTo(new[] { "error1", "error2", "error3" });
    }

    [Fact]
    public void Result_With_Collection_Value_Roundtrips()
    {
        var list = new List<SampleDto>
        {
            new("a", 1, 1m),
            new("b", 2, 2m)
        };
        var original = Result<IReadOnlyList<SampleDto>>.Success(list);

        var json = JsonSerializer.SerializeToUtf8Bytes(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Result<IReadOnlyList<SampleDto>>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.IsSuccess.Should().BeTrue();
        deserialized.Value.Should().NotBeNull();
        deserialized.Value!.Should().HaveCount(2);
        deserialized.Value![0].Name.Should().Be("a");
        deserialized.Value![1].Count.Should().Be(2);
    }

    [Fact]
    public void GetValueOrThrow_On_Success_Returns_Value()
    {
        var result = Result<SampleDto>.Success(new SampleDto("x", 1, 1m));

        var value = result.GetValueOrThrow();

        value.Name.Should().Be("x");
    }

    [Fact]
    public void GetValueOrThrow_On_Failure_Throws()
    {
        var result = Result<SampleDto>.Failure("nope");

        var act = () => result.GetValueOrThrow();

        act.Should().Throw<InvalidOperationException>();
    }
}
