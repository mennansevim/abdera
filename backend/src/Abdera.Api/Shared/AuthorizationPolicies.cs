using Abdera.Api.Modules.Auth.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Abdera.Api.Shared;

// docs/04-permissions.md - rol bazlı politikalar burada tek yerden tanımlanır,
// controller/handler içinde "if (role == ...)" tekrarlanmaz.
public static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string TeacherOrAdmin = "TeacherOrAdmin";
    public const string GuardianOnly = "GuardianOnly";

    public static void AddAbderaAuthorizationPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(AdminOnly, policy => policy.RequireRole(UserRole.Admin.ToString()));
        options.AddPolicy(TeacherOrAdmin, policy => policy.RequireRole(UserRole.Teacher.ToString(), UserRole.Admin.ToString()));
        options.AddPolicy(GuardianOnly, policy => policy.RequireRole(UserRole.Guardian.ToString()));
    }
}
