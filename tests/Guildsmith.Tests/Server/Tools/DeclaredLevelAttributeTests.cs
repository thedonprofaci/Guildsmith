using Guildsmith.Core.Guards;
using Guildsmith.Server.Tools;

namespace Guildsmith.Tests.Server.Tools;

public sealed class DeclaredLevelAttributeTests
{
    [Theory]
    [InlineData(ToolLevel.Read)]
    [InlineData(ToolLevel.Write)]
    [InlineData(ToolLevel.Destructive)]
    public void Level_is_the_level_the_attribute_was_declared_with(ToolLevel level)
    {
        Assert.Equal(level, new DeclaredLevelAttribute(level).Level);
    }
}
