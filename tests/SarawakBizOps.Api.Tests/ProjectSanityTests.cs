using SarawakBizOps.Api.Models.Enums;

namespace SarawakBizOps.Api.Tests;

public class ProjectSanityTests
{
    [Fact]
    public void All_five_roles_from_the_design_are_defined()
    {
        Assert.Equal(5, AppRoles.All.Count());
        Assert.Contains(AppRoles.Technician, AppRoles.All);
    }
}
