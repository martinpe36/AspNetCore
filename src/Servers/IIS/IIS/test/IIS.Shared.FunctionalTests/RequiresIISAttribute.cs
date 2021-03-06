// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Win32;

namespace Microsoft.AspNetCore.Server.IISIntegration.FunctionalTests
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequiresIISAttribute : Attribute, ITestCondition
    {
        private static readonly (IISCapability Capability, string DllName)[] Modules =
        {
            (IISCapability.Websockets, "iiswsock.dll"),
            (IISCapability.WindowsAuthentication, "authsspi.dll"),
            (IISCapability.DynamicCompression, "compdyn.dll"),
            (IISCapability.ApplicationInitialization, "warmup.dll"),
            (IISCapability.TracingModule, "iisetw.dll"),
            (IISCapability.FailedRequestTracingModule, "iisfreb.dll"),
            (IISCapability.BasicAuthentication, "authbas.dll"),
        };

        private static readonly bool _isMetStatic;
        private static readonly string _skipReasonStatic;
        private static readonly bool _poolEnvironmentVariablesAvailable;
        private static readonly IISCapability _modulesAvailable;

        static RequiresIISAttribute()
        {
            if (Environment.GetEnvironmentVariable("ASPNETCORE_TEST_SKIP_IIS") == "true")
            {
                _skipReasonStatic = "Test skipped using ASPNETCORE_TEST_SKIP_IIS environment variable";
                return;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _skipReasonStatic = "IIS tests can only be run on Windows";
                return;
            }

            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator) && !SkipInVSTSAttribute.RunningInVSTS)
            {
                _skipReasonStatic += "The current console is not running as admin.";
                return;
            }

            if (!File.Exists(Path.Combine(Environment.SystemDirectory, "inetsrv", "w3wp.exe")) && !SkipInVSTSAttribute.RunningInVSTS)
            {
                _skipReasonStatic += "The machine does not have IIS installed.";
                return;
            }

            var ancmConfigPath = Path.Combine(Environment.SystemDirectory, "inetsrv", "config", "schema", "aspnetcore_schema_v2.xml");

            if (!File.Exists(ancmConfigPath) && !SkipInVSTSAttribute.RunningInVSTS)
            {
                _skipReasonStatic = "IIS Schema is not installed.";
                return;
            }

            XDocument ancmConfig;

            try
            {
                ancmConfig = XDocument.Load(ancmConfigPath);
            }
            catch
            {
                _skipReasonStatic = "Could not read ANCM schema configuration";
                return;
            }

            _isMetStatic = ancmConfig
                .Root
                .Descendants("attribute")
                .Any(n => "hostingModel".Equals(n.Attribute("name")?.Value, StringComparison.Ordinal));

            _skipReasonStatic = _isMetStatic ? null : "IIS schema needs to be upgraded to support ANCM.";

            foreach (var module in Modules)
            {
                if (File.Exists(Path.Combine(Environment.SystemDirectory, "inetsrv", module.DllName)) || SkipInVSTSAttribute.RunningInVSTS)
                {
                    _modulesAvailable |= module.Capability;
                }
            }

            var iisRegistryKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\InetStp", writable: false);
            if (iisRegistryKey == null)
            {
                _poolEnvironmentVariablesAvailable = false;
            }
            else
            {
                var majorVersion = (int)iisRegistryKey.GetValue("MajorVersion", -1);
                var minorVersion = (int)iisRegistryKey.GetValue("MinorVersion", -1);
                var version = new Version(majorVersion, minorVersion);
                _poolEnvironmentVariablesAvailable = version >= new Version(10, 0);
            }
        }

        public RequiresIISAttribute()
            : this(IISCapability.None) { }

        public RequiresIISAttribute(IISCapability capabilities)
        {
            IsMet = _isMetStatic;
            SkipReason = _skipReasonStatic;
            if (capabilities.HasFlag(IISCapability.PoolEnvironmentVariables))
            {
                IsMet &= _poolEnvironmentVariablesAvailable;
                if (!_poolEnvironmentVariablesAvailable)
                {
                    SkipReason += "The machine does allow for setting environment variables on application pools.";
                }
            }

            if (capabilities.HasFlag(IISCapability.ShutdownToken))
            {
                IsMet = false;
                SkipReason += "https://github.com/aspnet/IISIntegration/issues/1074";
            }

            foreach (var module in Modules)
            {
                if (capabilities.HasFlag(module.Capability))
                {
                    var available = _modulesAvailable.HasFlag(module.Capability);
                    IsMet &= available;
                    if (!available)
                    {
                        SkipReason += $"The machine does have {module.Capability} available.";
                    }
                }
            }
        }

        public bool IsMet { get; }
        public string SkipReason { get; }
    }
}
