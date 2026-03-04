using System.ComponentModel.DataAnnotations;

namespace HealthPilot.Api.Middleware;

public sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return await next(context);
        }

        var validationContext = new ValidationContext(request);
        var validationResults = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
        {
            return await next(context);
        }

        var errors = validationResults
            .Where(x => x != ValidationResult.Success)
            .SelectMany(result =>
            {
                var message = result?.ErrorMessage ?? "Invalid value.";
                return result?.MemberNames?.Any() == true
                    ? result.MemberNames.Select(member => new KeyValuePair<string, string>(member, message))
                    : [new KeyValuePair<string, string>(string.Empty, message)];
            })
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Value).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return Results.ValidationProblem(errors);
    }
}

public static class ValidationEndpointFilterExtensions
{
    public static RouteHandlerBuilder WithDtoValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class
    {
        return builder.AddEndpointFilter<ValidationEndpointFilter<TRequest>>();
    }
}
