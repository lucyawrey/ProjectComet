using System.Text.Json;
using Comet.Data.Characters;
using Comet.Protocol.Login;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Comet.Login;

internal static class LoginEndpoints
{
    public const string SignInPolicy = "sign-in";
    public const string GuestPolicy = "guest";

    public static void Map<TCharacter>(IEndpointRouteBuilder app)
        where TCharacter : Character
    {
        app.MapPost(
                LoginPaths.Guest,
                async (GuestRequest<JsonElement> request, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
                    Session(await accounts.CreateGuestAsync(request.Name, request.Character, cancel))
            )
            .RequireRateLimiting(GuestPolicy);

        app.MapPost(
                LoginPaths.SignIn,
                async (SignInRequest request, HttpContext http, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
                    Session(await accounts.SignInAsync(request.Username, request.Password, Address(http), restore: false, cancel))
            )
            .RequireRateLimiting(SignInPolicy);

        app.MapPost(
                LoginPaths.SignInWithCode,
                async (RecoveryCodeSignInRequest request, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
                    Session(await accounts.SignInWithCodeAsync(request.Code, restore: false, cancel))
            )
            .RequireRateLimiting(SignInPolicy);

        app.MapPost(
                LoginPaths.Restore,
                async (SignInRequest request, HttpContext http, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
                    Session(
                        request.RecoveryCode is { } code
                            ? await accounts.SignInWithCodeAsync(code, restore: true, cancel)
                            : await accounts.SignInAsync(request.Username, request.Password, Address(http), restore: true, cancel)
                    )
            )
            .RequireRateLimiting(SignInPolicy);

        app.MapGet(
            LoginPaths.Me,
            async (HttpContext http, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
            {
                var (signedIn, error) = await accounts.AuthenticateAsync(http, cancel);
                return signedIn == null ? Problem(error!) : Results.Ok(await accounts.DescribeAsync(signedIn, cancel));
            }
        );

        app.MapPost(
            LoginPaths.Password,
            async (PasswordRequest request, HttpContext http, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
            {
                var (signedIn, error) = await accounts.AuthenticateAsync(http, cancel);
                error ??= await accounts.SetPasswordAsync(signedIn!, request.Password, cancel);
                return error == null ? Results.NoContent() : Problem(error);
            }
        );

        app.MapPost(
            LoginPaths.RecoveryCode,
            async (HttpContext http, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
            {
                var (signedIn, error) = await accounts.AuthenticateAsync(http, cancel);
                if (signedIn == null)
                {
                    return Problem(error!);
                }

                var (code, refused) = await accounts.MakeRecoveryCodeAsync(signedIn, cancel);
                return code == null ? Problem(refused!) : Results.Ok(new RecoveryCodeResponse { Code = code });
            }
        );

        app.MapPost(
            LoginPaths.SignOut,
            async (HttpContext http, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
            {
                var (signedIn, error) = await accounts.AuthenticateAsync(http, cancel);
                if (signedIn == null)
                {
                    return Problem(error!);
                }

                await accounts.SignOutAsync(signedIn, cancel);
                return Results.NoContent();
            }
        );

        app.MapPost(
            LoginPaths.Rename,
            async (RenameRequest request, HttpContext http, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
            {
                var (signedIn, error) = await accounts.AuthenticateAsync(http, cancel);
                error ??= await accounts.RenameAsync(signedIn!, request.Name, cancel);
                return error == null ? Results.NoContent() : Problem(error);
            }
        );

        app.MapPost(
            LoginPaths.Ticket,
            async (HttpContext http, LoginAccounts<TCharacter> accounts, CancellationToken cancel) =>
            {
                var (signedIn, error) = await accounts.AuthenticateAsync(http, cancel);
                if (signedIn == null)
                {
                    return Problem(error!);
                }

                var (ticket, refused) = await accounts.TicketAsync(signedIn, cancel);
                return ticket == null ? Problem(refused!) : Results.Ok(ticket);
            }
        );
    }

    public static IResult Problem(LoginError error) =>
        Results.Problem(statusCode: error.Status, title: error.Code, extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    /// <summary>Use after the forwarded-headers middleware, so a trusted proxy's client is seen.</summary>
    public static string Address(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static IResult Session((string? Token, LoginError? Error) result) =>
        result.Token == null ? Problem(result.Error!) : Results.Ok(new SessionResponse { Token = result.Token });
}
