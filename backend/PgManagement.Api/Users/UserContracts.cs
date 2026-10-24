namespace PgManagement.Api.Users;

/// <summary>Registration details returned after an account is created.</summary>
public sealed record RegistrationResponse(string Email, string FullName, string Role);