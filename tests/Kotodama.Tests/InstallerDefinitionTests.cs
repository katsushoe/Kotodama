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
    public void Package_AllowsReinstallingSameVersionBuilds()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");

        var majorUpgrade = document.Descendants(wix + "MajorUpgrade").Single();
        majorUpgrade.Attribute("AllowSameVersionUpgrades")?.Value.Should().Be("yes");
        majorUpgrade.Attribute("Schedule")?.Value.Should().Be("afterInstallInitialize");
    }

    [Fact]
    public void Package_UpgradeStopsTaskAndProcessesBeforeFileValidation()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");

        var action = document.Descendants(wix + "CustomAction")
            .Single(element => (string?)element.Attribute("Id") == "StopKotodamaTask");
        var processAction = document.Descendants(wix + "CustomAction")
            .Single(element => (string?)element.Attribute("Id") == "StopKotodamaProcesses");

        action.Attribute("ExeCommand")?.Value.Should().Contain("schtasks.exe")
            .And.Contain("/End")
            .And.Contain("Kotodama MCP Server");
        action.Attribute("Return")?.Value.Should().Be("ignore");
        action.Attribute("Execute")?.Value.Should().Be("immediate");
        processAction.Attribute("ExeCommand")?.Value.Should().Contain("Where-Object Path -eq '[BinFolder]Kotodama.exe'")
            .And.Contain("Stop-Process -Force")
            .And.Contain("-WindowStyle Hidden")
            .And.NotContain("taskkill.exe");
        processAction.Attribute("Return")?.Value.Should().Be("ignore");
        processAction.Attribute("Execute")?.Value.Should().Be("immediate");
        document.Descendants(wix + "InstallUISequence").Should().BeEmpty();

        var sequence = document.Descendants(wix + "InstallExecuteSequence").Single();
        sequence.Elements(wix + "Custom")
            .Single(element => (string?)element.Attribute("Action") == "StopKotodamaTask")
            .Attribute("Before")?.Value.Should().Be("StopKotodamaProcesses");
        var processSequence = sequence.Elements(wix + "Custom")
            .Single(element => (string?)element.Attribute("Action") == "StopKotodamaProcesses");
        processSequence.Attribute("Before")?.Value.Should().Be("InstallValidate");
        processSequence.Attribute("Condition")?.Value.Should().Be("NOT REMOVE~=\"ALL\"");
    }
}
