using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using SarawakBizOps.Api.DTOs;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Filters;

/// <summary>
/// Global: any action argument that is an <see cref="IRowVersionRequest"/> must carry a valid base64 rowVersion, or
/// the request is a 400 ProblemDetails and the action never runs. Replaces the same check repeated in every
/// mutating action. Runs after [ApiController]'s model validation, so a missing body is still reported by that.
/// </summary>
public sealed class RowVersionFilter : IActionFilter
{
    public const string Message = "A valid rowVersion is required.";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is IRowVersionRequest request && !RowVersionCodec.TryParse(request.RowVersion, out _))
            {
                var problem = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
                    .CreateProblemDetails(context.HttpContext, StatusCodes.Status400BadRequest, "Bad request", detail: Message);
                context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
                return;
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
