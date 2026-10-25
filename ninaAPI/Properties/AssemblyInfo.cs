using System.Runtime.Versioning;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: SupportedOSPlatform("windows7.0")]

// [MANDATORY] The following GUID is used as a unique identifier of the plugin
[assembly: Guid("897ef051-204c-4dfb-9697-d3209a15adc0")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new build of a plugin
[assembly: AssemblyVersion("0.4.28.0")]
[assembly: AssemblyFileVersion("0.4.28.0")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Starfront Control")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Private Starfront monitoring and control for N.I.N.A., derived from Advanced API.")]

// Your name
[assembly: AssemblyCompany("Kevin McQuown")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Starfront Control")]
[assembly: AssemblyCopyright("Copyright © 2025 Christian Palm; © 2026 Kevin McQuown")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
[assembly: AssemblyMetadata("Repository", "https://github.com/iitgrad/ninaAPI")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/iitgrad/ninaAPI/blob/dev/Changelog.md")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/iitgrad/ninaAPI")]


// The following attributes are optional for the official manifest meta data

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "API,Web")]

//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"Starfront Control is a private fork of Advanced API by Christian Palm.
Experimental development build; Windows simulation validation is pending.
It retains the upstream HTTP and WebSocket interfaces while resource management
and a new telemetry/control interface are developed.

## Upstream features:
- Listen to events like connections, meridian flips and much more
- TPPA integration (Requires TPPA to be installed)
- Livestack integration (Requires Livestack to be installed)
- Supports basic control over the equipment and application in general
- And much more!

---

Report Starfront Control issues [here](https://github.com/iitgrad/ninaAPI/issues).
Original Advanced API [HTTP documentation](https://christian-photo.github.io/github-page/projects/ninaAPI/v2/doc/api)
and [WebSocket documentation](https://github.com/christian-photo/ninaAPI/wiki/Websocket-V2).
**Thanks to szymon and notzeeg (discord) for their help!**")]


// Setting ComVisible to false makes the types in this assembly not visible
// to COM components. If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]
