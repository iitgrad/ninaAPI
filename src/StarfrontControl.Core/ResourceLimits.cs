// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace StarfrontControl.Core;

public static class ResourceLimits
{
    // Initial retention policy, not a measured N.I.N.A. process memory budget.
    public const int EventHistoryCapacity = 2048;
    public const int AdapterPendingTopicCapacity = 32;
}
