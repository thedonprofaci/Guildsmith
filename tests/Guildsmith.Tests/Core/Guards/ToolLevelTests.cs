using Guildsmith.Core.Guards;

namespace Guildsmith.Tests.Core.Guards;

public sealed class ToolLevelTests
{
    [Fact]
    public void ToolLevel_has_exactly_the_three_levels_of_spec_section_6_2()
    {
        Assert.Equal(["Read", "Write", "Destructive"], Enum.GetNames<ToolLevel>());
    }
}
