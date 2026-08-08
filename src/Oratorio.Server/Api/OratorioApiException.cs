namespace Oratorio.Server.Api;

public sealed class OratorioApiException(
    int statusCode,
    string code,
    string message,
    IReadOnlyDictionary<string, object?>? details = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Details { get; } = details;

    public ErrorResponse ToResponse() => new(new ErrorBody(Code, Message, Details));

    public static OratorioApiException Validation(string message, IReadOnlyDictionary<string, object?>? details = null) =>
        new(StatusCodes.Status400BadRequest, "validationFailed", message, details);

    public static OratorioApiException ItemNotFound(string source, string externalId) =>
        new(
            StatusCodes.Status404NotFound,
            "itemNotFound",
            "The requested item does not exist.",
            new Dictionary<string, object?> { ["source"] = source, ["externalId"] = externalId });

    public static OratorioApiException RunNotFound(string runId) =>
        new(
            StatusCodes.Status404NotFound,
            "runNotFound",
            "The requested run does not exist.",
            new Dictionary<string, object?> { ["runId"] = runId });

    public static OratorioApiException Conflict(string code, string message, IReadOnlyDictionary<string, object?>? details = null) =>
        new(StatusCodes.Status409Conflict, code, message, details);
}
