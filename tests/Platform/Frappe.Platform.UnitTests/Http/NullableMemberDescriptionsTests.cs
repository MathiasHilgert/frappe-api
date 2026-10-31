using Frappe.Platform.Http.OpenApi;
using Microsoft.OpenApi;

namespace Frappe.Platform.UnitTests.Http;

public sealed class NullableMemberDescriptionsTests
{
    private const string Description = "The ISO 4217 code of the current tender, or null.";

    [Fact]
    public void A_nullable_member_takes_the_description_of_its_non_null_branch()
    {
        OpenApiSchema member = new()
        {
            OneOf =
            [
                new OpenApiSchema { Type = JsonSchemaType.Null },
                new OpenApiSchema { Type = JsonSchemaType.String, Description = Description },
            ],
        };

        NullableMemberDescriptions.Lift(member);

        member.Description.ShouldBe(Description);
    }

    [Fact]
    public void An_any_of_nullable_member_takes_the_description_of_its_non_null_branch()
    {
        OpenApiSchema member = new()
        {
            AnyOf =
            [
                new OpenApiSchema { Type = JsonSchemaType.String, Description = Description },
                new OpenApiSchema { Type = JsonSchemaType.Null },
            ],
        };

        NullableMemberDescriptions.Lift(member);

        member.Description.ShouldBe(Description);
    }

    [Fact]
    public void A_member_keeps_its_own_description()
    {
        OpenApiSchema member = new()
        {
            Description = "Its own.",
            OneOf =
            [
                new OpenApiSchema { Type = JsonSchemaType.Null },
                new OpenApiSchema { Type = JsonSchemaType.String, Description = Description },
            ],
        };

        NullableMemberDescriptions.Lift(member);

        member.Description.ShouldBe("Its own.");
    }

    [Fact]
    public void A_member_with_several_described_branches_stays_undescribed()
    {
        OpenApiSchema member = new()
        {
            OneOf =
            [
                new OpenApiSchema { Type = JsonSchemaType.String, Description = "A text." },
                new OpenApiSchema { Type = JsonSchemaType.Integer, Description = "A number." },
            ],
        };

        NullableMemberDescriptions.Lift(member);

        member.Description.ShouldBeNull();
    }
}
