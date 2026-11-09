using ButikkPilot.Contracts;

namespace ButikkPilot.Api.Hosting;

public static class ChatRequestValidator
{
    public static Dictionary<string, string[]> Validate(ChatRequest? request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request is null)
        {
            errors["request"] = ["A JSON body is required."];
            return errors;
        }

        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message))
        {
            errors[nameof(request.Message)] = ["Message must not be empty."];
        }
        else if (message.Length > ChatLimits.MaxMessageLength)
        {
            errors[nameof(request.Message)] = [$"Message must be at most {ChatLimits.MaxMessageLength} characters."];
        }
        else if (message.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
        {
            errors[nameof(request.Message)] = ["Message contains invalid control characters."];
        }

        return errors;
    }
}
