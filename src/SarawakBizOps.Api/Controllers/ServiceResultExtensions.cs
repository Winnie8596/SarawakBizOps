using Microsoft.AspNetCore.Mvc;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Controllers;

public static class ServiceResultExtensions
{
    /// <summary>Maps a failed <see cref="ServiceResult"/> to an RFC 7807 response.</summary>
    public static ObjectResult ToProblem(this ControllerBase controller, ServiceResult result)
    {
        var (status, title) = result.ErrorKind switch
        {
            ServiceErrorKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ServiceErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status400BadRequest, "Bad request")
        };

        return controller.Problem(statusCode: status, title: title, detail: result.ErrorMessage);
    }
}
