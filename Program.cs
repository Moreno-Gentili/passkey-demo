using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PasskeyDemo.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Authorization;

WebApplication app = CreateAndConfigureWebAppWithIdentity(args); // Configurazione standard, non molto interessante

// Qui inizia la parte interessante: gli endpoint per login e gestione delle passkey

app.MapPost("/login-with-credentials", LoginWithCredentials).AllowAnonymous();
app.MapPost("/passkeys/creation-options", GetPasskeyCreationOptionsForLoggedInUser).RequireAuthorization();
app.MapGet("/passkeys", GetPasskeysForLoggedInUser).RequireAuthorization();
app.MapPost("/passkeys", CreatePasskeyForLoggedInUser).RequireAuthorization();
app.MapDelete("/passkeys", DeletePasskeyForLoggedInUser).RequireAuthorization();
app.MapPost("/login-with-passkey", LoginWithPasskey).AllowAnonymous();
app.MapGet("/passkeys/request-options", GetPasskeyRequestOptionsForLoggedInUser).AllowAnonymous();
app.MapPost("/logout", Logout).RequireAuthorization();

// Login "normale" con email e password
async Task<Results<Ok<LoginResult>, UnauthorizedHttpResult>> LoginWithCredentials(
    LoginWithCredentialsCommand command,
    [FromServices] UserManager<ApplicationUser> userManager,
    [FromServices] SignInManager<ApplicationUser> signInManager)
{
    ApplicationUser? user = await userManager.FindByEmailAsync(command.Email);
    if (user is not null)
    {
        var result = await signInManager.PasswordSignInAsync(user, command.Password, isPersistent: false, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            return TypedResults.Ok(new LoginResult(user.UserName));
        }
    }

    return TypedResults.Unauthorized();
}

// Elenca del passkey attualmente associate all'utente
async Task<Ok<PasskeyDescriptor[]>> GetPasskeysForLoggedInUser(
    HttpContext httpContext,
    [FromServices] UserManager<ApplicationUser> userManager)
{
        ApplicationUser user = await userManager.GetUserAsync(httpContext.User) ??
                               throw new InvalidOperationException("Could not find user");

        IList<UserPasskeyInfo> passkeys = await userManager.GetPasskeysAsync(user);
        PasskeyDescriptor[] passkeysDescriptors = passkeys.Select(p => 
                            new PasskeyDescriptor(
                                Id: Convert.ToBase64String(p.CredentialId),
                                Name: p.Name)).ToArray();

        return TypedResults.Ok(passkeysDescriptors);
}

// Fornisce le opzioni (in formato json) che il client userà per fare il login con passkey
// Trovi un esempio del json nel readme
async Task<IResult> GetPasskeyRequestOptionsForLoggedInUser(
    HttpContext httpContext,
    [FromServices] UserManager<ApplicationUser> userManager,
    [FromServices] SignInManager<ApplicationUser> signInManager)
{
    string jsonOptions = await signInManager.MakePasskeyRequestOptionsAsync(null);
    return Results.Content(jsonOptions, "application/json");
}

// Il frontend invia la sua risposta alla challenge, che viene verificata e la passkey viene salvata nel db
async Task<Results<NoContent, BadRequest, InternalServerError>> CreatePasskeyForLoggedInUser(
    HttpContext httpContext,
    CreatePasskeyCommand command,
    [FromServices] UserManager<ApplicationUser> userManager,
    [FromServices] SignInManager<ApplicationUser> signInManager)
{
    ApplicationUser? user = await userManager.GetUserAsync(httpContext.User) ??
                            throw new InvalidOperationException("Could not find user");

    // Verifichiamo che il client abbia risposto correttamente alla challenge
    PasskeyAttestationResult result = await signInManager.PerformPasskeyAttestationAsync(command.JsonCredential);
    if (!result.Succeeded || result.Passkey is null)
    {
        return TypedResults.BadRequest();
    }

    result.Passkey.Name = command.PasskeyName;
    IdentityResult identityResult = await userManager.AddOrUpdatePasskeyAsync(user, result.Passkey);
    if (identityResult.Succeeded)
    {
        return TypedResults.NoContent();
    }
    else
    {
        return TypedResults.InternalServerError();
    }
}

// Rimuoviamo una passkey memorizzata nel database
async Task<Results<NoContent, BadRequest, InternalServerError>> DeletePasskeyForLoggedInUser(
    HttpContext httpContext,
    [FromBody] DeletePasskeyCommand command,
    [FromServices] UserManager<ApplicationUser> userManager)
{
    ApplicationUser? user = await userManager.GetUserAsync(httpContext.User) ??
                            throw new InvalidOperationException("Could not find user");

    byte[] binaryCredentialId = Convert.FromBase64String(command.PasskeyId);
    UserPasskeyInfo? info = await userManager.GetPasskeyAsync(user, binaryCredentialId);
    if (info is null)
    {
        return TypedResults.BadRequest();
    }

    IdentityResult result = await userManager.RemovePasskeyAsync(user, binaryCredentialId);
    if (result.Succeeded)
    {
        return TypedResults.NoContent();
    }
    else
    {
        return TypedResults.InternalServerError();
    }
}


// Fornisce le opzioni (in formato json) che il client userà per creare una nuova passkey
// Tali opzioni contengono: una challenge, una chiave pubblica, gli algoritmi disponibili e l'id e il nome dell'utente. Nel readme c'è un esempio.
async Task<IResult> GetPasskeyCreationOptionsForLoggedInUser(
    HttpContext httpContext,
    [FromBody] GetCreationOptionsCommand command,
    [FromServices] UserManager<ApplicationUser> userManager,
    [FromServices] SignInManager<ApplicationUser> signInManager)
{
    ApplicationUser user = await userManager.GetUserAsync(httpContext.User) ??
                           throw new InvalidOperationException("Could not find user");

    string username = user.UserName ??
                      throw new InvalidOperationException("The user must have a username");

    PasskeyUserEntity userEntity = new()
    {
        Id = user.Id,
        DisplayName = $"{username} ({command.Name})",
        Name = $"{username} ({command.Name})"
    };

    string jsonOptions = await signInManager.MakePasskeyCreationOptionsAsync(userEntity);
    return Results.Content(jsonOptions, "application/json");
}


// Login con passkey (username non richiesto)
async Task<Results<Ok<LoginResult>, UnauthorizedHttpResult>> LoginWithPasskey(
    LoginWithPasskeyCommand command,
    [FromServices] SignInManager<ApplicationUser> signInManager)
{
    var result = await signInManager.PerformPasskeyAssertionAsync(command.JsonCredential);
    if (result.Succeeded && result.User is not null)
    {
        await signInManager.SignInAsync(result.User, isPersistent: false);
        return TypedResults.Ok(new LoginResult(result.User.UserName));
    }

    return TypedResults.Unauthorized();
}

// Eliminiamo il cookie di autenticazione, che era stato emesso sia per il login con credenziali sia per quello con passkey
async Task<NoContent> Logout(
    [FromServices] SignInManager<ApplicationUser> signInManager)
{
    await signInManager.SignOutAsync();
    return TypedResults.NoContent();
}

app.Run();

static WebApplication CreateAndConfigureWebAppWithIdentity(string[] args)
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    }).AddIdentityCookies();

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    });

    string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(connectionString));

    builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.RespectNullableAnnotations = true;
        options.SerializerOptions.RespectRequiredConstructorParameters = true;
    });

    WebApplication app = builder.Build();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseAuthentication();
    app.UseAuthorization();

    return app;
}


public record LoginWithCredentialsCommand(string Email, string Password);
public record LoginWithPasskeyCommand(string JsonCredential);
public record LoginResult(string? Username);
public record GetCreationOptionsCommand(string Name);
public record CreatePasskeyCommand(string PasskeyName, string JsonCredential);
public record DeletePasskeyCommand(string PasskeyId);
public record PasskeyDescriptor(string Id, string? Name);