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

        var errors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var result in validationResults)
        {
            var message = result?.ErrorMessage ?? "Invalid value.";
            var members = result?.MemberNames?.Any() == true ? result.MemberNames : [string.Empty];
            foreach (var member in members)
            {
                if (!errors.TryGetValue(member, out var messages))
                {
                    messages = new HashSet<string>(StringComparer.Ordinal);
                    errors[member] = messages;
                }

                messages.Add(message);
            }
        }

        return Results.ValidationProblem(errors.ToDictionary(x => x.Key, x => x.Value.ToArray()));
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
