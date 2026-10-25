// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace ninaAPI.Properties
{
    // N.I.N.A. hosts both assemblies in the same process. Use our own settings
    // group rather than inheriting Advanced API's process-wide settings section.
    [System.Configuration.SettingsGroupName("StarfrontControl.Properties.Settings")]
    internal sealed partial class Settings { }
}
