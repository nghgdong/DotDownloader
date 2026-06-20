using DM.Core;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class ProjectInfoTests
{
    [Fact]
    public void ProjectName_Is_DotDownloader()
    {
        ProjectInfo.Name.Should().Be("DotDownloader");
    }
}
