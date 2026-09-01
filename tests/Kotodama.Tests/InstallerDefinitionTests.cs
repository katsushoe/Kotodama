using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class InstallerDefinitionTests
{
    private static readonly string PackagePath = Path.Combine(AppContext.BaseDirectory, "installer", "Package.wxs");

    [Fact]
    public void Package_UsesRequiredManufacturer()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");

        document.Descendants(wix + "Package").Single()
            .Attribute("Manufacturer")?.Value.Should().Be("Akatsukisoft");
    }

    [Fact]
    public void Package_UpgradeStopsScheduledTaskBeforeFileValidation()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");

        var action = document.Descendants(wix + "CustomAction")
            .Single(element => (string?)element.Attribute("Id") == "StopKotodamaTask");
        var sequence = document.Descendants(wix + "InstallExecuteSequence")
            .Elements(wix + "Custom")
            .Single(element => (string?)element.Attribute("Action") == "StopKotodamaTask");

        action.Attribute("ExeCommand")?.Value.Should().Contain("schtasks.exe")
            .And.Contain("/End")
            .And.Contain("Kotodama MCP Server");
        action.Attribute("Return")?.Value.Should().Be("ignore");
        sequence.Attribute("Before")?.Value.Should().Be("InstallValidate");
        sequence.Attribute("Condition")?.Value.Should().Be("NOT REMOVE~=\"ALL\"");
    }
}
