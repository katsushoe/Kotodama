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
    public void Package_InstallsAutoStartLocalServiceThatRestartsOnFailure()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");

        var component = document.Descendants(wix + "Component")
            .Single(element => (string?)element.Attribute("Id") == "KotodamaService");
        component.Element(wix + "File")?.Attribute("Source")?.Value.Should().EndWith(@"\Kotodama.exe");
        component.Element(wix + "File")?.Attribute("KeyPath")?.Value.Should().Be("yes");

        var service = component.Element(wix + "ServiceInstall")!;
        service.Attribute("Name")?.Value.Should().Be(KotodamaApplication.WindowsServiceName);
        service.Attribute("Start")?.Value.Should().Be("auto");
        service.Attribute("Account")?.Value.Should().Be(@"NT AUTHORITY\LocalService");
        service.Attribute("Arguments")?.Value.Should().Be("--http");
        var recovery = document.Descendants(wix + "CustomAction")
            .Single(element => (string?)element.Attribute("Id") == "ConfigureServiceRecovery");
        recovery.Attribute("ExeCommand")?.Value.Should().Contain("sc.exe")
            .And.Contain("failure Kotodama")
            .And.Contain("actions= restart/60000/restart/60000/restart/300000");
        recovery.Attribute("Execute")?.Value.Should().Be("deferred");
        recovery.Attribute("Impersonate")?.Value.Should().Be("no");
        document.Descendants(wix + "Custom")
            .Single(element => (string?)element.Attribute("Action") == "ConfigureServiceRecovery")
            .Attribute("After")?.Value.Should().Be("InstallServices");

        var control = component.Element(wix + "ServiceControl")!;
        control.Attribute("Start")?.Value.Should().Be("install");
        control.Attribute("Stop")?.Value.Should().Be("both");
        control.Attribute("Remove")?.Value.Should().Be("uninstall");

        document.Descendants(wix + "ComponentRef")
            .Should().Contain(element => (string?)element.Attribute("Id") == "KotodamaService");
        document.Descendants(wix + "Exclude")
            .Should().ContainSingle(element => ((string?)element.Attribute("Files") ?? "").EndsWith(@"\Kotodama.exe"));
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
