using FlowOnRails.AspNetCore.Translation;

namespace FlowOnRails.AspNetCore.Sample;

public static partial class ApiProblems
{
    [HttpProblem(StatusCodes.Status404NotFound)]
    [AutomaticExtensions]
    [UseDataField]
    private static void MissingUser(ProblemBuilder<UserNotFound> problem)
    {
        problem.Type("https://example.test/problems/user-not-found");
        problem.Title("User not found");
    }

    [HttpProblem(StatusCodes.Status422UnprocessableEntity)]
    private static void InvalidId(ProblemBuilder<InvalidUserId> problem)
    {
        problem.Type("https://example.test/problems/invalid-user-id");
        problem.Title("Invalid user identifier");
        problem.Extensions((error, extensions) => extensions["invalidId"] = error.Id);
    }

    [GenericProblem]
    private static void Generic(GenericProblemBuilder problem)
    {
        problem.Type("https://example.test/problems/unexpected");
        problem.Title("Unable to complete the operation");
        problem.Extensions((context, extensions) => extensions["requestId"] = context.TraceIdentifier);
    }
}
