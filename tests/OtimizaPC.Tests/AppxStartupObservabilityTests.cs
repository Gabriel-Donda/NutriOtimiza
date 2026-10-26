using System;
using System.IO;
using Microsoft.Win32;
using OtimizaPC.Tweaks.Apps;
using OtimizaPC.Tweaks.Startup;
using Xunit;

namespace OtimizaPC.Tests
{
    public sealed class AppxStartupObservabilityTests
    {
        [Theory]
        [InlineData(2, StartupApprovalState.Enabled)]
        [InlineData(3, StartupApprovalState.Disabled)]
        [InlineData(6, StartupApprovalState.Enabled)]
        [InlineData(7, StartupApprovalState.Disabled)]
        [InlineData(0, StartupApprovalState.InvalidData)]
        [InlineData(255, StartupApprovalState.InvalidData)]
        public void Approval_ValidatesStatus(int code, StartupApprovalState expected)
        {
            var data = new byte[12];
            data[0] = (byte)code;
            Assert.Equal(expected, StartupManager.ParseApproval(data));
        }

        [Fact]
        public void Approval_DistinguishesMissingAndMalformed()
        {
            Assert.Equal(StartupApprovalState.NotConfigured, StartupManager.ParseApproval(null));
            foreach (var value in new object[] { "2", new byte[0], new byte[] { 2 }, new byte[13] })
                Assert.Equal(StartupApprovalState.InvalidData, StartupManager.ParseApproval(value));
            var reserved = new byte[12];
            reserved[0] = 2;
            reserved[1] = 1;
            Assert.Equal(StartupApprovalState.InvalidData, StartupManager.ParseApproval(reserved));
        }

        [Theory]
        [InlineData(StartupSource.UserRun, RegistryHive.CurrentUser, "Run")]
        [InlineData(StartupSource.MachineRun, RegistryHive.LocalMachine, "Run")]
        [InlineData(StartupSource.MachineRun32, RegistryHive.LocalMachine, "Run32")]
        [InlineData(StartupSource.UserFolder, RegistryHive.CurrentUser, "StartupFolder")]
        [InlineData(StartupSource.CommonFolder, RegistryHive.LocalMachine, "StartupFolder")]
        public void Approval_UsesCorrectScopeAndMissingValueIsEnabled(StartupSource source, RegistryHive expected, string suffix)
        {
            var entry = new StartupEntry { Name = "Test", Source = source };
            StartupManager.ReadApproval(entry, (hive, path, name) =>
            {
                Assert.Equal(expected, hive);
                Assert.EndsWith("\\" + suffix, path);
                Assert.Equal(entry.Name, name);
                return null;
            });
            Assert.True(entry.IsEnabled);
            Assert.True(entry.IsStateKnown);
            Assert.Equal(StartupApprovalState.NotConfigured, entry.ApprovalState);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Approval_DistinguishesAccessDeniedFromReadFailure(bool denied)
        {
            var entry = new StartupEntry { Name = "Test" };
            StartupManager.ReadApproval(entry, (h, p, n) => { if (denied) throw new UnauthorizedAccessException("denied"); throw new IOException("read failed"); });
            Assert.Equal(denied ? StartupApprovalState.AccessDenied : StartupApprovalState.ReadError, entry.ApprovalState);
            Assert.False(entry.IsStateKnown);
            Assert.False(entry.IsEnabled);
            Assert.NotNull(entry.ApprovalMessage);
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("registration")]
        [InlineData("unverified")]
        [InlineData("timeout")]
        [InlineData("store")]
        [InlineData("success")]
        public void Reinstall_ReportsOutcomeAndPreservesRemovedUntilConfirmed(string scenario)
        {
            var folder = Path.Combine(Path.GetTempPath(), "OtimizaPC-Appx-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            try
            {
                string opened = null;
                var calls = 0;
                var service = new AppxService(Path.Combine(folder, "removed.json"), script =>
                {
                    calls++;
                    if (script.StartsWith("Add-AppxPackage"))
                    {
                        if (scenario == "timeout") throw new TimeoutException("timeout");
                        return new PowerShellResult { ExitCode = scenario == "registration" ? 1 : 0, Error = scenario == "registration" ? "registration denied" : null };
                    }
                    return new PowerShellResult { Output = scenario == "success" ? "[{\"Name\":\"Test.App\"}]" : "[]" };
                }, path => scenario != "missing", uri =>
                {
                    if (scenario == "store") throw new InvalidOperationException("store unavailable");
                    opened = uri;
                });
                var app = new RemovedApp { Name = "Test.App", DisplayName = "Test", PackageFamilyName = "Test_family", InstallLocation = folder };
                service.SaveRemoved(new System.Collections.Generic.List<RemovedApp> { app });
                var result = service.ReinstallDetailed(app);
                if (scenario == "success")
                {
                    Assert.Equal(ReinstallOutcome.Reinstalled, result.Outcome);
                    Assert.Empty(service.LoadRemoved());
                    Assert.Single(result.InstalledApps);
                    Assert.Null(opened);
                }
                else
                {
                    Assert.Single(service.LoadRemoved());
                    Assert.False(string.IsNullOrWhiteSpace(result.FallbackReason));
                    Assert.Equal(scenario == "store" ? ReinstallOutcome.Failed : ReinstallOutcome.OpenedStore, result.Outcome);
                    if (scenario == "store") Assert.Contains("store unavailable", result.Error);
                    else Assert.Contains("Test_family", opened);
                    if (scenario == "registration") Assert.Contains("registration denied", result.FallbackReason);
                    if (scenario == "timeout") Assert.Contains("timeout", result.FallbackReason);
                }
                Assert.Equal(scenario == "missing" ? 0 : scenario == "success" || scenario == "unverified" || scenario == "store" ? 2 : 1, calls);
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
